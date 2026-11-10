using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.StudentInformationSystems;
using CampusFlow.ClearanceResets;
using NSubstitute;
using Microsoft.Extensions.Configuration;
using Xunit;
namespace CampusFlow.EntityFrameworkCore.Tests.BillApprovals;
public class BillClearanceServiceTests
{
    private sealed class Handler : HttpMessageHandler
    {
        public JsonObject Current=Sample();public JsonObject? Patch;public int Requests;public bool Ambiguous;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Requests++;Assert.Equal(request.RequestUri!.AbsolutePath.EndsWith("Authenticate") ? "app-elm-nonprod-usc-integrationapi.azurewebsites.net" : "np-usc-gateway.thesiscloud.com",request.RequestUri.Host);
            if(request.RequestUri.AbsolutePath.EndsWith("Authenticate"))return Response("{\"data\":{\"token\":\"fake\"}}");
            Assert.Equal("Registration",string.Join("",request.Headers.GetValues("Module")));
            if(request.Method==HttpMethod.Patch){Patch=JsonNode.Parse(await request.Content!.ReadAsStringAsync(token))!.AsObject();Current=Patch.DeepClone().AsObject();return Response("{\"isSuccess\":true}");}
            if(request.RequestUri.AbsolutePath.EndsWith("select-items"))return Response(Ambiguous?"{\"isSuccess\":true,\"data\":{\"statusLookup1\":[{\"uniqueId\":5,\"displayText\":\"Cleared\"}],\"statusLookup2\":[{\"uniqueId\":6045,\"displayText\":\"Cleared\"}]}}":"{\"isSuccess\":true,\"data\":{\"statusLookup2\":[{\"uniqueId\":6045,\"displayText\":\"Cleared\"}]}}");
            Assert.EndsWith("/user-defined/123",request.RequestUri.AbsolutePath);return Response(new JsonObject{["isSuccess"]=true,["data"]=Current.DeepClone()}.ToJsonString());
        }
        private static HttpResponseMessage Response(string body)=>new(HttpStatusCode.OK){Content=new StringContent(body,Encoding.UTF8,"application/json")};
    }
    private static JsonObject Sample()=>JsonNode.Parse("""
        {"studentStatusID":123,"uDefLookup1":20,"uDefLookup2":6046,"uDefLookup3":30,
        "uDefFreeForm1":"keep this","uDefFreeForm2":null,"uDefFreeForm3":"existing",
        "uDefCurr1":123.45,"uDefCurr2":null,"uDefCurr3":0,"uDefDate1":"2026-11-05T00:00:00",
        "uDefDate2":null,"uDefDate3":null,"uDefDate4":null,"uDefBit1":true,"uDefBit2":false,"uDefBit3":true}
        """)!.AsObject();
    private static ThesisElementsBillClearanceService Service(Handler handler,bool enabled=true,bool production=false,string? authUrl=null,IClearanceResetProtection? protection=null)
    {
        var p="StudentInformationSystems:Providers:ThesisElements:Api:";
        var configuration=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
        { ["App:EnvironmentLabel"]="NON-PRODUCTION",["BillApproval:ClearedStatus:Enabled"]=enabled.ToString(),
          [p+"BaseUrl"]=authUrl??(production?"https://app-elm-prod-usc-integrationapi.azurewebsites.net":"https://app-elm-nonprod-usc-integrationapi.azurewebsites.net"),
          [p+"RegistrationBaseUrl"]="https://np-usc-gateway.thesiscloud.com",[p+"TenantHost"]="el-np-nel.thesiscloud.com",[p+"Username"]="fake",[p+"Password"]="fake" }).Build();
        return new(configuration,new HttpClient(handler),(student,term,ct)=>{Assert.Equal(10,student);Assert.Equal(269,term);return Task.FromResult(123);},protection);
    }
    [Theory]
    [InlineData("http://app-elm-nonprod-usc-integrationapi.azurewebsites.net")]
    [InlineData("https://app-elm-nonprod-usc-integrationapi.azurewebsites.net/path")]
    [InlineData("https://app-elm-nonprod-usc-integrationapi.azurewebsites.net:444")]
    public async Task Unsafe_authentication_urls_never_receive_credentials(string url)
    {var handler=new Handler();await Assert.ThrowsAsync<InvalidOperationException>(()=>Service(handler,authUrl:url).MarkClearedAsync("10","269"));Assert.Equal(0,handler.Requests);}
    [Fact] public async Task Only_the_cleared_lookup_changes_and_every_other_field_is_preserved()
    {
        var handler=new Handler();var original=handler.Current.DeepClone().AsObject();await Service(handler).MarkClearedAsync("10","269");
        Assert.NotNull(handler.Patch);Assert.Equal(6045,handler.Patch["uDefLookup2"]!.GetValue<int>());
        foreach(var field in original.Where(x=>x.Key!="uDefLookup2"))Assert.True(JsonNode.DeepEquals(field.Value,handler.Patch[field.Key]),field.Key);
    }
    [Fact] public async Task Already_cleared_status_does_not_write_again()
    {var handler=new Handler();handler.Current["uDefLookup2"]=6045;await Service(handler).MarkClearedAsync("10","269");Assert.Null(handler.Patch);}
    [Fact] public async Task An_ambiguous_mapping_does_not_write()
    {var handler=new Handler{Ambiguous=true};await Assert.ThrowsAsync<InvalidOperationException>(()=>Service(handler).MarkClearedAsync("10","269"));Assert.Null(handler.Patch);}
    [Fact] public async Task A_status_from_another_term_identity_is_rejected()
    {var handler=new Handler();handler.Current["studentStatusID"]=999;await Assert.ThrowsAsync<InvalidOperationException>(()=>Service(handler).MarkClearedAsync("10","269"));Assert.Null(handler.Patch);}
    [Fact] public async Task Missing_existing_fields_stop_the_update_instead_of_resetting_them()
    {var handler=new Handler();handler.Current.Remove("uDefDate1");await Assert.ThrowsAsync<InvalidOperationException>(()=>Service(handler).MarkClearedAsync("10","269"));Assert.Null(handler.Patch);}
    [Theory][InlineData(false,false)][InlineData(true,true)] public async Task Disabled_and_cross_environment_settings_do_not_make_requests(bool enabled,bool production)
    {var handler=new Handler();await Assert.ThrowsAsync<InvalidOperationException>(()=>Service(handler,enabled,production).MarkClearedAsync("10","269"));Assert.Equal(0,handler.Requests);}
    [Fact]public async Task Approval_clearance_uses_the_same_student_term_lock_as_resets()
    {
        var handler=new Handler();var protection=Substitute.For<IClearanceResetProtection>();
        await Service(handler,protection:protection).MarkClearedAsync("10","269");
        await protection.Received(1).AcquireAsync(10,269,Arg.Any<CancellationToken>());Assert.Equal(6045,handler.Patch!["uDefLookup2"]!.GetValue<int>());
    }
    [Fact]public async Task Normal_completion_waits_for_a_short_reset_instead_of_losing_its_clearance_write()
    {
        var handler=new Handler();var protection=Substitute.For<IClearanceResetProtection>();var calls=0;
        protection.AcquireAsync(10,269,Arg.Any<CancellationToken>()).Returns(_=>++calls==1?Task.FromException<IAsyncDisposable>(new ClearanceUpdateBusyException()):Task.FromResult<IAsyncDisposable>(null!));
        await Service(handler,protection:protection).MarkClearedAsync("10","269");Assert.Equal(2,calls);Assert.NotNull(handler.Patch);
    }
}
