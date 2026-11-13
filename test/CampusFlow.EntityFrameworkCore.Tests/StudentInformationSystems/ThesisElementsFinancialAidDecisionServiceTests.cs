using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.StudentInformationSystems;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace CampusFlow.EntityFrameworkCore.Tests.StudentInformationSystems;

public sealed class ThesisElementsFinancialAidDecisionServiceTests
{
    internal static JsonObject Award() => JsonNode.Parse("""
        {"financialAwardId":123,"financialStatusId":12,"studentUid":456,"termCalendarId":269,
         "distributionDate":"2026-10-20T00:00:00","transTypeId":9,"transStatusId":2,
         "amount":1250.0,"estimatedAmount":1000.0,"description":"UNSUB ADD","referenceNo":"keep",
         "creditStatusId":2,"code1Id":3,"code2Id":4,"showOnBillingStatement":"Yes",
         "dateCheckSigned":null,"requiredCredits":6,"studentAccepted":null,"transId":0,"lock":false,
         "campusId":1,"paymentPeriodStartDate":null,"paymentPeriodEndDate":null,"codaei":true,
         "originationFeeAmount":12.0,"interestRebateAmount":0.0,"calendarYear":2027,
         "programAttendanceBeginDate":null,"workForcePellTuitionAndFees":null,"institutionalLimitApplied":true}
        """)!.AsObject();

