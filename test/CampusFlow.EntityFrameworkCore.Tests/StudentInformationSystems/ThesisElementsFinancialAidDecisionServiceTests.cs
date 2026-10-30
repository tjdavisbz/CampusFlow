using System;
using System.Text.Json.Nodes;
using CampusFlow.StudentInformationSystems;
using Xunit;

namespace CampusFlow.EntityFrameworkCore.Tests.StudentInformationSystems;

public sealed class ThesisElementsFinancialAidDecisionServiceTests
{
    [Fact]
    public void Decision_update_preserves_the_elements_distribution_date()
    {
        var award = JsonNode.Parse("""
            {"financialAwardId":123,"studentUid":456,"amount":1250.00,
             "distributionDate":"2026-10-20T00:00:00","studentAccepted":null}
            """)!.AsObject();

        var body = ThesisElementsFinancialAidDecisionService.BuildDecisionBody(
            award, StudentFinancialAidDecision.Accept);

        Assert.Equal("2026-10-20T00:00:00", body["distributionDate"]!.GetValue<string>());
        Assert.Equal(1, body["studentAccepted"]!.GetValue<int>());
        Assert.Null(award["studentAccepted"]);
    }

    [Fact]
    public void Decision_update_refuses_a_missing_distribution_date_field()
    {
        var award = JsonNode.Parse("""{"amount":1250.00,"studentAccepted":null}""")!.AsObject();

        Assert.Throws<InvalidOperationException>(() =>
            ThesisElementsFinancialAidDecisionService.BuildDecisionBody(
                award, StudentFinancialAidDecision.Accept));
    }

    [Fact]
    public void Decision_update_refuses_a_zero_dollar_award()
    {
        var award = JsonNode.Parse("""{"amount":0,"distributionDate":"2026-10-20T00:00:00"}""")!
            .AsObject();

        Assert.Throws<InvalidOperationException>(() =>
            ThesisElementsFinancialAidDecisionService.BuildDecisionBody(
                award, StudentFinancialAidDecision.Accept));
    }
}
