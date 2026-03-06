using HERM_MAPPER_APP.Models;
using Microsoft.EntityFrameworkCore;

namespace HERM_MAPPER_APP.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<TrmDomain> TrmDomains => Set<TrmDomain>();
    public DbSet<TrmCapability> TrmCapabilities => Set<TrmCapability>();
    public DbSet<TrmComponent> TrmComponents => Set<TrmComponent>();
    public DbSet<TrmComponentCapabilityLink> TrmComponentCapabilityLinks => Set<TrmComponentCapabilityLink>();
    public DbSet<TrmComponentVersion> TrmComponentVersions => Set<TrmComponentVersion>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();
    public DbSet<ProductCatalogItem> ProductCatalogItems => Set<ProductCatalogItem>();
    public DbSet<ProductMapping> ProductMappings => Set<ProductMapping>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<TrmDomain>(entity =>
        {
            entity.HasIndex(x => x.Code).IsUnique();
            entity.Property(x => x.Code).HasMaxLength(16);
            entity.Property(x => x.Name).HasMaxLength(200);
        });

        modelBuilder.Entity<TrmCapability>(entity =>
        {
            entity.HasIndex(x => x.Code).IsUnique();
            entity.Property(x => x.Code).HasMaxLength(16);
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.HasOne(x => x.ParentDomain)
                .WithMany(x => x.Capabilities)
                .HasForeignKey(x => x.ParentDomainId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TrmComponent>(entity =>
        {
            entity.HasIndex(x => x.Code).IsUnique();
            entity.Property(x => x.Code).HasMaxLength(32);
            entity.Property(x => x.TechnologyComponentCode).HasMaxLength(32);
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.HasOne(x => x.ParentCapability)
                .WithMany(x => x.Components)
                .HasForeignKey(x => x.ParentCapabilityId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TrmComponentCapabilityLink>(entity =>
        {
            entity.HasIndex(x => new { x.TrmComponentId, x.TrmCapabilityId }).IsUnique();
            entity.HasOne(x => x.TrmComponent)
                .WithMany(x => x.CapabilityLinks)
                .HasForeignKey(x => x.TrmComponentId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.TrmCapability)
                .WithMany(x => x.ComponentLinks)
                .HasForeignKey(x => x.TrmCapabilityId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TrmComponentVersion>(entity =>
        {
            entity.HasIndex(x => new { x.TrmComponentId, x.VersionNumber }).IsUnique();
            entity.Property(x => x.ChangeType).HasMaxLength(40);
            entity.Property(x => x.ModelCode).HasMaxLength(32);
            entity.Property(x => x.TechnologyComponentCode).HasMaxLength(32);
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.HasOne(x => x.TrmComponent)
                .WithMany(x => x.Versions)
                .HasForeignKey(x => x.TrmComponentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AuditLogEntry>(entity =>
        {
            entity.HasIndex(x => x.OccurredUtc);
            entity.Property(x => x.Category).HasMaxLength(80);
            entity.Property(x => x.Action).HasMaxLength(80);
            entity.Property(x => x.EntityType).HasMaxLength(80);
            entity.Property(x => x.Summary).HasMaxLength(400);
        });

        modelBuilder.Entity<ProductCatalogItem>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.Property(x => x.Vendor).HasMaxLength(120);
            entity.Property(x => x.Version).HasMaxLength(80);
            entity.Property(x => x.Owner).HasMaxLength(120);
            entity.Property(x => x.LifecycleStatus).HasMaxLength(80);
        });

        modelBuilder.Entity<ProductMapping>(entity =>
        {
            entity.HasOne(x => x.ProductCatalogItem)
                .WithMany(x => x.Mappings)
                .HasForeignKey(x => x.ProductCatalogItemId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.TrmDomain)
                .WithMany()
                .HasForeignKey(x => x.TrmDomainId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(x => x.TrmCapability)
                .WithMany()
                .HasForeignKey(x => x.TrmCapabilityId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(x => x.TrmComponent)
                .WithMany()
                .HasForeignKey(x => x.TrmComponentId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
