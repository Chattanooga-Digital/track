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

        var migrations = context.Database.GetMigrations();
        if (migrations.Any())
        {
            logger.LogInformation("Applying database migrations...");
            await context.Database.MigrateAsync();
        }
        else
        {
            logger.LogInformation("No migrations found. Ensuring database schema is created...");
            await context.Database.EnsureCreatedAsync();
        }

        await SeedAdminAsync(scope.ServiceProvider, logger);

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
}
