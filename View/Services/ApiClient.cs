using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Model;

namespace View.Services;

public sealed class ApiClient(IHttpClientFactory factory, AuthenticationStateProvider authentication)
{
    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, HttpContent? content = null)
    {
        var user = (await authentication.GetAuthenticationStateAsync()).User;
        using var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", user.FindFirst("api_token")?.Value);
        return await factory.CreateClient("SpeakReadyAPI").SendAsync(request);
    }
    public async Task<T?> GetAsync<T>(string path)
    {
        using var response = await SendAsync(HttpMethod.Get, path);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(AppJson.Options);
    }
}
