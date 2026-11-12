using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.ClearanceResets;
using Microsoft.Extensions.Configuration;
using Xunit;
namespace CampusFlow.EntityFrameworkCore.Tests.ClearanceResets;
public class ElementsClearanceResetTests
{
    private static readonly ClearanceResetCandidate Candidate=new(10,269,123,"Spring 2027","B27C");
    private static JsonObject Sample()=>JsonNode.Parse("""
    {"studentStatusID":123,"uDefLookup1":20,"uDefLookup2":6045,"uDefLookup3":30,
    "uDefFreeForm1":"keep","uDefFreeForm2":null,"uDefFreeForm3":"existing","uDefCurr1":123.45,"uDefCurr2":null,"uDefCurr3":0,
    "uDefDate1":"2026-11-05T00:00:00","uDefDate2":null,"uDefDate3":null,"uDefDate4":null,"uDefBit1":true,"uDefBit2":false,"uDefBit3":true}
    """)!.AsObject();
    private sealed class Protection:IClearanceResetProtection
    {
        public Func<int,bool> Protected=_=>false;public int Checks,Leases,Released;
        public Task<bool> IsProtectedAsync(ClearanceResetCandidate c,CancellationToken ct=default)=>Task.FromResult(Protected(++Checks));
        public Task<HashSet<(int,int)>> ProtectedAsync(IReadOnlyCollection<ClearanceResetCandidate> rows,CancellationToken ct=default)=>Task.FromResult(Protected(++Checks)?rows.Select(x=>(x.StudentUid,x.TermId)).ToHashSet():new HashSet<(int,int)>());
        public Task<IAsyncDisposable> AcquireAsync(int student,int term,CancellationToken ct=default){Assert.Equal(10,student);Assert.Equal(269,term);Leases++;return Task.FromResult<IAsyncDisposable>(new Lease(this));}
        sealed class Lease(Protection owner):IAsyncDisposable{public ValueTask DisposeAsync(){owner.Released++;return ValueTask.CompletedTask;}}
    }
    private sealed class Handler:HttpMessageHandler
    {
        public JsonObject Current=Sample();public JsonObject? Patch;public bool FailReadback,ChangeFields,MissingMapping;public int Requests;private int reads;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req,CancellationToken ct)
        {
            Requests++;var path=req.RequestUri!.AbsolutePath;
            Assert.Equal(path.EndsWith("Authenticate")?"app-elm-nonprod-usc-integrationapi.azurewebsites.net":"np-usc-gateway.thesiscloud.com",req.RequestUri.Host);
            if(path.EndsWith("Authenticate"))return Response(new(){["data"]=new JsonObject{["token"]="fake"}});
            Assert.Equal("Registration",string.Join("",req.Headers.GetValues("Module")));
            if(req.Method==HttpMethod.Patch){Patch=JsonNode.Parse(await req.Content!.ReadAsStringAsync(ct))!.AsObject();Current=Patch.DeepClone().AsObject();return Response(new(){["isSuccess"]=true});}
            JsonNode data;
            if(path.EndsWith("select-items"))data=MissingMapping?new JsonObject():JsonNode.Parse("""{"statusLookup2":[{"uniqueId":0,"displayText":""},{"uniqueId":6045,"displayText":"Cleared"}]}""")!;
            else{Assert.EndsWith("/user-defined/123",path);reads++;if(ChangeFields&&reads==2)Current["uDefFreeForm1"]="changed by staff";if(FailReadback&&Patch is not null)throw new HttpRequestException("Unknown after write");data=Current.DeepClone();}
            return Response(new(){["isSuccess"]=true,["data"]=data});
        }
        private static HttpResponseMessage Response(JsonObject body)=>new(HttpStatusCode.OK){Content=new StringContent(body.ToJsonString(),Encoding.UTF8,"application/json")};
    }
    private static IConfiguration Config(string? key=null,string? value=null)
    {
        const string p="StudentInformationSystems:Providers:ThesisElements:Api:";var values=new Dictionary<string,string?>
        {["App:EnvironmentLabel"]="NON-PRODUCTION",["BillApproval:ClearedStatus:Enabled"]="true",[p+"BaseUrl"]="https://app-elm-nonprod-usc-integrationapi.azurewebsites.net",[p+"RegistrationBaseUrl"]="https://np-usc-gateway.thesiscloud.com",[p+"TenantHost"]="el-np-nel.thesiscloud.com",[p+"Username"]="fake",[p+"Password"]="fake",["ConnectionStrings:ThesisElementsReadOnly"]="Server=sql-elm-nonprod-usc-replica.database.windows.net;Database=EL-NP-NEL-DB;User Id=fake;Password=fake;"};
        if(key is not null)values[key]=value;return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
    private static ElementsClearanceResetService Service(Handler h,Protection? p=null,Func<Task<bool>>? eligible=null,IConfiguration? config=null)=>
        new(config??Config(),new HttpClient(h),(_,_)=>Task.FromResult(new List<ClearanceResetCandidate>{Candidate}),(_,_,_)=>eligible?.Invoke()??Task.FromResult(true),p??new());
    [Fact]public async Task No_bill_resets_exactly_the_five_reviewed_fields_under_the_shared_lease()
    {
        var h=new Handler();var p=new Protection();var before=h.Current.DeepClone().AsObject();
        var result=await Service(h,p).ResetIfEligibleAsync(Candidate,a=>{Assert.Null(h.Patch);Assert.Equal(0,p.Released);Assert.True(JsonNode.Parse(a.DesiredJson)!.AsObject().ContainsKey("uDefLookup2"));Assert.Equal(0,JsonNode.Parse(a.DesiredJson)!["uDefLookup2"]!.GetValue<int>());return Task.FromResult(true);});
        Assert.Equal("Verified blank",result.Outcome);Assert.True(h.Patch!.ContainsKey("uDefLookup2"));Assert.Equal(0,h.Patch["uDefLookup2"]!.GetValue<int>());
        Assert.Null(h.Patch["uDefDate1"]);Assert.Equal("",h.Patch["uDefFreeForm1"]!.GetValue<string>());
        Assert.False(h.Patch["uDefBit1"]!.GetValue<bool>());Assert.False(h.Patch["uDefBit2"]!.GetValue<bool>());
        string[] changed=["uDefLookup2","uDefDate1","uDefFreeForm1","uDefBit1","uDefBit2"];
        foreach(var field in before.Where(x=>!changed.Contains(x.Key)))Assert.True(JsonNode.DeepEquals(field.Value,h.Patch[field.Key]),field.Key);
        Assert.Equal(1,p.Leases);Assert.Equal(1,p.Released);
    }
    private static List<(string Property,string Label)> Labels()=>[("UDefLookup2","Cleared Status"),
        ("uDefDate1","Disable Blackboard"),("uDefBit2","Payment Plan?"),("uDefBit1","Non-Returning"),("uDefFreeForm1","Non-Returning Reason")];
    [Fact]public void Reviewed_field_types_are_mapped_by_verified_configuration_labels()
    {ElementsClearanceResetService.ValidateResetFields(Labels());}
    [Fact]public void Renamed_missing_or_duplicate_field_definitions_fail_closed()
    {
        var fields=Labels();fields[1]=("uDefDate1","Different field");Assert.Throws<InvalidOperationException>(()=>ElementsClearanceResetService.ValidateResetFields(fields));
        fields=Labels();fields.RemoveAt(1);Assert.Throws<InvalidOperationException>(()=>ElementsClearanceResetService.ValidateResetFields(fields));
        fields=Labels();fields.Add(("uDefDate2","Disable Blackboard"));Assert.Throws<InvalidOperationException>(()=>ElementsClearanceResetService.ValidateResetFields(fields));
    }
    [Fact]public async Task A_field_configuration_check_failure_prevents_all_writes()
    {
        var h=new Handler();var service=new ElementsClearanceResetService(Config(),new HttpClient(h),
            (_,_)=>Task.FromResult(new List<ClearanceResetCandidate>{Candidate}),(_,_,_)=>Task.FromResult(true),new Protection(),
            _=>Task.FromException(new InvalidOperationException("Labels changed")));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.ResetIfEligibleAsync(Candidate,_=>Task.FromResult(true)));
        Assert.Null(h.Patch);
    }
    [Fact]public async Task Approved_or_manually_exempt_records_are_excluded_from_preview_and_execution()
    {
        var h=new Handler();var p=new Protection{Protected=_=>true};Assert.Empty(await Service(h,p).FindCandidatesAsync());
        var result=await Service(h,p).ResetIfEligibleAsync(Candidate,_=>throw new Exception("No audit/write"));Assert.StartsWith("Skipped",result.Outcome);Assert.Null(h.Patch);
    }
    [Fact]public async Task A_new_approval_or_exception_during_the_audit_prevents_dispatch()
    {var h=new Handler();var p=new Protection();await Service(h,p).ResetIfEligibleAsync(Candidate,_=>{p.Protected=_=>true;return Task.FromResult(true);});Assert.Null(h.Patch);}
    [Theory][InlineData(6046)][InlineData(0)][InlineData(null)]public async Task Not_Cleared_and_blank_are_never_bulk_repaired(int? value)
    {var h=new Handler();h.Current["uDefLookup2"]=value;var result=await Service(h).ResetIfEligibleAsync(Candidate,_=>throw new Exception("No write"));Assert.StartsWith("Skipped",result.Outcome);Assert.Null(h.Patch);}
    [Theory][InlineData("uDefDate1")][InlineData("studentStatusID")]public async Task Missing_fields_and_wrong_identity_fail_closed(string field)
    {var h=new Handler();h.Current.Remove(field);await Assert.ThrowsAsync<InvalidOperationException>(()=>Service(h).ResetIfEligibleAsync(Candidate,_=>Task.FromResult(true)));Assert.Null(h.Patch);}
    [Fact]public async Task Concurrent_staff_changes_are_preserved()
    {var h=new Handler{ChangeFields=true};var result=await Service(h).ResetIfEligibleAsync(Candidate,_=>Task.FromResult(true));Assert.StartsWith("Skipped",result.Outcome);Assert.Null(h.Patch);}
    [Fact]public async Task Scope_changes_before_audit_prevent_the_patch()
    {var h=new Handler();int checks=0;await Service(h,eligible:()=>Task.FromResult(++checks==1)).ResetIfEligibleAsync(Candidate,_=>Task.FromResult(true));Assert.Null(h.Patch);Assert.Equal(2,checks);}
    [Fact]public async Task Turning_off_or_audit_failure_prevents_the_patch()
    {var h=new Handler();await Service(h).ResetIfEligibleAsync(Candidate,_=>Task.FromResult(false));Assert.Null(h.Patch);await Assert.ThrowsAsync<InvalidOperationException>(()=>Service(h).ResetIfEligibleAsync(Candidate,_=>throw new InvalidOperationException("Storage failed")));Assert.Null(h.Patch);}
    [Fact]public async Task Unknown_readback_is_recorded_as_unconfirmed_without_retry()
    {var h=new Handler{FailReadback=true};var p=new Protection();await Assert.ThrowsAsync<ClearanceResetUnconfirmedException>(()=>Service(h,p).ResetIfEligibleAsync(Candidate,_=>Task.FromResult(true)));Assert.NotNull(h.Patch);Assert.Equal(1,p.Released);}
    [Fact]public async Task Failed_protection_lookup_stops_before_dispatch()
    {var h=new Handler();var p=new Protection{Protected=_=>throw new InvalidOperationException("Approval source unavailable")};await Assert.ThrowsAsync<InvalidOperationException>(()=>Service(h,p).ResetIfEligibleAsync(Candidate,_=>Task.FromResult(true)));Assert.Null(h.Patch);}
    [Fact]public async Task Missing_or_ambiguous_Cleared_mapping_never_writes()
    {var h=new Handler{MissingMapping=true};await Assert.ThrowsAsync<InvalidOperationException>(()=>Service(h).FindCandidatesAsync());var choices=JsonNode.Parse("""{"statusLookup1":[{"uniqueId":1,"displayText":"Cleared"}],"statusLookup2":[{"uniqueId":2,"displayText":"Cleared"}]}""")!.AsObject();Assert.Throws<InvalidOperationException>(()=>ElementsClearanceResetService.ResolveMapping(choices));}
    [Theory]
    [InlineData("BillApproval:ClearedStatus:Enabled","false")]
    [InlineData("StudentInformationSystems:Providers:ThesisElements:Api:BaseUrl","https://app-elm-prod-usc-integrationapi.azurewebsites.net")]
    [InlineData("StudentInformationSystems:Providers:ThesisElements:Api:RegistrationBaseUrl","https://np-usc-gateway.thesiscloud.com/bad")]
    [InlineData("StudentInformationSystems:Providers:ThesisElements:Api:TenantHost","el-nel.thesiscloud.com")]
    [InlineData("ConnectionStrings:ThesisElementsReadOnly","Server=sql-elm-prod-usc-replica.database.windows.net;Database=EL-NEL-DB;")]
    [InlineData("App:EnvironmentLabel","unknown")]
    public async Task Mismatched_environments_and_disabled_updates_make_no_requests(string key,string value)
    {var h=new Handler();await Assert.ThrowsAsync<InvalidOperationException>(()=>Service(h,config:Config(key,value)).ResetIfEligibleAsync(Candidate,_=>Task.FromResult(true)));Assert.Equal(0,h.Requests);}
}
