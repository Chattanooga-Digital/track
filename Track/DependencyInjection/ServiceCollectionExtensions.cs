using Track.Configuration;
using Track.Data;
using Track.Data.Models;
using Track.Services;
using Track.Services.Docker;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Track.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTrack(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(TrackOptions.Section).Get<TrackOptions>() ?? new TrackOptions();
        services.Configure<TrackOptions>(configuration.GetSection(TrackOptions.Section));

        services.AddDbContext<TrackContext>(dbOptions =>
            dbOptions.UseSqlite(configuration.GetConnectionString("Default") ?? "Data Source=track.db"));

        services.AddIdentity<User, Role>(identity =>
            {
                identity.User.RequireUniqueEmail = false;
                identity.Password.RequiredLength = 4;
                identity.Password.RequireDigit = false;
                identity.Password.RequireNonAlphanumeric = false;
                identity.Password.RequireUppercase = false;
                identity.Password.RequireLowercase = false;
                identity.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                identity.Lockout.AllowedForNewUsers = true;
            })
            .AddEntityFrameworkStores<TrackContext>()
            .AddDefaultTokenProviders();

        // This is an API: answer with status codes rather than redirecting to a login page.
        services.ConfigureApplicationCookie(cookie =>
        {
            cookie.Cookie.Name = "track.auth";
            cookie.ExpireTimeSpan = TimeSpan.FromDays(30);
            cookie.SlidingExpiration = true;
            cookie.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            cookie.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });

        if (!string.IsNullOrWhiteSpace(options.DataProtectionKeysPath))
            services.AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(options.DataProtectionKeysPath));

        services.AddSingleton<DockerClientFactory>();
        services.AddSingleton<DockerDfClient>();
        services.AddSingleton<DockerStatsCollector>();
        services.AddSingleton<DockerDiskCollector>();
        services.AddSingleton<CurrentStateHolder>();

        return services;
    }
}
