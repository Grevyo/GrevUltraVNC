using GrevUltraVNC.Agent;
using GrevUltraVNC.Contracts;
using Microsoft.Extensions.Hosting.WindowsServices;

var serviceConfiguration = AgentConfiguration.LoadOrCreate();
var options = new WebApplicationOptions
{
    Args = args,
    ContentRootPath = OperatingSystem.IsWindows() && WindowsServiceHelpers.IsWindowsService()
        ? AppContext.BaseDirectory
        : default
};

var builder = WebApplication.CreateBuilder(options);
builder.Host.UseWindowsService(serviceOptions =>
{
    serviceOptions.ServiceName = "GrevUltraVNC Agent";
});

builder.WebHost.UseUrls($"http://0.0.0.0:{serviceConfiguration.Port}");
builder.Services.AddSingleton(serviceConfiguration);
builder.Services.AddSingleton<AgentRequestAuthenticator>();
builder.Services.AddSingleton<SystemTelemetryService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<SystemTelemetryService>());

var app = builder.Build();

app.Use(async (context, next) =>
{
    var isPing = string.Equals(
        context.Request.Path.Value,
        AgentProtocol.PingPath,
        StringComparison.OrdinalIgnoreCase);

    if (!context.Request.Path.StartsWithSegments("/api/v1") || isPing)
    {
        await next();
        return;
    }

    var authenticator = context.RequestServices.GetRequiredService<AgentRequestAuthenticator>();
    if (!await authenticator.IsAuthorizedAsync(context.Request, context.RequestAborted))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = "Grev agent authentication failed." }, context.RequestAborted);
        return;
    }

    await next();
});

app.MapGet("/", () => Results.Json(new
{
    product = "GrevUltraVNC Agent",
    api = "v1",
    status = "running"
}));

app.MapGet(AgentProtocol.PingPath, () =>
{
    var version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "dev";
    return Results.Json(new AgentPingResponse(
        "GrevUltraVNC Agent",
        version,
        Environment.MachineName,
        true,
        1));
});

app.MapGet(AgentProtocol.StatusPath, async (SystemTelemetryService telemetry, CancellationToken cancellationToken) =>
    Results.Json(await telemetry.CaptureAsync(cancellationToken)));

await app.RunAsync();
