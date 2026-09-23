using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Platega.Http;

internal static class PlategaResponseReader
{
    /// <summary>
    /// Reads a JSON body regardless of the declared content type: some endpoints answer <c>text/plain</c> with a JSON body.
    /// </summary>
    public static async Task<T> ReadAsync<T>(
        HttpResponseMessage response,
        string endpoint,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
    {
        byte[] body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new PlategaApiException(response.StatusCode, endpoint, Encoding.UTF8.GetString(body));
        }

        if (body.Length == 0)
        {
            throw new PlategaApiException(response.StatusCode, endpoint, responseBody: null);
        }

        try
        {
            T? result = JsonSerializer.Deserialize(body, typeInfo);
            return result ?? throw new PlategaApiException(HttpStatusCode.OK, endpoint, Encoding.UTF8.GetString(body));
        }
        catch (JsonException exception)
        {
            throw new PlategaApiException(HttpStatusCode.OK, endpoint, Encoding.UTF8.GetString(body), exception);
        }
    }
}
