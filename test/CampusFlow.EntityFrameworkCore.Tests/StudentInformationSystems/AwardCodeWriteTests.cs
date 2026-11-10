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
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CampusFlow.EntityFrameworkCore.Tests.StudentInformationSystems;

public sealed class AwardCodeWriteTests
{
    private const string Api = "StudentInformationSystems:Providers:ThesisElements:Api:";
    private static AwardCodeMismatch Expected() => new(123,456,"Test","Test Student","Spring 2027","UNSUB ADD",3,7,true,false,269);
    private static JsonObject Award() => ThesisElementsFinancialAidDecisionServiceTests.Award();
    private static IConfiguration Config(string? changed=null,string? value=null)
    {
        var values=new Dictionary<string,string?> {
            ["App:EnvironmentLabel"]="NON-PRODUCTION",
            ["ConnectionStrings:ThesisElementsReadOnly"]="Server=sql-elm-nonprod-usc-replica.database.windows.net;Database=EL-NP-NEL-DB",
            ["AwardCodeMaintenance:Enabled"]="true",["AwardCodeMaintenance:EnableNonProductionWrites"]="true",
            [Api+"BaseUrl"]="https://app-elm-nonprod-usc-integrationapi.azurewebsites.net",
            [Api+"TenantHost"]="el-np-nel.thesiscloud.com" };
        if(changed!=null)values[changed]=value;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
    private static ThesisElementsAwardCodeMaintenance Service(Handler handler,AwardCodeMismatch? preview=null,IConfiguration? config=null)
    {
        var cache=new MemoryCache(new MemoryCacheOptions());cache.Set("AwardCodeMaintenance:el-np-nel.thesiscloud.com","test");
        return new(config??Config(),cache,new HttpClient(handler),(_,_,_)=>Task.FromResult<IReadOnlyList<AwardCodeMismatch>>([preview??Expected()]));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Only_code_and_billing_flag_change_and_the_input_is_untouched(bool nullDate)
    {
        var award=Award();if(nullDate){award["distributionDate"]=null;award["codaei"]=false;}
        var original=award.DeepClone();var body=ThesisElementsAwardCodeMaintenance.BuildReconciliationBody(award,Expected());
        Assert.Equal(7,body["code1Id"]!.GetValue<int>());Assert.Equal("No",body["showOnBillingStatement"]!.GetValue<string>());
        foreach(var field in award.Where(x=>x.Key is not ("financialAwardId" or "code1Id" or "showOnBillingStatement")))
            Assert.True(JsonNode.DeepEquals(field.Value,body[field.Key=="distributionDate"?"transDate":field.Key]),field.Key);
        Assert.True(JsonNode.DeepEquals(original,award));
    }
    [Theory] [InlineData("distributionDate")] [InlineData("codaei")] [InlineData("studentAccepted")] [InlineData("amount")]
    public void Missing_protected_fields_stop_before_writing(string field)
    {var a=Award();a.Remove(field);Assert.Throws<InvalidOperationException>(()=>ThesisElementsAwardCodeMaintenance.BuildReconciliationBody(a,Expected()));}
    [Theory] [InlineData("financialAwardId")] [InlineData("studentUid")] [InlineData("termCalendarId")]
    public void Wrong_identity_is_rejected(string field)
    {var a=Award();a[field]=999;Assert.Throws<InvalidOperationException>(()=>ThesisElementsAwardCodeMaintenance.BuildReconciliationBody(a,Expected()));}
    [Theory] [InlineData(true,1)] [InlineData(false,0)]
    public void Existing_acceptance_uses_the_write_contract_without_changing_its_meaning(bool accepted,int expected)
    {var a=Award();a["studentAccepted"]=accepted;Assert.Equal(expected,ThesisElementsAwardCodeMaintenance.BuildReconciliationBody(a,Expected())["studentAccepted"]!.GetValue<int>());}
    [Fact] public void Empty_reference_code_uses_the_Elements_zero_option()
    {var a=Award();Assert.Equal(0,ThesisElementsAwardCodeMaintenance.BuildReconciliationBody(a,Expected() with{ReferenceCodeId=null})["code1Id"]!.GetValue<int>());}
    [Fact] public void Locked_awards_are_rejected()
    {var a=Award();a["lock"]=true;Assert.Throws<InvalidOperationException>(()=>ThesisElementsAwardCodeMaintenance.BuildReconciliationBody(a,Expected()));}
    [Fact] public async Task Successful_write_is_read_back_with_every_protected_field_intact()
    {var h=new Handler();await Service(h).UpdateAsync(Expected());Assert.Equal(1,h.Writes);Assert.Equal(3,h.Reads);}
    [Theory] [InlineData("distributionDate")] [InlineData("codaei")] [InlineData("amount")] [InlineData("studentAccepted")]
    public async Task Any_protected_readback_change_stops_the_batch(string field)
    {var h=new Handler{Corrupt=field};await Assert.ThrowsAsync<InvalidOperationException>(()=>Service(h).UpdateAsync(Expected()));Assert.Equal(1,h.Writes);}
    [Fact] public async Task A_live_edit_between_preview_and_put_is_not_overwritten()
    {var h=new Handler{ChangeBetweenReads=true};await Assert.ThrowsAsync<InvalidOperationException>(()=>Service(h).UpdateAsync(Expected()));Assert.Equal(0,h.Writes);}
    [Fact] public async Task Changed_reference_values_require_another_preview()
    {var h=new Handler();await Assert.ThrowsAsync<InvalidOperationException>(()=>Service(h,Expected() with{ReferenceCodeId=8}).UpdateAsync(Expected()));Assert.Equal(0,h.Reads);Assert.Equal(0,h.Writes);}
    [Fact] public async Task Lost_reply_is_not_resent_and_existing_success_is_recognized()
    {var h=new Handler{LoseReply=true};var s=Service(h);await Assert.ThrowsAsync<HttpRequestException>(()=>s.UpdateAsync(Expected()));await s.UpdateAsync(Expected());Assert.Equal(1,h.Writes);}
    [Theory]
    [InlineData("AwardCodeMaintenance:Enabled","false")]
    [InlineData("AwardCodeMaintenance:EnableProductionWrites","true")]
    [InlineData(Api+"BaseUrl","https://app-elm-prod-usc-integrationapi.azurewebsites.net")]
    [InlineData(Api+"TenantHost","el-nel.thesiscloud.com")]
    [InlineData("ConnectionStrings:ThesisElementsReadOnly","Server=sql-elm-prod-usc-replica.database.windows.net;Database=EL-NEL-DB")]
    public async Task Disabled_or_mixed_environments_cannot_make_requests(string key,string value)
    {var h=new Handler();var s=Service(h,config:Config(key,value));Assert.False(s.CanWrite);await Assert.ThrowsAsync<InvalidOperationException>(()=>s.UpdateAsync(Expected()));Assert.Equal(0,h.Reads);Assert.Equal(0,h.Writes);}
    private sealed class Handler:HttpMessageHandler
    {
        public int Reads,Writes; public string? Corrupt;public bool LoseReply,ChangeBetweenReads;
        private readonly JsonObject _award=Award();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Assert.Equal("FinancialAid",request.Headers.GetValues("Module").Single());
            if(request.Method==HttpMethod.Put)
            {
                Writes++;var body=JsonNode.Parse(await request.Content!.ReadAsStringAsync(token))!.AsObject();
                foreach(var field in body)_award[field.Key=="transDate"?"distributionDate":field.Key]=field.Value?.DeepClone();
                if(Corrupt=="studentAccepted")_award[Corrupt]=true;else if(Corrupt!=null)_award[Corrupt]=null;
                if(LoseReply)throw new HttpRequestException("Synthetic lost response");
                return Response(new JsonObject{["isSuccess"]=true});
            }
            Reads++;if(ChangeBetweenReads&&Reads==2)_award["amount"]=900m;
            return Response(new JsonObject{["isSuccess"]=true,["data"]=new JsonObject{["getFinancialAwardDetails"]=new JsonArray(_award.DeepClone())}});
        }
        private static HttpResponseMessage Response(JsonNode node)=>new(HttpStatusCode.OK){Content=new StringContent(node.ToJsonString(),Encoding.UTF8,"application/json")};
    }
}
