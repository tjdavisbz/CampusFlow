using CampusFlow.Web.Payments;
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace CampusFlow.Payments;

public class PayflowGatewayTests
{
    [Fact]
    public void Parses_length_tagged_and_encoded_nvp_response()
    {
        var values = PayflowGateway.Parse("RESULT=0&RESPMSG=Approved+Transaction&SECURETOKEN[5]=a%2Bb%3Dc");
        values["RESULT"].ShouldBe("0");
        values["RESPMSG"].ShouldBe("Approved Transaction");
        values["SECURETOKEN"].ShouldBe("a+b=c");
    }

    [Fact]
    public async Task Direct_sale_formats_card_request_and_recognizes_approval()
    {
        var handler = new RecordingHandler("RESULT=0&RESPMSG=Approved&PNREF=TESTREF");
        var options = Options.Create(new PayflowOptions
        {
            Partner = "PayPal", Vendor = "vendor", User = "user", Password = "password", TestMode = true
        });
        var gateway = new PayflowGateway(new HttpClient(handler), options);

        var response = await gateway.SaleAsync(20m, "CF123",
            new PayflowCard("4111111111111111", 8, 2030, "123", "75165"), CancellationToken.None);

        response.IsDirectSaleApproved.ShouldBeTrue();
        handler.Body.ShouldContain("TRXTYPE[1]=S");
        handler.Body.ShouldContain("ACCT[16]=4111111111111111");
        handler.Body.ShouldContain("EXPDATE[4]=0830");
        handler.Body.ShouldContain("CVV2[3]=123");
        handler.Body.ShouldContain("PARTNER[6]=PayPal");
    }

    private sealed class RecordingHandler(string responseBody) : HttpMessageHandler
    {
        public string Body { get; private set; } = string.Empty;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(responseBody) };
        }
    }
}
