using Track.Data.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Track.Data;

public class TrackContext(DbContextOptions<TrackContext> options)
    : IdentityDbContext<User, Role, Guid>(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<StackAssignment> StackAssignments => Set<StackAssignment>();
    public DbSet<SharedStack> SharedStacks => Set<SharedStack>();
    public DbSet<SharedBucket> SharedBuckets => Set<SharedBucket>();
    public DbSet<BucketAssignment> BucketAssignments => Set<BucketAssignment>();
    public DbSet<ContainerSample> ContainerSamples => Set<ContainerSample>();
    public DbSet<DiskSample> DiskSamples => Set<DiskSample>();
    public DbSet<BillingSample> BillingSamples => Set<BillingSample>();
    public DbSet<ClusterCapacitySample> ClusterCapacitySamples => Set<ClusterCapacitySample>();
    public DbSet<IdentityEnvVar> IdentityEnvVars => Set<IdentityEnvVar>();
    public DbSet<S3Source> S3Sources => Set<S3Source>();
    public DbSet<BillingRate> BillingRates => Set<BillingRate>();
    public DbSet<AgentEnrollment> AgentEnrollments => Set<AgentEnrollment>();
    public DbSet<BackupTarget> BackupTargets => Set<BackupTarget>();
    public DbSet<BackupSettings> BackupSettings => Set<BackupSettings>();
    public DbSet<BackupRepository> BackupRepositories => Set<BackupRepository>();
    public DbSet<BackupVolumeOverride> BackupVolumeOverrides => Set<BackupVolumeOverride>();
    public DbSet<BackupJob> BackupJobs => Set<BackupJob>();
    public DbSet<BackupSnapshot> BackupSnapshots => Set<BackupSnapshot>();
    public DbSet<MailSettings> MailSettings => Set<MailSettings>();
    public DbSet<OutboundEmail> OutboundEmails => Set<OutboundEmail>();

    // SQLite keeps no time zone, so values come back Unspecified and serialize without a Z; every stored time is UTC.
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
    }

    private sealed class UtcDateTimeConverter()
        : ValueConverter<DateTime, DateTime>(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    private sealed class NullableUtcDateTimeConverter()
        : ValueConverter<DateTime?, DateTime?>(v => v, v => v == null ? v : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Identity's own configuration must run first, then our entity configurations.
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TrackContext).Assembly);
    }
}
