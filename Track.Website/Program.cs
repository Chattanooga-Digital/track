using System.Text.Json;
using System.Text.Json.Serialization;
using Track.Configuration;
using Track.DependencyInjection;
using Track.Website;
using Track.Website.HostedServices;
using Track.Website.Hubs;
using Track.Website.Queries;
using Track.Website.Services;
using Track.Website.Services.Backups;
using Track.Services.Docker;
using Microsoft.AspNetCore.HttpOverrides;
using RoushTech.Asio;
using RoushTech.Asio.Forwarding;
using RoushTech.Asio.InMemory;
using RoushTech.Asio.SignalR;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true);
builder.Configuration.AddEnvironmentVariables();

var trackOptions = builder.Configuration.GetSection(TrackOptions.Section).Get<TrackOptions>() ?? new TrackOptions();

// A job's live log carries Track's own messages only, not the HTTP and framework chatter around them.
builder.Logging.AddFilter<JobSessionLoggerProvider>((category, level) =>
    level >= LogLevel.Information && category?.StartsWith("Track.", StringComparison.Ordinal) == true);

if (trackOptions.Mode == TrackMode.Agent)
{
    // Agent: poll the local Docker socket and push readings to the central instance. No UI, DB, or SignalR.
    builder.Services.Configure<TrackOptions>(builder.Configuration.GetSection(TrackOptions.Section));
    builder.Services.AddDockerCollectors();
    builder.Services.AddSingleton<AgentNodeNameResolver>();
    builder.Services.AddSingleton<AgentSelfUpdater>();
    builder.Services.AddSingleton<AgentTokenStore>();
    builder.Services.AddSingleton<SwarmInventoryCollector>();
    builder.Services.AddSingleton<HookRunner>();
    builder.Services.AddSingleton<BackupJobRunner>();
    builder.Services.AddHttpClient<CentralClient>(CentralClient.Configure);
    builder.Services.AddAsio().AddAsioForwarding().AddSingleton<IAsioForwardingTransport, CentralLogTransport>();
    builder.Services
        .AddHostedService<StatsPushHostedService>()
        .AddHostedService<DiskPushHostedService>()
        .AddHostedService<SwarmInventoryPushHostedService>();

    var agent = builder.Build();
    agent.Logger.LogInformation("Starting in Agent mode, pushing to {Url}", trackOptions.Central.Url);
    agent.MapGet("/health", () => Results.Ok(new { status = "ok" }));
    await agent.RunAsync();
    return;
}

// Central (default): aggregate metrics pushed by agents and serve the UI, API, and live feed.
builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
        // Serialize enums (e.g. S3 source kind) as their names, so the API contract is stable and readable.
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    })
    .Services
    .AddOpenApi()
    .AddSignalR()
    .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
    // Every signed-in user can already see every job, so a session needs no owner check beyond the hub's login.
    .AddAsioSignalR(options => options.UseDefaultOwnerAuthorization = false)
    .Services
    .AddAsio()
    .AddAsioInMemory(options =>
    {
        options.PerSessionLogCapacity = 2000;
        options.MaxSessions = 300;
    })
    .AddSingleton<JobLogs>()
    // AddTrack wires up Identity (AddIdentity + cookie configuration); see Track/DependencyInjection.
    .AddTrack(builder.Configuration)
    .AddScoped<SnapshotBuilder>()
    .AddScoped<S3UsageRefresher>()
    .AddScoped<BillingSampleRecorder>()
    .AddSingleton<AgentEnrollmentRegistry>()
    .AddSingleton<BackupRuntimeState>()
    .AddSingleton<BackupNotifier>()
    .AddSingleton<ResticRunner>()
    .AddSingleton<BackupTargetTester>()
    .AddScoped<ResticTargets>()
    .AddScoped<BackupCatalogService>()
    .AddScoped<BackupJobFactory>()
    .AddScoped<BackupScheduler>()
    .AddScoped<SnapshotIndex>()
    .AddScoped<BackupMaintenance>()
    .AddHttpClient()
    .AddAuthorization()
    .AddHostedService<NodeReaperHostedService>()
    .AddHostedService<SamplePruningHostedService>()
    .AddHostedService<S3UsageHostedService>()
    .AddHostedService<BillingSampleHostedService>()
    .AddHostedService<BackupSchedulerHostedService>()
    .AddHostedService<BackupMaintenanceHostedService>();

var app = builder.Build();

await DatabaseInitializer.InitializeAsync(app.Services);

app
    .UseForwardedHeaders(new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
    })
    .UseDefaultFiles()
    .UseStaticFiles()
    .UseRouting()
    .UseAuthentication()
    .UseAuthorization()
    .UseEndpoints(endpoints =>
    {
        if (app.Environment.IsDevelopment())
        {
            endpoints.MapOpenApi();
        }

        endpoints.MapControllers();
        endpoints.MapHub<TrackHub>(TrackHub.HubPath);
        endpoints.MapAsioHub("/hubs/job-log");
        endpoints.MapFallbackToFile("index.html");
    });

await app.RunAsync();
