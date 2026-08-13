using Microsoft.AspNetCore.Builder;
using CampusFlow;
using Volo.Abp.AspNetCore.TestBase;

var builder = WebApplication.CreateBuilder();
builder.Environment.ContentRootPath = GetWebProjectContentRootPathHelper.Get("CampusFlow.Web.csproj"); 
await builder.RunAbpModuleAsync<CampusFlowWebTestModule>(applicationName: "CampusFlow.Web");

public partial class Program
{
}
