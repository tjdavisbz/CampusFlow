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
using NSubstitute;
using Xunit;
namespace CampusFlow.MealPlanTaxes;
public class MealPlanTaxGatewayTests
{
    private static readonly MealPlanTaxTarget Target=new(10,269,42,8,2475m);
    private static Dictionary<string,string?> Settings()=>new(){
        ["MealPlanTax:Enabled"]="true",["MealPlanTax:EnableNonProductionWrites"]="true",["MealPlanTax:EnableProductionWrites"]="false",["App:EnvironmentLabel"]="NON-PRODUCTION",
        ["StudentInformationSystems:Providers:ThesisElements:Api:BaseUrl"]="https://app-elm-nonprod-usc-integrationapi.azurewebsites.net",["StudentInformationSystems:Providers:ThesisElements:Api:RegistrationBaseUrl"]="https://np-usc-gateway.thesiscloud.com",["StudentInformationSystems:Providers:ThesisElements:Api:TenantHost"]="el-np-nel.thesiscloud.com",["StudentInformationSystems:Providers:ThesisElements:Api:Username"]="test",["StudentInformationSystems:Providers:ThesisElements:Api:Password"]="test",
        ["ConnectionStrings:ThesisElementsReadOnly"]="Server=sql-elm-nonprod-usc-replica.database.windows.net;Database=EL-NP-NEL-DB;"};
    private static (ElementsMealPlanTaxGateway,Handler,IElementsMealPlanTaxReadModel,IStudentInformationSystemMealPlanRemovalService) Setup(Dictionary<string,string?>? values=null)
    {
        var reader=Substitute.For<IElementsMealPlanTaxReadModel>();reader.IsInScopeAsync(269,Arg.Any<CancellationToken>()).Returns(true);reader.DestinationAsync(Arg.Any<CancellationToken>()).Returns(new MealPlanTaxDestination(77,261,0));reader.ReadAsync(Arg.Any<MealPlanTaxTarget>(),Arg.Any<MealPlanTaxDestination>(),Arg.Any<CancellationToken>()).Returns(new MealPlanTaxReportedCharge(null,null));
        var assignments=Substitute.For<IStudentInformationSystemMealPlanRemovalService>();assignments.GetAssignmentsAsync("10",Arg.Any<CancellationToken>()).Returns(new[]{new StudentMealPlanAssignment(42,8,269,"12 Meal Plan - Spr 27",2475m),new StudentMealPlanAssignment(43,9,268,"Old term",2475m),new StudentMealPlanAssignment(44,10,269,"No meals",0m)});
        var http=new Handler();var gateway=new ElementsMealPlanTaxGateway(new ConfigurationBuilder().AddInMemoryCollection(values??Settings()).Build(),new MemoryCache(new MemoryCacheOptions()),assignments,reader,new HttpClient(http));return(gateway,http,reader,assignments);
    }
    [Fact]public async Task Writes_only_the_separate_rounded_tax_to_the_matching_gateway()
    {
        var(gateway,http,_,_)=Setup();await gateway.CreateAsync(Target);var sent=Assert.Single(http.Posts);
        Assert.Equal("np-usc-gateway.thesiscloud.com",sent.Host);Assert.Equal("Billing",sent.Module);Assert.Equal("el-np-nel.thesiscloud.com",sent.Tenant);
        Assert.Equal(204.19m,sent.Body["amount"]!.GetValue<decimal>());Assert.Equal(261,sent.Body["transDocID"]!.GetValue<int>());Assert.Equal(269,sent.Body["termID"]!.GetValue<int>());Assert.Equal(10,sent.Body["studentID"]!.GetValue<int>());Assert.Equal(Target.Reference,sent.Body["refNo"]!.GetValue<string>());Assert.Equal("DEBIT",sent.Body["transactionTypeID"]!.GetValue<string>());
    }
    [Fact]public async Task Targets_only_selected_term_paid_assignments()
    {var(gateway,_,_,_)=Setup();Assert.Equal(Target,Assert.Single(await gateway.TargetsAsync(10,269)));}
    [Fact]public async Task Earlier_terms_are_read_only_and_do_not_load_assignments()
    {var(gateway,http,_,assignments)=Setup();Assert.Empty(await gateway.TargetsAsync(10,268));await assignments.DidNotReceiveWithAnyArgs().GetAssignmentsAsync(default!,default);Assert.Empty(http.Posts);}
    [Fact]public async Task Existing_verified_tax_is_never_created_again()
    {var(gateway,http,_,_)=Setup();http.Entries.Add(Entry());await gateway.CreateAsync(Target);Assert.Empty(http.Posts);}
    [Fact]public async Task Posted_tax_does_not_trigger_a_batch_post()
    {var(gateway,http,reader,_)=Setup();reader.ReadAsync(Arg.Any<MealPlanTaxTarget>(),Arg.Any<MealPlanTaxDestination>(),Arg.Any<CancellationToken>()).Returns(new MealPlanTaxReportedCharge("Ledger:456",null));Assert.Equal("Ledger:456",await gateway.FindAsync(Target));Assert.Empty(http.Posts);Assert.Equal(0,http.Calls);}
    [Theory][InlineData("studentUID",11)][InlineData("batchMasterID",78)][InlineData("termCalendarID",268)][InlineData("transDOCID",165)][InlineData("amount",999)]
    public async Task Unexpected_identity_or_amount_cannot_be_replaced_or_duplicated(string field,int value)
    {var(gateway,http,_,_)=Setup();var row=Entry();row[field]=value;http.Entries.Add(row);await Assert.ThrowsAsync<InvalidOperationException>(()=>gateway.FindAsync(Target));Assert.Empty(http.Posts);}
    [Fact]public async Task Duplicate_references_cannot_be_automatically_repaired()
    {var(gateway,http,_,_)=Setup();http.Entries.Add(Entry());http.Entries.Add(Entry());await Assert.ThrowsAsync<InvalidOperationException>(()=>gateway.FindAsync(Target));Assert.Empty(http.Posts);}
    [Fact]public async Task Changed_plan_amount_cannot_be_posted_from_a_stale_review()
    {var(gateway,http,_,assignments)=Setup();assignments.GetAssignmentsAsync("10",Arg.Any<CancellationToken>()).Returns(new[]{new StudentMealPlanAssignment(42,8,269,"Changed plan",2575m)});await Assert.ThrowsAsync<InvalidOperationException>(()=>gateway.CreateAsync(Target));Assert.Empty(http.Posts);}
    [Theory][InlineData("MealPlanTax:Enabled","false")][InlineData("MealPlanTax:EnableProductionWrites","true")][InlineData("MealPlanTax:EnableNonProductionWrites","false")][InlineData("StudentInformationSystems:Providers:ThesisElements:Api:TenantHost","el-nel.thesiscloud.com")][InlineData("ConnectionStrings:ThesisElementsReadOnly","Server=sql-elm-prod-usc-replica.database.windows.net;Database=EL-NEL-DB;")]
    public async Task Cross_environment_or_disabled_flags_prevent_even_preflight_calls(string key,string value)
    {var settings=Settings();settings[key]=value;var(gateway,http,reader,_)=Setup(settings);await Assert.ThrowsAsync<InvalidOperationException>(()=>gateway.TargetsAsync(10,269));await reader.DidNotReceiveWithAnyArgs().IsInScopeAsync(default,default);Assert.Equal(0,http.Calls);}
    [Fact]public async Task A_lost_post_response_is_not_retried_by_the_gateway()
    {var(gateway,http,_,_)=Setup();http.LosePost=true;await Assert.ThrowsAsync<HttpRequestException>(()=>gateway.CreateAsync(Target));Assert.Single(http.Posts);}
    private static JsonObject Entry()=>new(){["billingBatchID"]=123,["batchMasterID"]=77,["studentUID"]=10,["termCalendarID"]=269,["transDOCID"]=261,["amount"]=204.19m,["refNo"]=Target.Reference};
    private sealed class Handler:HttpMessageHandler
    {
        public int Calls;public bool LosePost;public JsonArray Entries=new();public List<(string Host,string Module,string Tenant,JsonObject Body)> Posts=[];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Calls++;JsonNode data;
            if(request.RequestUri!.AbsolutePath.Contains("Authenticate"))data=new JsonObject{["token"]="test"};
            else if(request.Method==HttpMethod.Get)data=new JsonObject{["batchEntriesList"]=Entries.DeepClone()};
            else
            {
                Posts.Add((request.RequestUri.Host,request.Headers.GetValues("Module").Single(),request.Headers.GetValues("TenantHost").Single(),JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!.AsObject()));
                if(LosePost)throw new HttpRequestException("Unconfirmed request");Entries.Add(Entry());data=JsonValue.Create(123)!;
            }
            return new(HttpStatusCode.OK){Content=new StringContent(new JsonObject{["isSuccess"]=true,["data"]=data}.ToJsonString(),Encoding.UTF8,"application/json")};
        }
    }
}
