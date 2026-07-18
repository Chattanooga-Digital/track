using Track.Configuration;
using Track.Data;
using Track.Data.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Track.Website;

public static class DatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TrackContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<TrackContext>>();

        logger.LogInformation("Applying database migrations...");
        await context.Database.MigrateAsync();

        await SeedAdminAsync(scope.ServiceProvider, logger);
        await SeedS3SourcesAsync(scope.ServiceProvider, context, logger);
        await SeedBillingRateAsync(context);

        // Prime the identity env-var cache so grouping config survives restarts and is served on the first push.
        var identityEnvVarStore = scope.ServiceProvider.GetRequiredService<Track.Services.IdentityEnvVarStore>();
        identityEnvVarStore.Set(await context.IdentityEnvVars
            .OrderBy(e => e.SortOrder)
            .Select(e => e.Name)
            .ToListAsync());

        logger.LogInformation("Database ready.");
    }

    private static async Task SeedAdminAsync(IServiceProvider services, ILogger logger)
    {
        var userManager = services.GetRequiredService<UserManager<User>>();
        var roleManager = services.GetRequiredService<RoleManager<Role>>();

        if (!await roleManager.RoleExistsAsync(Role.Administrator))
            await roleManager.CreateAsync(new Role { Name = Role.Administrator });

        if (await userManager.Users.AnyAsync())
            return;

        var options = services.GetRequiredService<IOptions<TrackOptions>>().Value;
        var admin = new User
        {
            UserName = options.AdminUsername,
            Email = options.AdminEmail ?? $"{options.AdminUsername}@localhost",
            EmailConfirmed = true,
            CreatedAt = DateTime.UtcNow,
        };

        var result = await userManager.CreateAsync(admin, options.AdminPassword);
        if (!result.Succeeded)
        {
            logger.LogError("Failed to seed admin user: {Errors}",
                string.Join("; ", result.Errors.Select(e => e.Description)));
            return;
        }

        await userManager.AddToRoleAsync(admin, Role.Administrator);
        logger.LogWarning(
            "Seeded admin user '{Username}'. Change the password via Track:AdminPassword if it is still the default.",
            options.AdminUsername);
    }

    /// <summary>Ensures the single zero-rate pricing row exists so the UI always has something to edit.</summary>
    private static async Task SeedBillingRateAsync(TrackContext context)
    {
        if (await context.BillingRates.AnyAsync())
            return;

        context.BillingRates.Add(new BillingRate());
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// One-time migration path: seed any S3 sources given in config into the DB when the table is empty.
    /// After this, sources are managed live from the UI; config is no longer consulted.
    /// </summary>
    private static async Task SeedS3SourcesAsync(IServiceProvider services, TrackContext context, ILogger logger)
    {
        var configured = services.GetRequiredService<IOptions<TrackOptions>>().Value.S3Sources;
        if (configured.Count == 0 || await context.S3Sources.AnyAsync())
            return;

        var protector = services.GetRequiredService<Track.Services.S3.S3SourceSecretProtector>();
        foreach (var source in configured)
        {
            if (string.IsNullOrWhiteSpace(source.Name) || string.IsNullOrWhiteSpace(source.Endpoint))
            {
                logger.LogWarning("Skipping config S3 source with missing Name or Endpoint");
                continue;
            }

            context.S3Sources.Add(new Data.Models.S3Source
            {
                Name = source.Name.Trim(),
                Endpoint = source.Endpoint.Trim(),
                Kind = source.Kind,
                AccessKey = source.AccessKey?.Trim() ?? "",
                SecretKeyProtected = protector.Protect(source.SecretKey ?? ""),
                Region = source.Region,
                Enabled = true,
                CreatedAt = DateTime.UtcNow,
            });
        }

        await context.SaveChangesAsync();
        logger.LogInformation("Seeded {Count} S3 source(s) from config into the database.", configured.Count);
    }
}
