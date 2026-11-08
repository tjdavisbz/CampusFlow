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
    private static readonly ClearanceResetCandidate Candidate=new(10,269,123,"Spring 2027","B27C",1100);
    private static JsonObject Sample()=>JsonNode.Parse("""
    {"studentStatusID":123,"uDefLookup1":20,"uDefLookup2":6045,"uDefLookup3":30,
    "uDefFreeForm1":"keep","uDefFreeForm2":null,"uDefFreeForm3":"existing","uDefCurr1":123.45,"uDefCurr2":null,"uDefCurr3":0,
    "uDefDate1":"2026-11-05T00:00:00","uDefDate2":null,"uDefDate3":null,"uDefDate4":null,"uDefBit1":true,"uDefBit2":false,"uDefBit3":true}
    """)!.AsObject();
    private static JsonObject Course(int college=0,int category=1099,int term=269,int student=10)=>new(){["studentUID"]=student,["termCalendarID"]=term,["collegeID"]=college,["categoryID"]=category};
    private sealed class Handler:HttpMessageHandler
    {
        public JsonObject Current=Sample();public JsonArray Transcripts=new();public JsonArray Unofficial=new();public JsonObject? Patch;public bool FailReadback;public bool ChangeFields;public bool MissingMapping;public int Requests;private int _reads;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req,CancellationToken ct)
        {
            Requests++;var path=req.RequestUri!.AbsolutePath;Assert.Equal(path.EndsWith("Authenticate")?"app-elm-nonprod-usc-integrationapi.azurewebsites.net":"np-usc-gateway.thesiscloud.com",req.RequestUri.Host);
            if(path.EndsWith("Authenticate"))return Response(new JsonObject{["data"]=new JsonObject{["token"]="fake"}});
            Assert.Equal("Registration",string.Join("",req.Headers.GetValues("Module")));
            if(req.Method==HttpMethod.Patch){Patch=JsonNode.Parse(await req.Content!.ReadAsStringAsync(ct))!.AsObject();Current=Patch.DeepClone().AsObject();return Response(new JsonObject{["isSuccess"]=true});}
            JsonNode data;
            if(path.EndsWith("select-items"))data=MissingMapping?new JsonObject():JsonNode.Parse("""{"statusLookup2":[{"uniqueId":6045,"displayText":"Cleared"},{"uniqueId":6046,"displayText":"Not Cleared"}]}""")!;
            else if(path.Contains("transcripts"))data=Transcripts.DeepClone();
            else if(path.Contains("unofficial"))data=Unofficial.DeepClone();
            else {Assert.EndsWith("/user-defined/123",path);_reads++;if(ChangeFields&&_reads==2)Current["uDefFreeForm1"]="changed by staff";if(FailReadback&&Patch is not null)throw new HttpRequestException("Unknown after write");data=Current.DeepClone();}
            return Response(new JsonObject{["isSuccess"]=true,["data"]=data});
        }
        private static HttpResponseMessage Response(JsonObject body)=>new(HttpStatusCode.OK){Content=new StringContent(body.ToJsonString(),Encoding.UTF8,"application/json")};
    }
    private static IConfiguration Config(string? key=null,string? value=null)
    {
        const string p="StudentInformationSystems:Providers:ThesisElements:Api:";var values=new Dictionary<string,string?>
        {["App:EnvironmentLabel"]="NON-PRODUCTION",["BillApproval:ClearedStatus:Enabled"]="true",[p+"BaseUrl"]="https://app-elm-nonprod-usc-integrationapi.azurewebsites.net",[p+"RegistrationBaseUrl"]="https://np-usc-gateway.thesiscloud.com",[p+"TenantHost"]="el-np-nel.thesiscloud.com",[p+"Username"]="fake",[p+"Password"]="fake",["ConnectionStrings:ThesisElementsReadOnly"]="Server=sql-elm-nonprod-usc-replica.database.windows.net;Database=EL-NP-NEL-DB;User Id=fake;Password=fake;"};
        if(key is not null)values[key]=value;return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
    private static ElementsClearanceResetService Service(Handler h,Func<Task<bool>>? eligible=null,IConfiguration? config=null)=>new(config??Config(),new HttpClient(h),(_,_)=>Task.FromResult(new List<ClearanceResetCandidate>{Candidate}),(_,_,_)=>eligible?.Invoke()??Task.FromResult(true));
    [Theory][InlineData(0,1099,true)][InlineData(5,1099,false)][InlineData(0,1100,false)][InlineData(5,1100,false)]
    public void Transfer_exclusion_uses_both_college_and_category(int college,int category,bool blocks)
    {Assert.Equal(blocks,ElementsClearanceResetService.HasNelsonCourse(new JsonArray(Course(college,category)),Candidate));}
    [Fact]public void Another_terms_courses_do_not_block_this_term_but_unknown_identity_does()
    {Assert.False(ElementsClearanceResetService.HasNelsonCourse(new JsonArray(Course(term:268)),Candidate));Assert.Throws<InvalidOperationException>(()=>ElementsClearanceResetService.HasNelsonCourse(new JsonArray(Course(student:11)),Candidate));}
    [Theory][InlineData(false)][InlineData(true)]public async Task Either_official_or_unofficial_Nelson_courses_prevent_every_patch(bool unofficial)
    {var h=new Handler();(unofficial?h.Unofficial:h.Transcripts).Add(Course());var recorded=false;var result=await Service(h).ResetIfEligibleAsync(Candidate,_=>{recorded=true;return Task.FromResult(true);});Assert.StartsWith("Skipped",result.Outcome);Assert.Null(h.Patch);Assert.False(recorded);}
    [Fact]public async Task Transfer_only_records_allow_a_status_only_patch_and_require_audit_before_send()
    {
        var h=new Handler();h.Transcripts.Add(Course(5));h.Transcripts.Add(Course(category:1100));var before=h.Current.DeepClone().AsObject();var count=0;
        var result=await Service(h).ResetIfEligibleAsync(Candidate,a=>{Assert.Null(h.Patch);Assert.Equal(6045,JsonNode.Parse(a.BeforeJson)!["uDefLookup2"]!.GetValue<int>());count++;return Task.FromResult(true);});
        Assert.Equal("Verified Not Cleared",result.Outcome);Assert.Equal(1,count);Assert.Equal(6046,h.Patch!["uDefLookup2"]!.GetValue<int>());foreach(var field in before.Where(x=>x.Key!="uDefLookup2"))Assert.True(JsonNode.DeepEquals(field.Value,h.Patch[field.Key]),field.Key);
    }
    [Fact]public async Task Already_Not_Cleared_is_idempotently_skipped()
    {var h=new Handler();h.Current["uDefLookup2"]=6046;var result=await Service(h).ResetIfEligibleAsync(Candidate,_=>throw new Exception("Must not record"));Assert.StartsWith("Skipped",result.Outcome);Assert.Null(h.Patch);}
    [Theory][InlineData("uDefDate1")][InlineData("studentStatusID")]public async Task Missing_term_fields_or_wrong_identity_fail_closed(string field)
    {var h=new Handler();h.Current.Remove(field);await Assert.ThrowsAsync<InvalidOperationException>(()=>Service(h).ResetIfEligibleAsync(Candidate,_=>Task.FromResult(true)));Assert.Null(h.Patch);}
    [Fact]public async Task Staff_field_changes_during_checks_are_not_overwritten()
    {var h=new Handler{ChangeFields=true};var result=await Service(h).ResetIfEligibleAsync(Candidate,_=>Task.FromResult(true));Assert.StartsWith("Skipped",result.Outcome);Assert.Null(h.Patch);}
    [Fact]public async Task A_new_replica_course_or_scope_change_on_the_final_check_prevents_the_patch()
    {var h=new Handler();int checks=0;var result=await Service(h,()=>Task.FromResult(++checks==1)).ResetIfEligibleAsync(Candidate,_=>Task.FromResult(true));Assert.StartsWith("Skipped",result.Outcome);Assert.Null(h.Patch);Assert.Equal(2,checks);}
    [Fact]public async Task Turning_off_before_dispatch_or_an_audit_failure_prevents_the_patch()
    {var h=new Handler();await Service(h).ResetIfEligibleAsync(Candidate,_=>Task.FromResult(false));Assert.Null(h.Patch);await Assert.ThrowsAsync<InvalidOperationException>(()=>Service(h).ResetIfEligibleAsync(Candidate,_=>throw new InvalidOperationException("Storage failed")));Assert.Null(h.Patch);}
    [Fact]public async Task A_failed_readback_is_explicitly_unconfirmed_and_never_retried()
    {var h=new Handler{FailReadback=true};await Assert.ThrowsAsync<ClearanceResetUnconfirmedException>(()=>Service(h).ResetIfEligibleAsync(Candidate,_=>Task.FromResult(true)));Assert.NotNull(h.Patch);}
    [Fact]public async Task Missing_or_ambiguous_lookup_values_never_write()
    {var h=new Handler{MissingMapping=true};await Assert.ThrowsAsync<InvalidOperationException>(()=>Service(h).FindCandidatesAsync());Assert.Null(h.Patch);var choices=JsonNode.Parse("""{"statusLookup1":[{"uniqueId":1,"displayText":"Cleared"}],"statusLookup2":[{"uniqueId":2,"displayText":"Not Cleared"}]}""")!.AsObject();Assert.Throws<InvalidOperationException>(()=>ElementsClearanceResetService.ResolveMapping(choices));}
    [Theory]
    [InlineData("BillApproval:ClearedStatus:Enabled","false")]
    [InlineData("StudentInformationSystems:Providers:ThesisElements:Api:BaseUrl","https://app-elm-prod-usc-integrationapi.azurewebsites.net")]
    [InlineData("StudentInformationSystems:Providers:ThesisElements:Api:RegistrationBaseUrl","https://np-usc-gateway.thesiscloud.com/bad")]
    [InlineData("StudentInformationSystems:Providers:ThesisElements:Api:TenantHost","el-nel.thesiscloud.com")]
    [InlineData("ConnectionStrings:ThesisElementsReadOnly","Server=sql-elm-prod-usc-replica.database.windows.net;Database=EL-NEL-DB;")]
    [InlineData("App:EnvironmentLabel","unknown")]
    public async Task Environment_mismatches_and_disabled_updates_send_no_requests(string key,string value)
    {var h=new Handler();await Assert.ThrowsAsync<InvalidOperationException>(()=>Service(h,config:Config(key,value)).ResetIfEligibleAsync(Candidate,_=>Task.FromResult(true)));Assert.Equal(0,h.Requests);}
}
