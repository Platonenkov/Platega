using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Platega.Http;

/// <summary>
/// Typed HTTP client for the header-authenticated part of the API. No automatic retries:
/// <c>POST /transaction/process</c> has no idempotency key, so a blind retry could create a duplicate payment.
/// </summary>
internal sealed class PlategaConnection(HttpClient httpClient)
{
    private static readonly MediaTypeWithQualityHeaderValue JsonMediaType = new MediaTypeWithQualityHeaderValue("application/json");
    private static readonly MediaTypeWithQualityHeaderValue TextMediaType = new MediaTypeWithQualityHeaderValue("text/plain", 0.5);

    public Task<TResponse> GetAsync<TResponse>(string path, JsonTypeInfo<TResponse> responseType, CancellationToken cancellationToken)
    {
        HttpRequestMessage request = CreateRequest(HttpMethod.Get, path);
        return SendAsync(request, path, responseType, cancellationToken);
    }

    public Task<TResponse> PostAsync<TResponse>(string path, JsonTypeInfo<TResponse> responseType, CancellationToken cancellationToken)
    {
        HttpRequestMessage request = CreateRequest(HttpMethod.Post, path);
        return SendAsync(request, path, responseType, cancellationToken);
    }

    public Task<TResponse> PostAsync<TRequest, TResponse>(
        string path,
        TRequest body,
        JsonTypeInfo<TRequest> requestType,
        JsonTypeInfo<TResponse> responseType,
        CancellationToken cancellationToken)
    {
        HttpRequestMessage request = CreateRequest(HttpMethod.Post, path);
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(body, requestType);
        ByteArrayContent content = new ByteArrayContent(payload);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        request.Content = content;
        return SendAsync(request, path, responseType, cancellationToken);
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        HttpRequestMessage request = new HttpRequestMessage(method, path);
        request.Headers.Accept.Add(JsonMediaType);
        request.Headers.Accept.Add(TextMediaType);
        return request;
    }

    private async Task<TResponse> SendAsync<TResponse>(
        HttpRequestMessage request,
        string path,
        JsonTypeInfo<TResponse> responseType,
        CancellationToken cancellationToken)
    {
        using (request)
        {
            using HttpResponseMessage response = await httpClient
                .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                .ConfigureAwait(false);

            return await PlategaResponseReader
                .ReadAsync(response, $"{request.Method} {path}", responseType, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
