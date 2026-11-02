using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DeviceGlance.Core;
using DeviceGlance.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace DeviceGlance.Sources;

/// <summary>LAN endpoint the iPad (iOS Shortcuts) posts its battery level to.</summary>
public sealed class ReportServer : IAsyncDisposable
{
    public const string TokenHeader = "X-DeviceGlance-Token";

    private readonly WebApplication _app;

    /// <param name="onReport">Receives each validated report; runs on a Kestrel thread.</param>
    /// <param name="getRows">Returns the current rows; must be safe to call from any thread.</param>
    public ReportServer(AppSettings settings, Func<PushReport, Task> onReport, Func<IReadOnlyList<DeviceRow>> getRows)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://0.0.0.0:{settings.Port}");
        _app = builder.Build();

        var expected = Encoding.UTF8.GetBytes(settings.Token);

        _app.Use(async (ctx, next) =>
        {
            var given = Encoding.UTF8.GetBytes(ctx.Request.Headers[TokenHeader].ToString());
            if (!CryptographicOperations.FixedTimeEquals(given, expected))
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
            await next();
        });

        _app.MapPost("/api/report", async (HttpContext ctx) =>
        {
            JsonElement body;
            try
            {
                body = (await JsonDocument.ParseAsync(ctx.Request.Body, cancellationToken: ctx.RequestAborted)).RootElement;
            }
            catch (JsonException)
            {
                return Results.Json(new { error = "body invalid" }, statusCode: StatusCodes.Status400BadRequest);
            }

            if (!ReportValidator.TryParse(body, DateTimeOffset.Now, out var report, out var error))
                return Results.Json(new { error }, statusCode: StatusCodes.Status400BadRequest);

            await onReport(report!);
            return Results.NoContent();
        });

        _app.MapGet("/api/devices", () => Results.Json(getRows().Select(r => new
        {
            key = r.Key,
            name = r.Name,
            connected = r.IsConnected,
            battery = r.Battery?.Percent,
            charging = r.Battery?.Charging,
            source = r.Battery?.Source.ToString(),
            live = r.BatteryIsLive,
            subtitle = r.Subtitle,
        })));
    }

    /// <exception cref="IOException">The port is already in use.</exception>
    public Task StartAsync() => _app.StartAsync();

    public Task StopAsync() => _app.StopAsync();

    public ValueTask DisposeAsync() => _app.DisposeAsync();
}
