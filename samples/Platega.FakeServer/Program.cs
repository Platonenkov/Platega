using Platega.FakeServer;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<FakeOptions>()
    .Bind(builder.Configuration.GetSection(FakeOptions.SectionName))
    .Validate(options => options.MerchantId != Guid.Empty && !string.IsNullOrEmpty(options.Secret), "Fake:MerchantId and Fake:Secret are required.")
    .ValidateOnStart();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<FakeStore>();
builder.Services.AddSingleton<CallbackQueue>();
builder.Services.AddSingleton<PayoutSignatureVerifier>();
builder.Services.AddHttpClient(CallbackDispatcher.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(60));
builder.Services.AddHostedService<CallbackDispatcher>();
builder.Services.AddHostedService<ExpirySweeper>();

WebApplication app = builder.Build();

app.MapPlategaApi();
app.MapFakePages();

app.Run();

/// <summary>Entry point marker for integration tests.</summary>
public partial class Program;
