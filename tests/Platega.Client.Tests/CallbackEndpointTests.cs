using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Platega.AspNetCore;
using Platega.Callbacks;
using Platega.Tests.Infrastructure;

namespace Platega.Tests;

public sealed class CallbackEndpointTests : IAsyncLifetime
{
    private readonly RecordingHandler _handler = new RecordingHandler();
    private WebApplication? _app;
    private HttpClient? _client;

    public async ValueTask InitializeAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddPlatega(options =>
        {
            options.MerchantId = PlategaTestHost.MerchantId;
            options.Secret = PlategaTestHost.Secret;
        });
        builder.Services.AddSingleton<IPlategaCallbackHandler>(_handler);

        _app = builder.Build();
        _app.MapPlategaCallback();
        await _app.StartAsync(TestContext.Current.CancellationToken);
        _client = _app.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    [Fact]
    public async Task AuthenticCallback_IsHandledAndAcknowledged()
    {
        using HttpResponseMessage response = await PostAsync(Fixture.Read("callback-payment.json"), PlategaTestHost.Secret);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        PlategaCallback callback = Assert.Single(_handler.Handled);
        Assert.Equal(PaymentStatus.Confirmed, callback.PaymentStatus);
        Assert.Empty(_handler.Rejected);
    }

    [Fact]
    public async Task WrongSecret_IsRejectedWithoutHandling()
    {
        using HttpResponseMessage response = await PostAsync(Fixture.Read("callback-payment.json"), "forged");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_handler.Handled);
        Assert.Equal(PlategaCallbackRejectionReason.Unauthorized, Assert.Single(_handler.Rejected).Reason);
    }

    [Fact]
    public async Task InvalidBody_IsRejectedWithBadRequest()
    {
        using HttpResponseMessage response = await PostAsync("{\"status\":\"CONFIRMED\"}", PlategaTestHost.Secret);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(PlategaCallbackRejectionReason.InvalidPayload, Assert.Single(_handler.Rejected).Reason);
    }

    [Fact]
    public async Task HandlerFailure_Returns500SoPlategaRetries()
    {
        _handler.FailNext = true;

        using HttpResponseMessage response = await PostAsync(Fixture.Read("callback-payment.json"), PlategaTestHost.Secret);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task OversizedBody_IsRejected()
    {
        string body = new string('x', PlategaEndpointRouteBuilderExtensions.MaxBodyBytes + 1);

        using HttpResponseMessage response = await PostAsync(body, PlategaTestHost.Secret);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Empty(_handler.Handled);
    }

    private async Task<HttpResponseMessage> PostAsync(string body, string secret)
    {
        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "/platega/callback")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-MerchantId", PlategaTestHost.MerchantId.ToString());
        request.Headers.Add("X-Secret", secret);
        return await _client!.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private sealed class RecordingHandler : IPlategaCallbackHandler
    {
        public List<PlategaCallback> Handled { get; } = [];

        public List<PlategaCallbackRejection> Rejected { get; } = [];

        public bool FailNext { get; set; }

        public Task HandleAsync(PlategaCallback callback, CancellationToken cancellationToken)
        {
            if (FailNext)
            {
                FailNext = false;
                throw new InvalidOperationException("Database is down");
            }

            Handled.Add(callback);
            return Task.CompletedTask;
        }

        public Task OnRejectedAsync(PlategaCallbackRejection rejection, CancellationToken cancellationToken)
        {
            Rejected.Add(rejection);
            return Task.CompletedTask;
        }
    }
}
