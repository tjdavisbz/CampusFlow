using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.Housing;
using CampusFlow.StudentInformationSystems;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Xunit;

namespace CampusFlow.MealPlanTaxes;

public class SharedMealPlanBatchTests
{
    private static readonly MealPlanTaxTarget Target=new(10,269,42,9,2475m);
    private static Dictionary<string,string?> Settings()=>new()
    {
        ["MealPlans:SharedBatchMasterId"]="300",["MealPlanTax:TransactionDocumentId"]="261",
        ["MealPlanTax:Enabled"]="true",["MealPlanTax:EnableNonProductionWrites"]="true",["MealPlanTax:EnableProductionWrites"]="false",["App:EnvironmentLabel"]="NON-PRODUCTION",
        ["StudentInformationSystems:Providers:ThesisElements:Api:BaseUrl"]="https://app-elm-nonprod-usc-integrationapi.azurewebsites.net",
        ["StudentInformationSystems:Providers:ThesisElements:Api:RegistrationBaseUrl"]="https://np-usc-gateway.thesiscloud.com",
        ["StudentInformationSystems:Providers:ThesisElements:Api:TenantHost"]="el-np-nel.thesiscloud.com",
        ["StudentInformationSystems:Providers:ThesisElements:Api:Username"]="test",["StudentInformationSystems:Providers:ThesisElements:Api:Password"]="test",
        ["ConnectionStrings:ThesisElementsReadOnly"]="Server=sql-elm-nonprod-usc-replica.database.windows.net;Database=EL-NP-NEL-DB;"
    };
    private sealed class Fixture
    {
        public readonly Handler Http=new();
        public readonly IElementsMealPlanBatchReadModel Read=Substitute.For<IElementsMealPlanBatchReadModel>();
        public readonly IElementsMealPlanTaxReadModel Tax=Substitute.For<IElementsMealPlanTaxReadModel>();
        public readonly IStudentInformationSystemMealPlanRemovalService Assignments=Substitute.For<IStudentInformationSystemMealPlanRemovalService>();
        public ElementsMealPlanBatchService Service {get;}
        public Fixture(Dictionary<string,string?>? settings=null)
        {
            Tax.DestinationAsync(Arg.Any<CancellationToken>()).Returns(new MealPlanTaxDestination(300,261,8047,300,200));
            Assignments.GetAssignmentsAsync(Arg.Any<string>(),Arg.Any<CancellationToken>()).Returns(c=>new[]{new StudentMealPlanAssignment(42,9,269,"Plan",2475m)});
            Read.ReadAsync(Arg.Any<MealPlanTaxTarget>(),Arg.Any<MealPlanTaxDestination>(),Arg.Any<CancellationToken>()).Returns(c=>
            {
                var target=c.Arg<MealPlanTaxTarget>();
                var entries=Http.Rows.Where(r=>r["studentUID"]!.GetValue<int>()==target.StudentUid && r["termCalendarID"]!.GetValue<int>()==target.TermId)
                    .Select(r=>new MealPlanRemovalEntry(r["billingBatchID"]!.GetValue<int>(),r["batchMasterID"]!.GetValue<int>(),r["transDOCID"]!.GetValue<int>(),
                        r["arTypeID"]!.GetValue<int>(),r["amount"]!.GetValue<decimal>(),r["refNo"]!.GetValue<string>(),r["description"]!.GetValue<string>(),r["transDOCID"]!.GetValue<int>()==165?"meal plan":"sales tax")).ToList();
                return new MealPlanBatchSnapshot(165,entries,new[]{100,200,300});
            });
            Service=new(new ConfigurationBuilder().AddInMemoryCollection(settings??Settings()).Build(),new MemoryCache(new MemoryCacheOptions()),Assignments,Tax,Read,new HttpClient(Http));
            Http.Rows.Add(Row(Target,1,100,false));Http.Rows.Add(Row(Target,2,200,true));
        }
    }
    [Fact] public async Task Moves_base_and_tax_without_creating_or_deleting_charges_and_reuses_the_same_batch()
    {
        var f=new Fixture();await f.Service.EnsureAsync(Target);await f.Service.EnsureAsync(Target);
        Assert.Equal(2,f.Http.Puts.Count);Assert.All(f.Http.Rows,r=>Assert.Equal(300,r["batchMasterID"]!.GetValue<int>()));
        Assert.Equal(new[]{1,2},f.Http.Rows.Select(r=>r["billingBatchID"]!.GetValue<int>()));
        Assert.Equal(2475m,f.Http.Rows[0]["amount"]!.GetValue<decimal>());Assert.Equal(204.19m,f.Http.Rows[1]["amount"]!.GetValue<decimal>());
        Assert.All(f.Http.Puts,r=>{Assert.Equal("2026-11-12T12:00:00",r["transactionDate"]!.GetValue<string>());Assert.Equal(17,r["paymentPlanID"]!.GetValue<int>());});
    }
    [Fact] public async Task Different_students_and_terms_use_one_configured_destination()
    {
        var f=new Fixture();var other=new MealPlanTaxTarget(11,270,43,17,715m);
        f.Assignments.GetAssignmentsAsync("11",Arg.Any<CancellationToken>()).Returns(new[]{new StudentMealPlanAssignment(43,17,270,"Other",715m)});
        f.Http.Rows.Add(Row(other,3,100,false));f.Http.Rows.Add(Row(other,4,200,true));
        await f.Service.EnsureAsync(Target);await f.Service.EnsureAsync(other);
        Assert.Equal(4,f.Http.Puts.Count);Assert.All(f.Http.Puts,r=>Assert.Equal(300,r["batchMasterID"]!.GetValue<int>()));
        Assert.Equal(2,f.Http.Rows.Select(r=>r["studentUID"]!.GetValue<int>()).Distinct().Count());
    }
    [Fact] public async Task A_lost_successful_put_is_reconciled_by_id_instead_of_recreating_a_charge()
    {
        var f=new Fixture();f.Http.LoseNextPut=true;
        await Assert.ThrowsAsync<HttpRequestException>(()=>f.Service.EnsureAsync(Target));await f.Service.EnsureAsync(Target);
        Assert.Equal(2,f.Http.Puts.Count);Assert.Equal(2,f.Http.Rows.Count);Assert.All(f.Http.Rows,r=>Assert.Equal(300,r["batchMasterID"]!.GetValue<int>()));
    }
    [Theory][InlineData("transDate")][InlineData("currencyTypeAmount")][InlineData("paymentPlanHeaderID")][InlineData("billingAllClear")]
    public async Task Missing_fields_block_all_moves_including_the_other_charge(string field)
    {
        var f=new Fixture();f.Http.Rows[1].Remove(field);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service.EnsureAsync(Target));Assert.Empty(f.Http.Puts);
    }
    [Theory][InlineData("paymentID")][InlineData("creditID")][InlineData("transDistID")][InlineData("a1098FilerID")][InlineData("paidAmount")]
    public async Task Payments_and_distributions_are_never_rewritten(string field)
    {
        var f=new Fixture();f.Http.Rows[1][field]=1;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service.EnsureAsync(Target));Assert.Empty(f.Http.Puts);
    }
    [Fact] public async Task A_stale_replica_or_extra_live_charge_blocks_all_moves()
    {
        var f=new Fixture();f.Http.ExtraLiveRow=Row(Target,99,100,false);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service.EnsureAsync(Target));Assert.Empty(f.Http.Puts);
    }
    [Fact] public async Task Foreign_currency_charges_are_not_recalculated_by_a_batch_move()
    {
        var f=new Fixture();f.Http.Rows[0]["currencyTypeID"]=2;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service.EnsureAsync(Target));Assert.Empty(f.Http.Puts);
    }
    [Fact] public async Task Explicitly_unset_legacy_tax_metadata_is_preserved_while_missing_metadata_still_blocks_moves()
    {
        var f=new Fixture();foreach(var field in new[]{"paymentID","creditID","transDistID","a1098FilerID","paidAmount","currencyTypeAmount"})f.Http.Rows[1][field]=null;
        await f.Service.EnsureAsync(Target);Assert.Equal(2,f.Http.Puts.Count);Assert.Null(f.Http.Puts[1]["currencyTypeAmt"]);
        var missing=new Fixture();missing.Http.Rows[1].Remove("paymentID");
        await Assert.ThrowsAsync<InvalidOperationException>(()=>missing.Service.EnsureAsync(Target));Assert.Empty(missing.Http.Puts);
    }
    [Fact] public async Task Changed_or_multiple_assignments_do_not_move_any_charge()
    {
        var f=new Fixture();f.Assignments.GetAssignmentsAsync("10",Arg.Any<CancellationToken>()).Returns(new[]{new StudentMealPlanAssignment(42,9,269,"Plan",2475m),new StudentMealPlanAssignment(43,10,269,"Other",2250m)});
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service.EnsureAsync(Target));Assert.Empty(f.Http.Puts);Assert.Equal(0,f.Http.Calls);
    }
    [Theory][InlineData("MealPlanTax:EnableProductionWrites","true")][InlineData("MealPlanTax:EnableNonProductionWrites","false")][InlineData("StudentInformationSystems:Providers:ThesisElements:Api:TenantHost","el-nel.thesiscloud.com")]
    public async Task Cross_environment_settings_block_even_read_calls(string key,string value)
    {
        var settings=Settings();settings[key]=value;var f=new Fixture(settings);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service.EnsureAsync(Target));Assert.Equal(0,f.Http.Calls);
        await f.Tax.DidNotReceiveWithAnyArgs().DestinationAsync(default);
    }
    [Fact] public async Task An_unconfigured_release_preserves_the_previous_behavior()
    {var settings=Settings();settings.Remove("MealPlans:SharedBatchMasterId");var f=new Fixture(settings);await f.Service.EnsureAsync(Target);Assert.Equal(0,f.Http.Calls);}
    [Fact] public async Task New_assignments_use_the_shared_path_but_zero_price_plans_do_not_create_charges()
    {
        var f=new Fixture();await f.Service.AfterAssignmentAsync(10,9);Assert.Equal(2,f.Http.Puts.Count);
        f.Assignments.GetAssignmentsAsync("11",Arg.Any<CancellationToken>()).Returns(new[]{new StudentMealPlanAssignment(43,16,269,"No meals",0m)});
        await f.Service.AfterAssignmentAsync(11,16);Assert.Equal(2,f.Http.Puts.Count);
    }
    private static JsonObject Row(MealPlanTaxTarget t,int id,int batch,bool tax)=>new()
    {
        ["billingBatchID"]=id,["batchMasterID"]=batch,["studentUID"]=t.StudentUid,["termCalendarID"]=t.TermId,["transDOCID"]=tax?261:165,
        ["arTypeID"]=tax?8047:0,["amount"]=tax?t.TaxAmount:t.BaseAmount,["debits"]=tax?t.TaxAmount:t.BaseAmount,["credits"]=0m,
        ["refNo"]=tax?t.Reference:"",["description"]=tax?"Meal plan sales tax (8.25%)":"Meal Plan",["transType"]="DEBIT",
        ["billingAllClear"]="",["currencyTypeID"]=null,["extTypeID"]=0,["transDate"]="2026-11-12T12:00:00",["currencyTypeAmount"]=0m,
        ["paymentPlanHeaderID"]=17,["paymentID"]=0,["creditID"]=0,["transDistID"]=0,["creditCardTypeID"]=0,["typeID"]=0,["paidAmount"]=0m,
        ["a1098FilerID"]=0,["creditCardNum"]="",["a1098Deductible"]="No"
    };
    private sealed class Handler:HttpMessageHandler
    {
        public readonly List<JsonObject> Rows=[];
        public readonly List<JsonObject> Puts=[];
        public JsonObject? ExtraLiveRow;
        public bool LoseNextPut;public int Calls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Calls++;JsonNode data;
            if(request.RequestUri!.AbsolutePath.Contains("Authenticate"))data=new JsonObject{["token"]="test"};
            else if(request.Method==HttpMethod.Get)
            {
                var parts=request.RequestUri.AbsolutePath.Split('/');var student=int.Parse(parts[^2]);var batch=int.Parse(parts[^1]);
                var list=new JsonArray();foreach(var row in Rows.Concat(ExtraLiveRow is null?[]:new[]{ExtraLiveRow}).Where(r=>r["studentUID"]!.GetValue<int>()==student && r["batchMasterID"]!.GetValue<int>()==batch))list.Add(row.DeepClone());
                data=new JsonObject{["batchEntriesList"]=list};
            }
            else
            {
                Assert.Equal(HttpMethod.Put,request.Method);Assert.Equal("np-usc-gateway.thesiscloud.com",request.RequestUri.Host);
                var body=JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!.AsObject();Puts.Add(body);
                var row=Rows.Single(r=>r["billingBatchID"]!.GetValue<int>()==body["billingBatchID"]!.GetValue<int>());row["batchMasterID"]=body["batchMasterID"]!.DeepClone();
                if(LoseNextPut){LoseNextPut=false;throw new HttpRequestException("Lost successful move reply");}data=JsonValue.Create(true)!;
            }
            return new(HttpStatusCode.OK){Content=new StringContent(new JsonObject{["isSuccess"]=true,["data"]=data}.ToJsonString(),Encoding.UTF8,"application/json")};
        }
    }
}