    [Theory]
    [InlineData(StudentFinancialAidDecision.Accept,false)]
    [InlineData(StudentFinancialAidDecision.Decline,false)]
    [InlineData(StudentFinancialAidDecision.Accept,true)]
    [InlineData(StudentFinancialAidDecision.Decline,true)]
    public void Decision_uses_write_date_name_and_preserves_every_other_field(StudentFinancialAidDecision decision,bool locked)
    {
        var award=Award();award["lock"]=locked;var original=(JsonObject)award.DeepClone();
        var body=ThesisElementsFinancialAidDecisionService.BuildDecisionBody(award,decision);
        Assert.False(body.ContainsKey("distributionDate"));
        Assert.Equal("2026-10-20T00:00:00",body["transDate"]!.GetValue<string>());
        Assert.True(body["codaei"]!.GetValue<bool>());
        Assert.Equal((int)decision,body["studentAccepted"]!.GetValue<int>());
        foreach(var field in original)
            if(field.Key is not ("financialAwardId" or "distributionDate" or "studentAccepted"))
                Assert.True(JsonNode.DeepEquals(field.Value,body[field.Key]),field.Key);
        Assert.True(JsonNode.DeepEquals(original,award));
    }
    [Theory]
    [InlineData("distributionDate")]
    [InlineData("codaei")]
    [InlineData("code1Id")]
    public void Missing_fields_prevent_destructive_default_values(string name)
    {
        var award=Award();award.Remove(name);
        Assert.Throws<InvalidOperationException>(()=>ThesisElementsFinancialAidDecisionService.BuildDecisionBody(award,StudentFinancialAidDecision.Accept));
    }
    [Fact] public void A_null_date_and_false_COD_AEI_are_preserved()
    {
        var award=Award();award["distributionDate"]=null;award["codaei"]=false;
        var body=ThesisElementsFinancialAidDecisionService.BuildDecisionBody(award,StudentFinancialAidDecision.Decline);
        Assert.True(body.ContainsKey("transDate"));Assert.Null(body["transDate"]);Assert.False(body["codaei"]!.GetValue<bool>());
    }
    [Fact] public void A_zero_dollar_award_cannot_be_submitted()
    {var award=Award();award["amount"]=0;Assert.Throws<InvalidOperationException>(()=>ThesisElementsFinancialAidDecisionService.BuildDecisionBody(award,StudentFinancialAidDecision.Accept));}

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_HTTP_update_maps_and_verifies_the_preserved_values(bool corruptReadback)
    {
        var handler=new AwardHandler(corruptReadback);
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
        { ["StudentInformationSystems:Providers:ThesisElements:Api:BaseUrl"]="https://example.invalid",
          ["StudentInformationSystems:Providers:ThesisElements:Api:TenantHost"]="test.invalid",
          ["StudentInformationSystems:Providers:ThesisElements:Api:Module"]="FinancialAid"}).Build();
        var cache=new MemoryCache(new MemoryCacheOptions());cache.Set("ThesisElements:FinancialAid:ApiToken","test");
        var service=new ThesisElementsFinancialAidDecisionService(config,cache,new HttpClient(handler));
        if(corruptReadback)await Assert.ThrowsAsync<InvalidOperationException>(()=>service.SubmitDecisionAsync("456","123",StudentFinancialAidDecision.Accept));
        else await service.SubmitDecisionAsync("456","123",StudentFinancialAidDecision.Accept);
        Assert.Equal(1,handler.Writes);Assert.Equal(2,handler.Reads);
    }
    [Theory] [InlineData("sentToBilling")] [InlineData("awardStatus")]
    public void Live_noneditable_awards_are_rejected_even_if_the_replica_was_stale(string field)
    {var award=Award();award["lock"]=true;if(field=="sentToBilling")award[field]="Yes";else award[field]="Cancelled";Assert.Throws<InvalidOperationException>(()=>ThesisElementsFinancialAidDecisionService.BuildDecisionBody(award,StudentFinancialAidDecision.Accept));}
    [Theory]
    [InlineData(StudentFinancialAidDecision.Accept,false)]
    [InlineData(StudentFinancialAidDecision.Decline,false)]
    [InlineData(StudentFinancialAidDecision.Accept,true)]
    public async Task Locked_awards_support_both_decisions_and_an_unexpected_unlock_fails_verification(StudentFinancialAidDecision decision,bool corruptLock)
    {
        var handler=new AwardHandler(false,true,decision,corruptLock);
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
        { ["StudentInformationSystems:Providers:ThesisElements:Api:BaseUrl"]="https://example.invalid",
          ["StudentInformationSystems:Providers:ThesisElements:Api:TenantHost"]="test.invalid",
          ["StudentInformationSystems:Providers:ThesisElements:Api:Module"]="FinancialAid"}).Build();
        using var cache=new MemoryCache(new MemoryCacheOptions());cache.Set("ThesisElements:FinancialAid:ApiToken","test");
        var service=new ThesisElementsFinancialAidDecisionService(config,cache,new HttpClient(handler));
        if(corruptLock)await Assert.ThrowsAsync<InvalidOperationException>(()=>service.SubmitDecisionAsync("456","123",decision,default,"269"));
        else await service.SubmitDecisionAsync("456","123",decision,default,"269");
        Assert.Equal(1,handler.Writes);Assert.Equal(2,handler.Reads);
    }
    [Fact] public async Task An_award_moved_to_another_term_is_not_updated()
    {
        var handler=new AwardHandler(false);var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["StudentInformationSystems:Providers:ThesisElements:Api:BaseUrl"]="https://example.invalid",["StudentInformationSystems:Providers:ThesisElements:Api:TenantHost"]="test.invalid",["StudentInformationSystems:Providers:ThesisElements:Api:Module"]="FinancialAid"}).Build();var cache=new MemoryCache(new MemoryCacheOptions());cache.Set("ThesisElements:FinancialAid:ApiToken","test");var service=new ThesisElementsFinancialAidDecisionService(config,cache,new HttpClient(handler));await Assert.ThrowsAsync<InvalidOperationException>(()=>service.SubmitDecisionAsync("456","123",StudentFinancialAidDecision.Accept,default,"268"));Assert.Equal(0,handler.Writes);
    }
    private sealed class AwardHandler(bool corrupt,bool locked=false,StudentFinancialAidDecision decision=StudentFinancialAidDecision.Accept,bool corruptLock=false):HttpMessageHandler
    {
        public int Writes;public int Reads;private readonly JsonObject _award=MakeAward(locked);
        private static JsonObject MakeAward(bool locked){var award=Award();award["lock"]=locked;return award;}
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            if(request.Method==HttpMethod.Put)
            {
                Writes++;var body=JsonNode.Parse(await request.Content!.ReadAsStringAsync(token))!.AsObject();
                Assert.False(body.ContainsKey("distributionDate"));Assert.Equal(_award["distributionDate"]!.GetValue<string>(),body["transDate"]!.GetValue<string>());
                Assert.True(body["codaei"]!.GetValue<bool>());
                Assert.Equal(locked,body["lock"]!.GetValue<bool>());
                Assert.Equal((int)decision,body["studentAccepted"]!.GetValue<int>());
                _award["studentAccepted"]=decision==StudentFinancialAidDecision.Accept;if(corrupt)_award["codaei"]=false;if(corruptLock)_award["lock"]=false;
                return Response(new JsonObject{["isSuccess"]=true});
            }
            Reads++;return Response(new JsonObject{["isSuccess"]=true,["data"]=new JsonObject{["getFinancialAwardDetails"]=new JsonArray(_award.DeepClone())}});
        }
        private static HttpResponseMessage Response(JsonNode node)=>new(HttpStatusCode.OK){Content=new StringContent(node.ToJsonString(),Encoding.UTF8,"application/json")};
    }
}
