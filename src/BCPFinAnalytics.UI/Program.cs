using BCPFinAnalytics.Services;
using BCPFinAnalytics.UI.Services;
using MudBlazor.Services;
using Serilog;

// ──────────────────────────────────────────────
//  Bootstrap Serilog from appsettings.json
//  so logging is available from the very first line
// ──────────────────────────────────────────────
var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json",
                 optional: true, reloadOnChange: true)
    .Build();

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(configuration)
    .Enrich.FromLogContext()
    .CreateLogger();

try
{
    Log.Information("BCPFinAnalytics starting up — Version {Version}",
        configuration["AppSettings:AppVersion"] ?? "1.0");

    var builder = WebApplication.CreateBuilder(args);

    // ──────────────────────────────────────────────
    //  Serilog
    // ──────────────────────────────────────────────
    builder.Host.UseSerilog();

    // ──────────────────────────────────────────────
    //  Blazor Server
    // ──────────────────────────────────────────────
    builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents();

    // Increase circuit and SignalR timeouts — DB calls on first load can be slow
    builder.Services.AddServerSideBlazor(options =>
    {
        options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(5);
        options.JSInteropDefaultCallTimeout        = TimeSpan.FromMinutes(2);
        options.MaxBufferedUnacknowledgedRenderBatches = 20;
    });

    builder.Services.AddSignalR(options =>
    {
        options.ClientTimeoutInterval        = TimeSpan.FromMinutes(5);
        options.HandshakeTimeout             = TimeSpan.FromSeconds(30);
        options.KeepAliveInterval            = TimeSpan.FromSeconds(15);
        options.MaximumReceiveMessageSize    = 32 * 1024;
    });

    // ──────────────────────────────────────────────
    //  MudBlazor
    // ──────────────────────────────────────────────
    builder.Services.AddMudServices();
    builder.Services.AddScoped<AlertService>();

    // ──────────────────────────────────────────────
    //  HttpContextAccessor — needed for URL param capture
    // ──────────────────────────────────────────────
    builder.Services.AddHttpContextAccessor();

    // ──────────────────────────────────────────────
    //  Application Services
    // ──────────────────────────────────────────────
    builder.Services.RegisterApplicationServices(builder.Configuration);

    // ──────────────────────────────────────────────
    //  Build & configure pipeline
    // ──────────────────────────────────────────────
    var app = builder.Build();

    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error");
        app.UseHsts();
    }

    app.UseHttpsRedirection();
    app.UseStaticFiles();
    app.UseAntiforgery();

    // Serilog request logging — logs each HTTP request
    app.UseSerilogRequestLogging();

    app.MapRazorComponents<BCPFinAnalytics.UI.Components.App>()
        .AddInteractiveServerRenderMode();

    Log.Information("BCPFinAnalytics pipeline configured — listening for requests");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "BCPFinAnalytics failed to start");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
