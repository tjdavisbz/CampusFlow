using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.StudentInformationSystems;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;

namespace CampusFlow.Payments;

public class ElementsPaymentPostingTests
{
    [Theory]
    [InlineData("2026-10-27T00:53:45Z", "2026-10-26")]
    [InlineData("2026-10-27T06:26:28Z", "2026-10-27")]
    [InlineData("2026-12-01T05:59:59Z", "2026-11-30")]
    [InlineData("2026-12-01T06:00:00Z", "2026-12-01")]
    public async Task Uses_central_payment_date_and_creates_daily_batch(string instant, string expected)
    {
        var handler = new ElementsHandler();
        var service = Service(handler);
        await service.PostAsync("123", 1, 100, 2.85m, "REF", false,
            DateTime.Parse(instant, null, System.Globalization.DateTimeStyles.AdjustToUniversal));
        handler.CreatedComment.ShouldBe("Online Credit Card Payments - " + expected);
        handler.Entries.Count.ShouldBe(2);
        handler.Entries.ShouldAllBe(e => e["transactionDate"]!.ToString() == expected + "T00:00:00");
        handler.Entries.Select(e => e["refNo"]!.ToString()).ShouldBe(new[] { "REF", "REF-FEE" });
    }

    [Fact]
    public async Task Does_not_reuse_yesterdays_legacy_batch_for_new_payment()
    {
        var handler = new ElementsHandler();
        handler.Batches.Add(Batch(10, "2026-10-26K", "Online Credit Card Payments"));
        await Service(handler).PostAsync("123", 1, 100, 0, "NEW", false, new DateTime(2026, 10, 27, 7, 0, 0, DateTimeKind.Utc));
        handler.CreatedComment.ShouldBe("Online Credit Card Payments - 2026-10-27");
        handler.Entries.Single()["batchMasterID"]!.GetValue<int>().ShouldBe(20);
    }

    [Fact]
    public async Task Finds_existing_payment_in_old_batch_without_inserting_duplicates()
    {
        var handler = new ElementsHandler { ExistingPayment = true, ExistingFee = true };
        handler.Batches.Add(Batch(10, "2026-10-26K", "Online Credit Card Payments"));
        var result = await Service(handler).PostAsync("123", 1, 100, 2.85m, "REF", false,
            new DateTime(2026, 10, 27, 7, 0, 0, DateTimeKind.Utc));
        handler.CreatedComment.ShouldBeNull();
        handler.Entries.ShouldBeEmpty();
        result.BatchMasterId.ShouldBe(10);
    }

    [Fact]
    public async Task Partial_retry_only_creates_missing_fee_in_original_batch()
    {
        var handler = new ElementsHandler { ExistingPayment = true };
        handler.Batches.Add(Batch(10, "2026-10-26K", "Online Credit Card Payments"));
        await Service(handler).PostAsync("123", 1, 100, 2.85m, "REF", false,
            new DateTime(2026, 10, 27, 7, 0, 0, DateTimeKind.Utc));
        handler.CreatedComment.ShouldBeNull();
        handler.Entries.Single()["refNo"]!.ToString().ShouldBe("REF-FEE");
        handler.Entries.Single()["batchMasterID"]!.GetValue<int>().ShouldBe(10);
    }

    [Fact]
    public async Task Reuses_same_day_legacy_batch_and_keeps_test_payments_separate()
    {
        var handler = new ElementsHandler();
        handler.Batches.Add(Batch(10, "2026-10-27A", "Online Credit Card Payments"));
        var service = Service(handler);
        await service.PostAsync("123", 1, 100, 0, "REF", false, new DateTime(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc));
        handler.CreatedComment.ShouldBeNull();
        handler.Entries.Single()["batchMasterID"]!.GetValue<int>().ShouldBe(10);
        await service.PostAsync("123", 1, 100, 0, "TEST", true, new DateTime(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc));
        handler.CreatedComment.ShouldBe("Test Online Credit Card Payments - 2026-10-27");
    }

    private static JsonObject Batch(int id, string name, string comment) => new()
        { ["batchMasterID"] = id, ["batchName"] = name, ["comment"] = comment };

    private static ThesisElementsPaymentPostingService Service(ElementsHandler handler)
    {
        var settings = new Dictionary<string, string?>();
        var prefix = "Payments:Payflow:ElementsPosting:";
        foreach (var key in new[] { "CampusId", "SourceModuleId", "TransactionDocumentId", "ExtendedDocumentId", "ArTypeId", "ConvenienceFeeTransactionDocumentId", "ConvenienceFeeArTypeId" }) settings[prefix + key] = "1";
        settings[prefix + "LiveBatchComment"] = "Online Credit Card Payments";
        settings[prefix + "TestBatchComment"] = "Test Online Credit Card Payments";
        foreach (var key in new[] { "TransactionType", "Description", "ConvenienceFeeTransactionType", "ConvenienceFeeDescription" }) settings[prefix + key] = key;
        prefix = "StudentInformationSystems:Providers:ThesisElements:Api:";
        settings[prefix + "RegistrationBaseUrl"] = "https://example.test";
        settings[prefix + "TenantHost"] = "example.test";
        var cache = new MemoryCache(new MemoryCacheOptions());
        cache.Set("ThesisElements:PaymentPosting:IntegrationToken", "test-token");
        return new ThesisElementsPaymentPostingService(new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), cache, new HttpClient(handler));
    }

    private sealed class ElementsHandler : HttpMessageHandler
    {
        public List<JsonObject> Batches { get; } = new();
        public List<JsonObject> Entries { get; } = new();
        public string? CreatedComment { get; private set; }
        public bool ExistingPayment { get; init; }
        public bool ExistingFee { get; init; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            JsonNode data;
            if (path.EndsWith("select-items")) data = new JsonObject { ["batchMasterList"] = new JsonArray(Batches.Select(x => x.DeepClone()).ToArray()) };
            else if (path.Contains("batch-entries-list"))
            {
                var entries = new JsonArray();
                if (ExistingPayment) entries.Add(new JsonObject { ["refNo"] = "REF", ["billingBatchID"] = 100 });
                if (ExistingFee) entries.Add(new JsonObject { ["refNo"] = "REF-FEE", ["billingBatchID"] = 101 });
                data = new JsonObject { ["batchEntriesList"] = entries };
            }
            else if (path.EndsWith("batch-master"))
            {
                var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!;
                CreatedComment = body["comment"]!.ToString();
                Batches.Add(Batch(20, "server-generated-name", CreatedComment));
                data = JsonValue.Create(true)!;
            }
            else if (path.EndsWith("student/batch"))
            {
                Entries.Add(JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject());
                data = JsonValue.Create(200 + Entries.Count)!;
            }
            else throw new InvalidOperationException(path);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new JsonObject { ["isSuccess"] = true, ["data"] = data }.ToJsonString()) };
        }
    }
}
