using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.StudentInformationSystems;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Xunit;
namespace CampusFlow.EntityFrameworkCore.Tests.Housing;
public class HousingAuthenticationTests
{
    private static IConfiguration Config(string host="nonprod")=>new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{
        ["StudentInformationSystems:Providers:ThesisElements:Api:BaseUrl"]="https://"+host+".example.test",["StudentInformationSystems:Providers:ThesisElements:Api:TenantHost"]=host,
        ["StudentInformationSystems:Providers:ThesisElements:Api:Username"]="test",["StudentInformationSystems:Providers:ThesisElements:Api:Password"]="private-test-secret"}).Build();
    private sealed class Handler(Func<HttpRequestMessage,int,HttpResponseMessage> response):HttpMessageHandler
    {public int Count;public List<HttpMethod> Methods=[];protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){Methods.Add(request.Method);return Task.FromResult(response(request,Interlocked.Increment(ref Count)));}}
    private static HttpResponseMessage Auth()=>new(HttpStatusCode.OK){Content=new StringContent("{\"isSuccess\":true,\"data\":{\"token\":\"fresh\"}}")};
    [Fact]public async Task Concurrent_readers_share_one_authentication_request()
    {using var cache=new MemoryCache(new MemoryCacheOptions());var h=new Handler((_,_)=>Auth());using var client=new HttpClient(h);var tasks=Enumerable.Range(1,20).Select(_=>ElementsHousingAuthentication.GetAsync(Config(),cache,client,CancellationToken.None));Assert.All(await Task.WhenAll(tasks),x=>Assert.Equal("fresh",x));Assert.Equal(1,h.Count);}
    [Theory][InlineData(429)][InlineData(503)]public async Task Transient_authentication_failures_retry_without_replaying_any_write(int code)
    {using var cache=new MemoryCache(new MemoryCacheOptions());var h=new Handler((_,n)=>n<3?new((HttpStatusCode)code):Auth());using var client=new HttpClient(h);Assert.Equal("fresh",await ElementsHousingAuthentication.GetAsync(Config(),cache,client,CancellationToken.None));Assert.Equal(3,h.Count);Assert.All(h.Methods,x=>Assert.Equal(HttpMethod.Get,x));}
    [Fact]public async Task Invalid_credentials_are_not_retried_or_exposed()
    {using var cache=new MemoryCache(new MemoryCacheOptions());var h=new Handler((_,_)=>new(HttpStatusCode.Unauthorized){Content=new StringContent("private-test-secret")});using var client=new HttpClient(h);var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>ElementsHousingAuthentication.GetAsync(Config(),cache,client,CancellationToken.None));Assert.DoesNotContain("private-test-secret",error.ToString());Assert.Equal(1,h.Count);}
    [Fact]public async Task A_rejected_cached_read_refreshes_authentication_and_retries_only_that_read()
    {using var cache=new MemoryCache(new MemoryCacheOptions());var cfg=Config();cache.Set(ElementsHousingAuthentication.Key(cfg),"expired");var h=new Handler((req,n)=>req.RequestUri!.AbsolutePath.Contains("Authenticate")?Auth():new(n==1?HttpStatusCode.Unauthorized:HttpStatusCode.OK));using var client=new HttpClient(h);using var request=new HttpRequestMessage(HttpMethod.Get,"https://nonprod.example.test/rooms");using var response=await ElementsHousingAuthentication.SendAsync(cfg,cache,client,request,CancellationToken.None);Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Equal(3,h.Count);Assert.All(h.Methods,x=>Assert.Equal(HttpMethod.Get,x));}
    [Theory][InlineData("POST")][InlineData("PUT")][InlineData("DELETE")]public async Task A_rejected_write_is_never_replayed(string method)
    {using var cache=new MemoryCache(new MemoryCacheOptions());var cfg=Config();cache.Set(ElementsHousingAuthentication.Key(cfg),"expired");var h=new Handler((_,_)=>new(HttpStatusCode.Unauthorized));using var client=new HttpClient(h);using var request=new HttpRequestMessage(new(method),"https://nonprod.example.test/rooms");using var response=await ElementsHousingAuthentication.SendAsync(cfg,cache,client,request,CancellationToken.None);Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode);Assert.Equal(1,h.Count);Assert.False(cache.TryGetValue(ElementsHousingAuthentication.Key(cfg),out _));}
    [Fact]public async Task Environment_tokens_do_not_share_a_cache_entry()
    {using var cache=new MemoryCache(new MemoryCacheOptions());var h=new Handler((_,_)=>Auth());using var client=new HttpClient(h);await ElementsHousingAuthentication.GetAsync(Config("nonprod"),cache,client,CancellationToken.None);await ElementsHousingAuthentication.GetAsync(Config("production"),cache,client,CancellationToken.None);Assert.Equal(2,h.Count);}
}
