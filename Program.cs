using Valkyrie;
using Valkyrie.Services;

var builder = WebApplication.CreateBuilder(args);

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

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<StatsService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/stats", (StatsService statsService) =>
{
    var currentTime = DateTimeOffset.Now;
    var uptime = currentTime - statsService.StartTime;
    return new { 
        statsService.TotalSubmittedTx,
        statsService.StartTime,
        Uptime = uptime
    };
})
.WithName("GetStats")
.WithOpenApi();

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
