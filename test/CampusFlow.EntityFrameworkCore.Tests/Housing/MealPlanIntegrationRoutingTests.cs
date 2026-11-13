using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.StudentInformationSystems;
using Microsoft.Extensions.Caching.Memory;
using CampusFlow.MealPlanTaxes;
using NSubstitute;
using Xunit;
namespace CampusFlow.EntityFrameworkCore.Tests.Housing;
public class MealPlanIntegrationRoutingTests
{
    [Fact]public async Task Assignment_uses_integration_API_instead_of_the_incompatible_gateway()
    {
        var handler=new Handler();var cache=new MemoryCache(new MemoryCacheOptions());cache.Set("ThesisElements:MealPlan:IntegrationToken","test");
        var service=new ThesisElementsMealPlanService(HousingCapacityWriterTests.Config(),cache,new HttpClient(handler));await service.AssignAsync("123",20);Assert.Equal(1,handler.Calls);
    }
    [Fact]public async Task Successful_assignment_is_followed_by_shared_batch_reconciliation()
    {
        var handler=new Handler();using var cache=new MemoryCache(new MemoryCacheOptions());cache.Set("ThesisElements:MealPlan:IntegrationToken","test");
        var batches=Substitute.For<IElementsMealPlanBatchService>();
        var service=new ThesisElementsMealPlanService(HousingCapacityWriterTests.Config(),cache,new HttpClient(handler),batches);
        await service.AssignAsync("123",20);Assert.Equal(1,handler.Calls);
        await batches.Received(1).AfterAssignmentAsync(123,20,Arg.Any<CancellationToken>());
    }
    private sealed class Handler:HttpMessageHandler
    {
        public int Calls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct)
        {Calls++;Assert.Equal("app-elm-nonprod-usc-integrationapi.azurewebsites.net",r.RequestUri!.Host);Assert.EndsWith("/api/student-life/housing/meal-plan/assign-meal-plan",r.RequestUri.AbsolutePath);Assert.Equal("{\"mealPlanID\":20,\"studentUID\":123}",await r.Content!.ReadAsStringAsync(ct));return new(HttpStatusCode.OK){Content=new StringContent("{\"isSuccess\":true}",Encoding.UTF8,"application/json")};}
    }
}
