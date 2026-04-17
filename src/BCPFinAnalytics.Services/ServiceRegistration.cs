using BCPFinAnalytics.DAL;
using BCPFinAnalytics.DAL.Interfaces;
using BCPFinAnalytics.DAL.Repositories;
using BCPFinAnalytics.Services.Interfaces;
using BCPFinAnalytics.Services.Lookup;
using BCPFinAnalytics.Services.Rendering;
using BCPFinAnalytics.Services.Report;
using BCPFinAnalytics.Services.Session;
using BCPFinAnalytics.Services.Preflight;
using BCPFinAnalytics.Services.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BCPFinAnalytics.Services;

/// <summary>
/// Extension method to register all application services in one place.
/// Called from Program.cs — keeps DI registration out of the UI project.
/// </summary>
public static class ServiceRegistration
{
    public static IServiceCollection RegisterApplicationServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ──────────────────────────────────────────────
        //  DAL
        // ──────────────────────────────────────────────
        services.AddSingleton<IDbConnectionFactory, DbConnectionFactory>();
        services.AddScoped<ILookupRepository, LookupRepository>();
        services.AddScoped<ISavedSettingsRepository, SavedSettingsRepository>();
        services.AddScoped<IEntityMetaRepository, EntityMetaRepository>();

        // ──────────────────────────────────────────────
        //  Session (Scoped — one per Blazor circuit)
        // ──────────────────────────────────────────────
        services.AddScoped<UserSessionService>();

        // ──────────────────────────────────────────────
        //  Services
        // ──────────────────────────────────────────────
        services.AddScoped<ILookupService, LookupService>();
        services.AddScoped<ISavedSettingsService, SavedSettingsService>();
        services.AddScoped<IReportStrategyResolver, ReportStrategyResolver>();
        services.AddScoped<IPivotService, PivotService>();
        services.AddScoped<IReportPreflightService, ReportPreflightService>();

        // ──────────────────────────────────────────────
        //  Renderers (Scoped — stateless but scoped for logging context)
        // ──────────────────────────────────────────────
        services.AddScoped<IExcelRenderer, ExcelRenderer>();
        services.AddScoped<IPdfRenderer, PdfRenderer>();
        services.AddScoped<IScreenReportMapper, ScreenReportMapper>();

        // ──────────────────────────────────────────────
        //  AppSettings binding
        // ──────────────────────────────────────────────
        services.Configure<AppSettings>(configuration.GetSection("AppSettings"));

        return services;
    }
}
