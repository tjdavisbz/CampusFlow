using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.StudentInformationSystems;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Xunit;
namespace CampusFlow.EntityFrameworkCore.Tests.BillApprovals;
public class DocumentDeliveryResponseTests
{
    private sealed class Handler(HttpStatusCode code,string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromResult(
            new HttpResponseMessage(request.RequestUri!.AbsolutePath.EndsWith("Authenticate")?HttpStatusCode.OK:code)
            {Content=new StringContent(request.RequestUri.AbsolutePath.EndsWith("Authenticate")?"{\"data\":{\"token\":\"fake\"}}":body,Encoding.UTF8,"application/json")});
    }
    private static ThesisElementsDocumentTrackingService Service(HttpStatusCode status,string body)
    {
        var p="StudentInformationSystems:Providers:ThesisElements:Api:";
        var configuration=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
        {["BillApproval:DocumentTracking:Enabled"]="true",[p+"BaseUrl"]="https://test.invalid",[p+"TenantHost"]="test.invalid",[p+"Username"]="fake",[p+"Password"]="fake"}).Build();
        return new(configuration,new MemoryCache(new MemoryCacheOptions()),new HttpClient(new Handler(status,body)));
    }
    [Fact] public async Task A_server_error_is_not_treated_as_a_missing_image()=>await Assert.ThrowsAsync<HttpRequestException>(()=>Service(HttpStatusCode.InternalServerError,"{}").HasImageAsync("123"));
    [Fact] public async Task A_failed_json_response_does_not_trigger_an_image_replacement()=>await Assert.ThrowsAsync<InvalidOperationException>(()=>Service(HttpStatusCode.OK,"{\"isSuccess\":false}").HasImageAsync("123"));
    [Fact] public async Task A_genuine_missing_image_can_be_uploaded()=>Assert.False(await Service(HttpStatusCode.NotFound,"{}").HasImageAsync("123"));
    [Fact] public async Task An_upload_requires_explicit_confirmation()=>await Assert.ThrowsAsync<InvalidOperationException>(()=>Service(HttpStatusCode.OK,"{\"isSuccess\":false}").UploadImageAsync("123","approved.pdf",new byte[]{1}));
}
