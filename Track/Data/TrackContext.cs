using Track.Data.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Track.Data;

public class TrackContext(DbContextOptions<TrackContext> options)
    : IdentityDbContext<User, Role, Guid>(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<StackAssignment> StackAssignments => Set<StackAssignment>();
    public DbSet<BucketAssignment> BucketAssignments => Set<BucketAssignment>();
    public DbSet<ContainerSample> ContainerSamples => Set<ContainerSample>();
    public DbSet<DiskSample> DiskSamples => Set<DiskSample>();
    public DbSet<BillingSample> BillingSamples => Set<BillingSample>();
    public DbSet<ClusterCapacitySample> ClusterCapacitySamples => Set<ClusterCapacitySample>();
    public DbSet<IdentityEnvVar> IdentityEnvVars => Set<IdentityEnvVar>();
    public DbSet<S3Source> S3Sources => Set<S3Source>();
    public DbSet<BillingRate> BillingRates => Set<BillingRate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Identity's own configuration must run first, then our entity configurations.
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TrackContext).Assembly);
    }
}
