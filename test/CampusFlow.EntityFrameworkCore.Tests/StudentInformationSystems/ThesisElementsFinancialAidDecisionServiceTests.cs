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
    [InlineData(StudentFinancialAidDecision.Accept)]
    [InlineData(StudentFinancialAidDecision.Decline)]
    public void Decision_uses_write_date_name_and_preserves_every_other_field(StudentFinancialAidDecision decision)
    {
        var award=Award();var original=(JsonObject)award.DeepClone();
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
    private sealed class AwardHandler(bool corrupt):HttpMessageHandler
    {
        public int Writes;public int Reads;private readonly JsonObject _award=Award();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            if(request.Method==HttpMethod.Put)
            {
                Writes++;var body=JsonNode.Parse(await request.Content!.ReadAsStringAsync(token))!.AsObject();
                Assert.False(body.ContainsKey("distributionDate"));Assert.Equal(_award["distributionDate"]!.GetValue<string>(),body["transDate"]!.GetValue<string>());
                Assert.True(body["codaei"]!.GetValue<bool>());
                _award["studentAccepted"]=true;if(corrupt)_award["codaei"]=false;
                return Response(new JsonObject{["isSuccess"]=true});
            }
            Reads++;return Response(new JsonObject{["isSuccess"]=true,["data"]=new JsonObject{["getFinancialAwardDetails"]=new JsonArray(_award.DeepClone())}});
        }
        private static HttpResponseMessage Response(JsonNode node)=>new(HttpStatusCode.OK){Content=new StringContent(node.ToJsonString(),Encoding.UTF8,"application/json")};
    }
}
