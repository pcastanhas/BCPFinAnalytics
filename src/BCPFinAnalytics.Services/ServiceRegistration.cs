using BCPFinAnalytics.DAL;
using BCPFinAnalytics.DAL.Interfaces;
using BCPFinAnalytics.DAL.Repositories;
using BCPFinAnalytics.Common.Interfaces;
using BCPFinAnalytics.Services.Interfaces;
using BCPFinAnalytics.Services.Lookup;
using BCPFinAnalytics.Services.Rendering;
using BCPFinAnalytics.Services.Report;
using BCPFinAnalytics.Services.Session;
using BCPFinAnalytics.Services.Format;
using BCPFinAnalytics.Services.GlDetail;
using BCPFinAnalytics.Services.BudgetDetail;
using BCPFinAnalytics.Services.Helpers;
using BCPFinAnalytics.Services.Reports.TrialBalance;
using BCPFinAnalytics.Services.Reports.TrialBalanceDC;
using BCPFinAnalytics.Services.Reports.IncomeStatement;
using BCPFinAnalytics.Services.Reports.Trailing12;
using BCPFinAnalytics.Services.Reports.Forecast12;
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
        services.AddScoped<IFormatRepository, FormatRepository>();
        services.AddScoped<IBalForRepository, BalForRepository>();
        services.AddScoped<IUnpostedRERepository, UnpostedRERepository>();
        services.AddScoped<IGlDrillDownRepository, GlDrillDownRepository>();
        services.AddScoped<IStartingBalanceRepository, StartingBalanceRepository>();
        services.AddScoped<IBudgetDrillDownRepository, BudgetDrillDownRepository>();
        services.AddScoped<IGlDataRepository, GlDataRepository>();
        services.AddScoped<IBudgetDataRepository, BudgetDataRepository>();

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
        services.AddScoped<IFormatLoader, FormatLoader>();
        services.AddScoped<EntitySelectionResolver>();
        services.AddScoped<GlFilterBuilder>();
        services.AddScoped<IUnpostedREService, UnpostedREService>();

        // ──────────────────────────────────────────────
        //  Report Strategies — add one entry per report
        // ──────────────────────────────────────────────
        services.AddScoped<IReportStrategy, TrialBalanceStrategy>();
        services.AddScoped<ITrialBalanceDCRepository, TrialBalanceDCRepository>();
        services.AddScoped<IReportStrategy, TrialBalanceDCStrategy>();
        services.AddScoped<IIncomeStatementRepository, IncomeStatementRepository>();
        services.AddScoped<IReportStrategy, IncomeStatementStrategy>();
        services.AddScoped<ITrailing12Repository, Trailing12Repository>();
        services.AddScoped<IReportStrategy, Trailing12Strategy>();
        services.AddScoped<IReportStrategy, Trailing12BudgetStrategy>();
        services.AddScoped<IForecast12Repository, Forecast12Repository>();
        services.AddScoped<IReportStrategy, Forecast12Strategy>();
        services.AddScoped<IGlDrillDownService, GlDrillDownService>();
        services.AddScoped<IStartingBalanceService, StartingBalanceService>();
        services.AddScoped<IBudgetDrillDownService, BudgetDrillDownService>();

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
