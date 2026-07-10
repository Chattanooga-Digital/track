using System.Text.Json;
using Track.Configuration;
using Track.DependencyInjection;
using Track.Website;
using Track.Website.HostedServices;
using Track.Website.Hubs;
using Track.Website.Queries;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true);
builder.Configuration.AddEnvironmentVariables();

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
    })
    .Services
    .AddOpenApi()
    .AddSignalR()
    .Services
    // AddTrack wires up Identity (AddIdentity + cookie configuration); see Track/DependencyInjection.
    .AddTrack(builder.Configuration)
    .AddScoped<SnapshotBuilder>()
    .AddAuthorization()
    .AddHostedService<StatsPollingHostedService>()
    .AddHostedService<DiskPollingHostedService>()
    .AddHostedService<SamplePruningHostedService>();

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
        endpoints.MapFallbackToFile("index.html");
    });

await app.RunAsync();
