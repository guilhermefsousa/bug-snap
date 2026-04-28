using BugSnap.Destinations;
using BugSnap.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.JSInterop;

namespace BugSnap.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers BugSnap services. After calling this, wire the HttpActivityTracker
    /// into your HttpClient pipeline to enable HTTP activity tracking:
    /// <code>
    /// builder.Services.AddHttpClient("Api", client => { ... })
    ///     .AddHttpMessageHandler(sp => sp.GetRequiredService&lt;HttpActivityTracker&gt;());
    /// </code>
    /// </summary>
    public static IServiceCollection AddBugSnap(
        this IServiceCollection services,
        Action<BugSnapOptions> configure)
    {
        var options = new BugSnapOptions();
        configure(options);

        services.AddSingleton(options);

        // Core services
        services.AddSingleton(new HttpActivityBuffer(options.MaxHttpEntries));
        services.AddTransient<HttpActivityTracker>();
        services.AddScoped<JsErrorCollector>();
        services.AddScoped<BugContextCollector>();
        services.AddScoped<MultiDestinationDispatcher>();

        // Default context provider (no-op) — apps can override with their own
        services.TryAddScoped<IBugContextProvider, DefaultBugContextProvider>();

        // Register configured destinations
        foreach (var destination in options.Destinations)
        {
            services.AddSingleton<IBugReportDestination>(destination);
        }

        // Auto-register ConsoleDestination if requested (resolves IJSRuntime from DI)
        if (options.EnableConsoleDestination)
        {
            services.AddSingleton<IBugReportDestination>(sp =>
                new ConsoleDestination(() => sp.GetRequiredService<IJSRuntime>()));
        }

        // Auto-capture services — registered always so [Inject] nullable properties resolve.
        // EnableAutoCapture guards actual dispatch at runtime (OnErrorAsync / JS hook).
        services.AddSingleton<AutoCaptureThrottle>();
        services.AddScoped<AutoCaptureService>();
        services.TryAddScoped<IAutoCaptureTelemetry, NoOpAutoCaptureTelemetry>();

        return services;
    }
}
