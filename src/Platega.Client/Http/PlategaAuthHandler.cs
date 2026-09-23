using Microsoft.Extensions.Options;
using Platega.Serialization;

namespace Platega.Http;

/// <summary>
/// Adds <c>X-MerchantId</c> / <c>X-Secret</c> to every request of the main API client.
/// Reads the options per request so that a rotated key is picked up without restarting.
/// </summary>
internal sealed class PlategaAuthHandler(IOptionsMonitor<PlategaOptions> options) : DelegatingHandler
{
    internal const string MerchantIdHeader = "X-MerchantId";
    internal const string SecretHeader = "X-Secret";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        PlategaOptions current = options.CurrentValue;
        request.Headers.Remove(MerchantIdHeader);
        request.Headers.Remove(SecretHeader);
        request.Headers.TryAddWithoutValidation(MerchantIdHeader, PlategaFormat.FormatId(current.MerchantId));
        request.Headers.TryAddWithoutValidation(SecretHeader, current.Secret);
        return base.SendAsync(request, cancellationToken);
    }
}
