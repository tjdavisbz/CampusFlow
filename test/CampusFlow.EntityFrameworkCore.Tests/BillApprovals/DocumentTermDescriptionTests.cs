using System;
using System.Collections.Generic;
using CampusFlow.StudentInformationSystems;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CampusFlow.EntityFrameworkCore.Tests.BillApprovals;

public class DocumentTermDescriptionTests
{
    [Theory]
    [InlineData("Spring 2027")]
    [InlineData("Fall 2027")]
    public void Term_description_preserves_the_reporting_document_type_and_delivery_metadata(string term)
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BillApproval:DocumentTracking:DocumentNameId"] = "123",
            ["BillApproval:DocumentTracking:LocationId"] = "7",
            ["BillApproval:DocumentTracking:DocumentStatusId"] = "8",
            ["BillApproval:DocumentTracking:UserDefinedFieldId"] = "9",
            ["BillApproval:DocumentTracking:Internal"] = "false"
        }).Build();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new ThesisElementsDocumentTrackingService(cfg, cache);
        var approvalId = Guid.NewGuid();
        var request = new StudentDocumentTrackingRequest("10", term, approvalId, DateTime.UtcNow);

        var body = service.BuildDocumentBody(request, " Approved  Bill ");

        Assert.Equal(123, body["docNameId"]!.GetValue<int>());
        Assert.Equal("Approved Bill - " + term, body["docDescription"]!.GetValue<string>());
        Assert.Equal(approvalId.ToString("N"), body["reference"]!.GetValue<string>());
        Assert.Equal(10, body["studentUid"]!.GetValue<int>());
        Assert.Equal(7, body["locationId"]!.GetValue<int>());
        Assert.Equal(8, body["docStatusId"]!.GetValue<int>());
        Assert.Equal(9, body["userDefinedFieldId"]!.GetValue<int>());
        Assert.False(body["internal"]!.GetValue<bool>());
        Assert.Equal("123", cfg["BillApproval:DocumentTracking:DocumentNameId"]);
    }

    [Fact]
    public void Missing_term_cannot_create_an_ambiguous_document()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new ThesisElementsDocumentTrackingService(new ConfigurationBuilder().Build(), cache);
        Assert.Throws<ArgumentException>(() => service.BuildDocumentBody(
            new("10", " ", Guid.NewGuid(), DateTime.UtcNow), "Approved Bill"));
    }
}
