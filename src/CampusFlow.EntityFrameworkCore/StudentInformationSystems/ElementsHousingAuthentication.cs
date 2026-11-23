using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

namespace CampusFlow.StudentInformationSystems;

// Authentication and failed reads can be retried; assignment and financial writes cannot.
internal static class ElementsHousingAuthentication
{
    private const string Prefix="StudentInformationSystems:Providers:ThesisElements:Api:";
    private static readonly ConditionalWeakTable<IMemoryCache,SemaphoreSlim> Gates=new();
    internal static string Key(IConfiguration cfg)=>"ThesisElements:Housing:Token:"+cfg[Prefix+"BaseUrl"]+":"+cfg[Prefix+"TenantHost"]+":"+cfg[Prefix+"Username"];
    internal static async Task<string> GetAsync(IConfiguration cfg,IMemoryCache cache,HttpClient client,CancellationToken ct)
    {
        var key=Key(cfg);
        if(cache.TryGetValue(key,out string? cached)&&!string.IsNullOrWhiteSpace(cached))return cached;
        var gate=Gates.GetValue(cache,_=>new SemaphoreSlim(1,1));await gate.WaitAsync(ct);
        try
        {
            if(cache.TryGetValue(key,out cached)&&!string.IsNullOrWhiteSpace(cached))return cached;
            for(var attempt=0;attempt<3;attempt++)
            {
                using var request=new HttpRequestMessage(HttpMethod.Get,cfg[Prefix+"BaseUrl"]!.TrimEnd('/')+"/api/Login/Authenticate");
                request.Headers.Add("TenantHost",cfg[Prefix+"TenantHost"]);
                request.Headers.Authorization=new AuthenticationHeaderValue("Basic",Convert.ToBase64String(Encoding.UTF8.GetBytes(cfg[Prefix+"Username"]+":"+cfg[Prefix+"Password"])));
                using var response=await client.SendAsync(request,ct);
                var status=(int)response.StatusCode;
                if(attempt<2&&status is 408 or 429 or 500 or 502 or 503 or 504)
                {await Task.Delay(TimeSpan.FromMilliseconds(250*(attempt+1)),ct);continue;}
                if(!response.IsSuccessStatusCode)throw new InvalidOperationException($"Elements housing authentication failed (HTTP {status}). No housing write was retried.");
                JsonNode? body;
                try{body=JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));}
                catch(System.Text.Json.JsonException){throw new InvalidOperationException("Elements returned an invalid housing authentication response.");}
                var token=body?["data"]?["token"]?.GetValue<string>();
                if(string.IsNullOrWhiteSpace(token)||body?["isSuccess"]?.GetValue<bool>()==false)
                    throw new InvalidOperationException("Elements did not return a housing authentication token.");
                cache.Set(key,token,TimeSpan.FromMinutes(5));return token;
            }
            throw new InvalidOperationException("Elements housing authentication is temporarily unavailable.");
        }
        finally{gate.Release();}
    }
    internal static async Task<HttpResponseMessage> SendAsync(IConfiguration cfg,IMemoryCache cache,HttpClient client,HttpRequestMessage request,CancellationToken ct)
    {
        var response=await client.SendAsync(request,ct);
        if(response.StatusCode!=HttpStatusCode.Unauthorized)return response;
        cache.Remove(Key(cfg));
        if(request.Method!=HttpMethod.Get)return response;
        response.Dispose();
        var token=await GetAsync(cfg,cache,client,ct);
        using var retry=new HttpRequestMessage(HttpMethod.Get,request.RequestUri);
        foreach(var header in request.Headers)if(!header.Key.Equals("Authorization",StringComparison.OrdinalIgnoreCase))retry.Headers.TryAddWithoutValidation(header.Key,header.Value);
        retry.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
        var result=await client.SendAsync(retry,ct);
        if(result.StatusCode==HttpStatusCode.Unauthorized)cache.Remove(Key(cfg));
        return result;
    }
}
