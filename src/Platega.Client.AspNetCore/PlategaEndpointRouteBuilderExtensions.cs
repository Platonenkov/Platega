using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Platega.Callbacks;

namespace Platega.AspNetCore;

/// <summary>Maps the Platega callback endpoint.</summary>
public static class PlategaEndpointRouteBuilderExtensions
{
    /// <summary>Maximum accepted callback body size.</summary>
    public const int MaxBodyBytes = 64 * 1024;

    /// <summary>
    /// Maps <c>POST {pattern}</c> for Platega callbacks. Responses: 200 for a reachability probe (empty body or <c>{}</c>,
    /// sent by Platega when the URL is saved in the cabinet), 401 when the <c>X-MerchantId</c>/<c>X-Secret</c>
    /// headers do not match, 400 for an invalid body, 200 after <see cref="IPlategaCallbackHandler"/> succeeds.
    /// A handler exception yields 500, so Platega retries the delivery.
    /// Requires <c>AddPlatega(...)</c> and a registered <see cref="IPlategaCallbackHandler"/>.
    /// </summary>
    public static IEndpointConventionBuilder MapPlategaCallback(this IEndpointRouteBuilder endpoints, string pattern = "/platega/callback")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);

        // Platega authenticates only with X-MerchantId/X-Secret, which the handler verifies itself; host-level
        // authorization (e.g. a fallback policy requiring a signed-in user) would otherwise reject every callback.
        return endpoints
            .MapPost(pattern, HandleAsync)
            .AllowAnonymous()
            .DisableAntiforgery()
            .WithName("PlategaCallback");
    }

    private static async Task HandleAsync(HttpContext context)
    {
        // Status code pages would replace empty 4xx/5xx answers (or re-execute them as another request),
        // hiding the status Platega relies on for retries.
        if (context.Features.Get<IStatusCodePagesFeature>() is { } statusCodePages)
        {
            statusCodePages.Enabled = false;
        }

        IResult result = await ProcessAsync(context).ConfigureAwait(false);
        await result.ExecuteAsync(context).ConfigureAwait(false);
    }

    private static async Task<IResult> ProcessAsync(HttpContext context)
    {
        CancellationToken cancellationToken = context.RequestAborted;
        IServiceProvider services = context.RequestServices;
        PlategaCallbackParser parser = services.GetRequiredService<PlategaCallbackParser>();
        IPlategaCallbackHandler handler = services.GetRequiredService<IPlategaCallbackHandler>();
        ILogger logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(PlategaEndpointRouteBuilderExtensions).FullName!);

        byte[]? body = await ReadBodyAsync(context.Request, cancellationToken).ConfigureAwait(false);
        if (body is null)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        string rawBody = System.Text.Encoding.UTF8.GetString(body);
        string? merchantId = context.Request.Headers[PlategaCallbackParser.MerchantIdHeader];
        string? secret = context.Request.Headers[PlategaCallbackParser.SecretHeader];

        bool authentic = parser.IsAuthentic(merchantId, secret);
        if (PlategaCallbackParser.IsProbe(body))
        {
            logger.LogInformation("Platega callback URL probe from {RemoteIp}, authenticated: {Authenticated}", context.Connection.RemoteIpAddress, authentic);
            await handler.OnProbeAsync(new PlategaCallbackProbe(authentic, rawBody), cancellationToken).ConfigureAwait(false);
            return Results.Ok();
        }

        if (!authentic)
        {
            string headers = PlategaCallbackParser.DescribeHeaders(merchantId, secret);
            logger.LogWarning("Rejected Platega callback from {RemoteIp}: invalid credentials ({Headers})", context.Connection.RemoteIpAddress, headers);
            await handler
                .OnRejectedAsync(new PlategaCallbackRejection(PlategaCallbackRejectionReason.Unauthorized, rawBody, $"Invalid X-MerchantId or X-Secret ({headers})."), cancellationToken)
                .ConfigureAwait(false);
            return Results.Unauthorized();
        }

        if (!PlategaCallbackParser.TryParse(body, out PlategaCallback? callback, out string? error))
        {
            logger.LogWarning("Rejected Platega callback: {Error}", error);
            await handler
                .OnRejectedAsync(new PlategaCallbackRejection(PlategaCallbackRejectionReason.InvalidPayload, rawBody, error), cancellationToken)
                .ConfigureAwait(false);
            return Results.BadRequest();
        }

        logger.LogInformation(
            "Platega callback {Kind} for {Id}: {Status}",
            callback.Kind,
            callback.Id,
            callback.RawStatus);

        try
        {
            await handler.HandleAsync(callback, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception, "Platega callback {Kind} for {Id} failed; Platega will retry", callback.Kind, callback.Id);
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }

        return Results.Ok();
    }

    private static async Task<byte[]?> ReadBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.ContentLength > MaxBodyBytes)
        {
            return null;
        }

        using MemoryStream buffer = new MemoryStream();
        byte[] chunk = new byte[8192];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxBodyBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
