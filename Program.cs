using Valkyrie;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<Worker>();
builder.Services.AddHttpClient("SubmitApi", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["CardanoSubmitApiUrl"]!);
}).ConfigurePrimaryHttpMessageHandler(() =>
{
    HttpClientHandler handler = new()
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    };
    return handler;
});

var host = builder.Build();
host.Run();
