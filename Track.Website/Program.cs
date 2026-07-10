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

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
    });
builder.Services.AddOpenApi();
builder.Services.AddSignalR();

// AddTrack wires up Identity (AddIdentity + cookie configuration); see Track/DependencyInjection.
builder.Services.AddTrack(builder.Configuration);
builder.Services.AddScoped<SnapshotBuilder>();

builder.Services.AddAuthorization();

var corsOrigins = builder.Configuration
    .GetSection($"{TrackOptions.Section}:CorsOrigins")
    .Get<string[]>()
    ?? ["http://localhost:5173", "https://localhost:5173"];

builder.Services.AddCors(options =>
{
    options.AddPolicy("VueDevServer", policy =>
    {
        policy.WithOrigins(corsOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

builder.Services.AddHostedService<StatsPollingHostedService>();
builder.Services.AddHostedService<DiskPollingHostedService>();
builder.Services.AddHostedService<SamplePruningHostedService>();

var app = builder.Build();

await DatabaseInitializer.InitializeAsync(app.Services);

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseRouting();
app.UseCors("VueDevServer");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<TrackHub>(TrackHub.HubPath);

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

await app.RunAsync();
