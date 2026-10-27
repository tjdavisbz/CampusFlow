using System;
using System.Linq;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.BillApprovals;
using CampusFlow.Permissions;
using CampusFlow.StudentInformationSystems;
using Microsoft.AspNetCore.Authorization;
using NSubstitute;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Auditing;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Xunit;
namespace CampusFlow.Bibliu;

public class BibliuExportServiceTests
{
    [Fact]
    public async Task Host_context_cannot_read_tenant_exports()
    {
        var configurations=Substitute.For<IRepository<BibliuConfiguration,Guid>>();
        var approvals=Substitute.For<IRepository<BillApproval,Guid>>();
        var reader=Substitute.For<IBibliuExportReader>();
        var tenant=Substitute.For<ICurrentTenant>();
        tenant.Id.Returns((Guid?)null);
        var service=new BibliuExportService(configurations,approvals,Array.Empty<IStudentInformationSystemTermLookup>(),reader,tenant);
        await Should.ThrowAsync<UserFriendlyException>(()=>service.ExportAsync("all",CancellationToken.None));
        reader.ReceivedCalls().ShouldBeEmpty();
        configurations.ReceivedCalls().ShouldBeEmpty();
    }
    [Fact]
    public async Task Disabled_configuration_cannot_trigger_source_queries()
    {
        var tenantId=Guid.NewGuid();
        var configurations=Substitute.For<IRepository<BibliuConfiguration,Guid>>();
        configurations.FindAsync(Arg.Any<Expression<Func<BibliuConfiguration,bool>>>(),Arg.Any<bool>(),Arg.Any<CancellationToken>())
            .Returns(new BibliuConfiguration(Guid.NewGuid(),tenantId));
        var reader=Substitute.For<IBibliuExportReader>();
        var tenant=Substitute.For<ICurrentTenant>();tenant.Id.Returns(tenantId);
        var service=new BibliuExportService(configurations,Substitute.For<IRepository<BillApproval,Guid>>(),Array.Empty<IStudentInformationSystemTermLookup>(),reader,tenant);
        await Should.ThrowAsync<UserFriendlyException>(()=>service.ExportAsync("users",CancellationToken.None));
        reader.ReceivedCalls().ShouldBeEmpty();
    }
    [Fact]
    public async Task Export_uses_one_configuration_and_only_approved_bills_for_its_tenant_and_term()
    {
        var tenantId=Guid.NewGuid();
        var config=new BibliuConfiguration(Guid.NewGuid(),tenantId);
        config.Update("268","269","123","241",true);
        var configurations=Substitute.For<IRepository<BibliuConfiguration,Guid>>();
        configurations.FindAsync(Arg.Any<Expression<Func<BibliuConfiguration,bool>>>(),Arg.Any<bool>(),Arg.Any<CancellationToken>())
            .Returns(call=>call.Arg<Expression<Func<BibliuConfiguration,bool>>>().Compile()(config)?config:null);
        BillApproval Bill(Guid owner,string term,bool accept)
        {
            var bill=new BillApproval(Guid.NewGuid(),owner,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid().ToString(),"123",term,"code","term");
            if(accept) bill.Accept(Guid.NewGuid(),1,"agreement",DateTime.UtcNow,null,null);
            return bill;
        }
        var eligible=Bill(tenantId,"268",true);
        var bills=new[]{eligible,Bill(tenantId,"268",false),Bill(Guid.NewGuid(),"268",true),Bill(tenantId,"269",true)};
        var approvals=Substitute.For<IRepository<BillApproval,Guid>>();
        approvals.GetListAsync(Arg.Any<Expression<Func<BillApproval,bool>>>(),Arg.Any<bool>(),Arg.Any<CancellationToken>())
            .Returns(call=>bills.Where(call.Arg<Expression<Func<BillApproval,bool>>>().Compile()).ToList());
        var lookup=Substitute.For<IStudentInformationSystemTermLookup>();
        lookup.Provider.Returns(StudentInformationSystemProvider.ThesisElements);
        lookup.GetTermsAsync(Arg.Any<CancellationToken>()).Returns(new List<StudentInformationSystemTerm> {
            new(StudentInformationSystemProvider.ThesisElements,"268","B26Q","Fall 2026",DateTime.MinValue,DateTime.MaxValue),
            new(StudentInformationSystemProvider.ThesisElements,"269","B27C","Spring 2027",DateTime.MinValue,DateTime.MaxValue),
            new(StudentInformationSystemProvider.ThesisElements,"270","B27L","Summer 2027",DateTime.MinValue,DateTime.MaxValue)
        });
        var reader=Substitute.For<IBibliuExportReader>();
        reader.ReadAsync(Arg.Any<string>(),Arg.Any<BibliuExportRequest>(),Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Dictionary<string,string?>>());
        var tenant=Substitute.For<ICurrentTenant>();tenant.Id.Returns(tenantId);
        var service=new BibliuExportService(configurations,approvals,new[]{lookup},reader,tenant);
        var result=await service.ExportAsync("all",CancellationToken.None);
        result.ConfigurationId.ShouldBe(config.Id);
        result.ConfigurationVersion.ShouldBe(config.ConcurrencyStamp);
        result.ExpandedTerms.ShouldBe(new[]{"Fall 2026","Spring 2027","Summer 2027"});
        result.Exports.Count.ShouldBe(4);
        await reader.Received(4).ReadAsync(Arg.Any<string>(),Arg.Is<BibliuExportRequest>(r=>
            r.BillingTermId==268 && r.BibliuTermId==269 && r.ApprovedStudentIds.Count==1 && r.ApprovedStudentIds[0]==eligible.ExternalStudentId),Arg.Any<CancellationToken>());
    }
    [Fact]
    public void Export_requires_its_own_permission_and_disables_payload_auditing()
    {
        var type=typeof(BibliuExportController);
        type.GetCustomAttributes(typeof(AuthorizeAttribute),true).Cast<AuthorizeAttribute>().Single().Policy
            .ShouldBe(CampusFlowPermissions.Admin.BibliuExport);
        type.IsDefined(typeof(DisableAuditingAttribute),true).ShouldBeTrue();
        typeof(CampusFlow.Web.Pages.Admin.BibliuModel).GetCustomAttributes(typeof(AuthorizeAttribute),true)
            .Cast<AuthorizeAttribute>().Single().Policy.ShouldBe(CampusFlowPermissions.Admin.BibliuConfiguration);
    }
}
