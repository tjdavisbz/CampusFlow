using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using CampusFlow.CostTypes;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;
namespace CampusFlow.EntityFrameworkCore.Tests.CostTypes;
public class ElementsCostTypeTests
{
    [Fact]
    public void Baseline_keeps_order_missing_degree_fallback_and_approved_duplicate_label_id()
    {
        using var stream=typeof(CostTypeAppService).Assembly.GetManifestResourceStream("CampusFlow.CostTypes.CamsCostTypeBaseline.json");
        stream.ShouldNotBeNull();
        var rules=JsonSerializer.Deserialize<List<CostTypeRuleDto>>(stream!)!;
        rules.Count.ShouldBe(239);
        rules.Select(x=>x.Priority).ShouldBe(Enumerable.Range(1,239));
        rules[0].DegreeMatch.ShouldBe("NULL");
        rules.Last().Campus.ShouldBeNull();rules.Last().Attendance.ShouldBe("Dual Credit");
        rules.Single(x=>x.Campus=="Lafayette_IN"&&x.Attendance=="School of Ministry").CostTypeId.ShouldBe(93);
        rules.Single(x=>x.Campus=="Main"&&x.Attendance=="Distance Education").CostType.ShouldBe("DE");
        rules.ShouldNotContain(x=>x.Campus=="Oaks_Main"&&x.Attendance=="Oaks Church");
    }
    [Fact]
    public void Explicit_nonproduction_configuration_allows_manual_run() => Service().EnsureRunEnabled();
    [Theory]
    [InlineData("CostTypes:EnableNonProductionRuns","false")]
    [InlineData("App:EnvironmentLabel","PRODUCTION")]
    [InlineData("StudentInformationSystems:Providers:ThesisElements:Api:BaseUrl","https://app-elm-prod-usc-integrationapi.azurewebsites.net")]
    [InlineData("StudentInformationSystems:Providers:ThesisElements:Api:RegistrationBaseUrl","https://usc-gateway.thesiscloud.com")]
    [InlineData("StudentInformationSystems:Providers:ThesisElements:Api:TenantHost","el-nel.thesiscloud.com")]
    [InlineData("ConnectionStrings:ThesisElementsReadOnly","Server=sql-elm-prod-usc-replica.database.windows.net;Database=EL-NEL-DB")]
    [InlineData("StudentInformationSystems:Providers:ThesisElements:Api:BaseUrl","https://user@app-elm-nonprod-usc-integrationapi.azurewebsites.net")]
    public void Guard_rejects_disabled_or_wrong_destinations(string key,string value) =>
        Should.Throw<InvalidOperationException>(()=>Service(key,value).EnsureRunEnabled());
    [Fact]
    public void Explicit_production_configuration_allows_only_its_own_manual_run() => ProductionService().EnsureRunEnabled();
    [Theory]
    [InlineData("CostTypes:EnableProductionRuns","false")]
    [InlineData("CostTypes:EnableNonProductionRuns","true")]
    [InlineData("App:EnvironmentLabel","NON-PRODUCTION")]
    [InlineData("ASPNETCORE_ENVIRONMENT","Staging")]
    [InlineData("ASPNETCORE_ENVIRONMENT","Development")]
    [InlineData("StudentInformationSystems:Providers:ThesisElements:Api:BaseUrl","https://app-elm-nonprod-usc-integrationapi.azurewebsites.net")]
    [InlineData("StudentInformationSystems:Providers:ThesisElements:Api:BaseUrl","http://app-elm-prod-usc-integrationapi.azurewebsites.net")]
    [InlineData("StudentInformationSystems:Providers:ThesisElements:Api:RegistrationBaseUrl","https://np-usc-gateway.thesiscloud.com")]
    [InlineData("StudentInformationSystems:Providers:ThesisElements:Api:TenantHost","el-np-nel.thesiscloud.com")]
    [InlineData("ConnectionStrings:ThesisElementsReadOnly","Server=sql-elm-nonprod-usc-replica.database.windows.net;Database=EL-NP-NEL-DB")]
    public void Production_gate_rejects_disabled_mixed_or_cross_environment_settings(string key,string value) =>
        Should.Throw<InvalidOperationException>(()=>ProductionService(key,value).EnsureRunEnabled());
    private static ElementsCostTypeService ProductionService(string? key=null,string? value=null)
    {
        const string prefix="StudentInformationSystems:Providers:ThesisElements:Api:";
        var settings=new Dictionary<string,string?>
        {
            ["CostTypes:EnableProductionRuns"]="true",["CostTypes:EnableNonProductionRuns"]="false",
            ["ASPNETCORE_ENVIRONMENT"]="Production",[prefix+"BaseUrl"]="https://app-elm-prod-usc-integrationapi.azurewebsites.net",
            [prefix+"RegistrationBaseUrl"]="https://el-usc-gateway.thesiscloud.com",[prefix+"TenantHost"]="el-nel.thesiscloud.com",
            ["ConnectionStrings:ThesisElementsReadOnly"]="Server=sql-elm-prod-usc-replica.database.windows.net;Database=EL-NEL-DB"
        };
        if(key is not null)settings[key]=value;
        return new(new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),new MemoryCache(new MemoryCacheOptions()));
    }
    [Fact]
    public void Change_preserves_unrelated_fields_and_uses_the_selected_terms_target()
    {
        var status=Status();var candidate=Candidate(status);
        var changed=ElementsCostTypeService.PrepareChange(candidate,status);
        changed["costTypeID"]!.GetValue<int>().ShouldBe(4);
        changed["campusID"]!.GetValue<int>().ShouldBe(21);
        changed["transcriptComment"]!.GetValue<string>().ShouldBe("Keep me");
        status["costTypeID"]!.GetValue<int>().ShouldBe(26);
        var second=Status();second["termCalendarID"]=270;second["studentStatusID"]=901;
        var next=new CostTypeCandidate(candidate.Row with {TermId=270,StatusId=901,ProposedId=75},ElementsCostTypeService.Fingerprint(second));
        ElementsCostTypeService.PrepareChange(next,second)["costTypeID"]!.GetValue<int>().ShouldBe(75);
        Should.Throw<InvalidOperationException>(()=>ElementsCostTypeService.PrepareChange(candidate,second));
    }
    [Fact]
    public void Concurrent_unrelated_edit_invalidates_preview()
    {
        var status=Status();var candidate=Candidate(status);status["transcriptComment"]="New comment";
        Should.Throw<InvalidOperationException>(()=>ElementsCostTypeService.PrepareChange(candidate,status));
    }
    [Fact]
    public void Explicit_default_zero_is_allowed_but_unmapped_is_not()
    {
        var status=Status();var candidate=Candidate(status);
        ElementsCostTypeService.PrepareChange(candidate with {Row=candidate.Row with {ProposedId=0,ProposedLabel="Default"}},status)["costTypeID"]!.GetValue<int>().ShouldBe(0);
        Should.Throw<InvalidOperationException>(()=>ElementsCostTypeService.PrepareChange(candidate with {Row=candidate.Row with {ProposedId=null}},status));
    }
    private static JsonObject Status()=>new(){["studentStatusID"]=900,["termCalendarID"]=269,["costTypeID"]=26,["campusID"]=21,["transcriptComment"]="Keep me"};
    private static CostTypeCandidate Candidate(JsonObject status)=>new(new(8313,"test","Test",900,269,"Spring 2027","MAIN","Distance Education",null,26,"AC",147,4,"DE","Would change"),ElementsCostTypeService.Fingerprint(status));
    private static ElementsCostTypeService Service(string? key=null,string? value=null)
    {
        var config=new Dictionary<string,string?>{
            ["CostTypes:EnableNonProductionRuns"]="true",["App:EnvironmentLabel"]="NON-PRODUCTION",
            ["StudentInformationSystems:Providers:ThesisElements:Api:BaseUrl"]="https://app-elm-nonprod-usc-integrationapi.azurewebsites.net",
            ["StudentInformationSystems:Providers:ThesisElements:Api:RegistrationBaseUrl"]="https://np-usc-gateway.thesiscloud.com",
            ["StudentInformationSystems:Providers:ThesisElements:Api:TenantHost"]="el-np-nel.thesiscloud.com",
            ["ConnectionStrings:ThesisElementsReadOnly"]="Server=sql-elm-nonprod-usc-replica.database.windows.net;Database=EL-NP-NEL-DB"};
        if(key is not null)config[key]=value;
        return new(new ConfigurationBuilder().AddInMemoryCollection(config).Build(),new MemoryCache(new MemoryCacheOptions()));
    }
}
