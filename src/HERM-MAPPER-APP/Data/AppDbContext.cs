using HERMMapperApp.Models;
using Microsoft.EntityFrameworkCore;

namespace HERMMapperApp.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<TrmDomain> TrmDomains => Set<TrmDomain>();
    public DbSet<TrmCapability> TrmCapabilities => Set<TrmCapability>();
    public DbSet<TrmComponent> TrmComponents => Set<TrmComponent>();
    public DbSet<TrmComponentCapabilityLink> TrmComponentCapabilityLinks => Set<TrmComponentCapabilityLink>();
    public DbSet<TrmComponentVersion> TrmComponentVersions => Set<TrmComponentVersion>();
    public DbSet<ArmDomain> ArmDomains => Set<ArmDomain>();
    public DbSet<ArmCapability> ArmCapabilities => Set<ArmCapability>();
    public DbSet<ArmComponent> ArmComponents => Set<ArmComponent>();
    public DbSet<ArmComponentCapabilityLink> ArmComponentCapabilityLinks => Set<ArmComponentCapabilityLink>();
    public DbSet<BrmModel> BrmModels => Set<BrmModel>();
    public DbSet<BrmDomain> BrmDomains => Set<BrmDomain>();
    public DbSet<BrmCapability> BrmCapabilities => Set<BrmCapability>();
    public DbSet<BrmComponent> BrmComponents => Set<BrmComponent>();
    public DbSet<DrmTopicType> DrmTopicTypes => Set<DrmTopicType>();
    public DbSet<DrmTopic> DrmTopics => Set<DrmTopic>();
    public DbSet<DrmEntity> DrmEntities => Set<DrmEntity>();
    public DbSet<DrmCommonSubClass> DrmCommonSubClasses => Set<DrmCommonSubClass>();
    public DbSet<DrmModel> DrmModels => Set<DrmModel>();
    public DbSet<DrmModelDataEntity> DrmModelDataEntities => Set<DrmModelDataEntity>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<AiProviderConfiguration> AiProviderConfigurations => Set<AiProviderConfiguration>();
    public DbSet<AiRequestUsageLog> AiRequestUsageLogs => Set<AiRequestUsageLog>();
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<ConfigurableFieldOption> ConfigurableFieldOptions => Set<ConfigurableFieldOption>();
    public DbSet<ProductCatalogItem> ProductCatalogItems => Set<ProductCatalogItem>();
    public DbSet<ProductCatalogItemOwner> ProductCatalogItemOwners => Set<ProductCatalogItemOwner>();
    public DbSet<ProductMapping> ProductMappings => Set<ProductMapping>();
    public DbSet<ServiceCatalogItem> ServiceCatalogItems => Set<ServiceCatalogItem>();
    public DbSet<ServiceCatalogItemProduct> ServiceCatalogItemProducts => Set<ServiceCatalogItemProduct>();
    public DbSet<ServiceCatalogItemConnection> ServiceCatalogItemConnections => Set<ServiceCatalogItemConnection>();
    public DbSet<ApplicationCatalogItem> ApplicationCatalogItems => Set<ApplicationCatalogItem>();
    public DbSet<ApplicationCatalogItemMapping> ApplicationCatalogItemMappings => Set<ApplicationCatalogItemMapping>();
    public DbSet<BusinessCapabilityCatalogItem> BusinessCapabilityCatalogItems => Set<BusinessCapabilityCatalogItem>();
    public DbSet<BusinessCapabilityCatalogItemMapping> BusinessCapabilityCatalogItemMappings => Set<BusinessCapabilityCatalogItemMapping>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<TrmDomain>(entity =>
        {
            entity.ToTable("TrmDomains");
            entity.HasIndex(x => x.Code).IsUnique();
            entity.Property(x => x.Code).HasMaxLength(16);
            entity.Property(x => x.Name).HasMaxLength(200);
        });

        modelBuilder.Entity<TrmCapability>(entity =>
        {
            entity.ToTable("TrmCapabilities");
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
            entity.ToTable("TrmComponents");
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
            entity.ToTable("TrmComponentCapabilityLinks");
            entity.HasIndex(x => new { x.TrmComponentId, x.TrmCapabilityId }).IsUnique();
            entity.HasOne(x => x.TrmComponent)
                .WithMany(x => x.CapabilityLinks)
                .HasForeignKey(x => x.TrmComponentId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.TrmCapability)
                .WithMany(x => x.ComponentLinks)
                .HasForeignKey(x => x.TrmCapabilityId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TrmComponentVersion>(entity =>
        {
            entity.ToTable("TrmComponentVersions");
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

        modelBuilder.Entity<ArmDomain>(entity =>
        {
            entity.ToTable("ArmDomains");
            entity.HasIndex(x => x.Code).IsUnique();
            entity.Property(x => x.Code).HasMaxLength(16);
            entity.Property(x => x.Name).HasMaxLength(200);
        });

        modelBuilder.Entity<ArmCapability>(entity =>
        {
            entity.ToTable("ArmCapabilities");
            entity.HasIndex(x => x.Code).IsUnique();
            entity.Property(x => x.Code).HasMaxLength(16);
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.HasOne(x => x.ParentDomain)
                .WithMany(x => x.Capabilities)
                .HasForeignKey(x => x.ParentDomainId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ArmComponent>(entity =>
        {
            entity.ToTable("ArmComponents");
            entity.HasIndex(x => x.Code).IsUnique();
            entity.Property(x => x.Code).HasMaxLength(32);
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.Property(x => x.DeletedReason).HasMaxLength(400);
            entity.HasOne(x => x.ParentCapability)
                .WithMany(x => x.Components)
                .HasForeignKey(x => x.ParentCapabilityId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ArmComponentCapabilityLink>(entity =>
        {
            entity.ToTable("ArmComponentCapabilityLinks");
            entity.HasIndex(x => new { x.ArmComponentId, x.ArmCapabilityId }).IsUnique();
            entity.HasOne(x => x.ArmComponent)
                .WithMany(x => x.CapabilityLinks)
                .HasForeignKey(x => x.ArmComponentId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.ArmCapability)
                .WithMany(x => x.ComponentLinks)
                .HasForeignKey(x => x.ArmCapabilityId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BrmModel>(entity =>
        {
            entity.ToTable("BrmModels");
            entity.HasIndex(x => x.Name);
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.Property(x => x.Area).HasMaxLength(120);
            entity.Property(x => x.Description).HasMaxLength(2000);
            entity.Property(x => x.Status).HasMaxLength(80);
            entity.Property(x => x.DeletedReason).HasMaxLength(400);
        });

        modelBuilder.Entity<BrmDomain>(entity =>
        {
            entity.ToTable("BrmDomains");
            entity.HasIndex(x => x.Code).IsUnique();
            entity.Property(x => x.Code).HasMaxLength(16);
            entity.Property(x => x.Name).HasMaxLength(200);
        });

        modelBuilder.Entity<BrmCapability>(entity =>
        {
            entity.ToTable("BrmCapabilities");
            entity.HasIndex(x => x.Code).IsUnique();
            entity.Property(x => x.Code).HasMaxLength(16);
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.HasOne(x => x.ParentDomain)
                .WithMany(x => x.Capabilities)
                .HasForeignKey(x => x.ParentDomainId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BrmComponent>(entity =>
        {
            entity.ToTable("BrmComponents");
            entity.HasIndex(x => x.Code).IsUnique();
            entity.Property(x => x.Code).HasMaxLength(32);
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.Property(x => x.DeletedReason).HasMaxLength(400);
            entity.HasOne(x => x.ParentCapability)
                .WithMany(x => x.Components)
                .HasForeignKey(x => x.ParentCapabilityId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DrmTopicType>(entity =>
        {
            entity.ToTable("DrmTopicTypes");
            entity.HasIndex(x => x.Code).IsUnique();
            entity.Property(x => x.Code).HasMaxLength(16);
            entity.Property(x => x.Name).HasMaxLength(200);
        });

        modelBuilder.Entity<DrmTopic>(entity =>
        {
            entity.ToTable("DrmTopics");
            entity.HasIndex(x => x.Code).IsUnique();
            entity.Property(x => x.Code).HasMaxLength(16);
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.Property(x => x.TopicTypeCode).HasMaxLength(16);
            entity.Property(x => x.TopicTypeName).HasMaxLength(200);
            entity.HasOne(x => x.TopicType)
                .WithMany(x => x.Topics)
                .HasForeignKey(x => x.TopicTypeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DrmEntity>(entity =>
        {
            entity.ToTable("DrmEntities");
            entity.HasIndex(x => x.Code).IsUnique();
            entity.Property(x => x.Code).HasMaxLength(32);
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.Property(x => x.ParentTopicCode).HasMaxLength(16);
            entity.Property(x => x.ParentTopicTypeName).HasMaxLength(200);
            entity.Property(x => x.DeletedReason).HasMaxLength(400);
            entity.HasOne(x => x.ParentTopic)
                .WithMany(x => x.Entities)
                .HasForeignKey(x => x.ParentTopicId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DrmCommonSubClass>(entity =>
        {
            entity.ToTable("DrmCommonSubClasses");
            entity.HasIndex(x => x.Code).IsUnique();
            entity.Property(x => x.Code).HasMaxLength(32);
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.Property(x => x.ParentEntityCode).HasMaxLength(32);
            entity.Property(x => x.DeletedReason).HasMaxLength(400);
            entity.HasOne(x => x.ParentEntity)
                .WithMany(x => x.CommonSubClasses)
                .HasForeignKey(x => x.ParentEntityId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DrmModel>(entity =>
        {
            entity.ToTable("DrmModels");
            entity.HasIndex(x => x.Name);
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.Property(x => x.Area).HasMaxLength(120);
            entity.Property(x => x.Description).HasMaxLength(2000);
            entity.Property(x => x.Status).HasMaxLength(80);
            entity.Property(x => x.DeletedReason).HasMaxLength(400);
        });

        modelBuilder.Entity<DrmModelDataEntity>(entity =>
        {
            entity.ToTable("DrmModelDataEntities");
            entity.HasIndex(x => x.DrmModelId);
            entity.HasIndex(x => new { x.DrmModelId, x.DrmEntityId, x.DrmCommonSubClassId }).IsUnique();
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.Property(x => x.Description).HasMaxLength(2000);
            entity.Property(x => x.Notes).HasMaxLength(4000);
            entity.HasOne(x => x.DrmModel)
                .WithMany(x => x.DataEntities)
                .HasForeignKey(x => x.DrmModelId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.DrmEntity)
                .WithMany(x => x.ModelDataEntities)
                .HasForeignKey(x => x.DrmEntityId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.DrmCommonSubClass)
                .WithMany(x => x.ModelDataEntities)
                .HasForeignKey(x => x.DrmCommonSubClassId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<AuditLogEntry>(entity =>
        {
            entity.HasIndex(x => x.OccurredUtc);
            entity.Property(x => x.Category).HasMaxLength(80);
            entity.Property(x => x.Action).HasMaxLength(80);
            entity.Property(x => x.EntityType).HasMaxLength(80);
            entity.Property(x => x.ActorUserName).HasMaxLength(200);
            entity.Property(x => x.Summary).HasMaxLength(400);
        });

        modelBuilder.Entity<AppSetting>(entity =>
        {
            entity.HasIndex(x => x.Key).IsUnique();
            entity.Property(x => x.Key).HasMaxLength(100);
            entity.Property(x => x.Value).HasMaxLength(4000);
        });

        modelBuilder.Entity<AiProviderConfiguration>(entity =>
        {
            entity.ToTable("AiProviderConfigurations");
            entity.HasIndex(x => x.Name);
            entity.HasIndex(x => x.IsActive);
            entity.Property(x => x.Name).HasMaxLength(120);
            entity.Property(x => x.Endpoint).HasMaxLength(2048);
            entity.Property(x => x.Model).HasMaxLength(200);
            entity.Property(x => x.ApiVersion).HasMaxLength(80);
            entity.Property(x => x.SystemPrompt);
            entity.Property(x => x.PromptTemplate);
            entity.Property(x => x.InputCostPerMillionTokensSek).HasPrecision(18, 6);
            entity.Property(x => x.OutputCostPerMillionTokensSek).HasPrecision(18, 6);
        });

        modelBuilder.Entity<AiRequestUsageLog>(entity =>
        {
            entity.ToTable("AiRequestUsageLogs");
            entity.HasIndex(x => x.OccurredUtc);
            entity.HasIndex(x => new { x.AiProviderConfigurationId, x.OccurredUtc });
            entity.Property(x => x.ProviderName).HasMaxLength(120);
            entity.Property(x => x.Model).HasMaxLength(200);
            entity.Property(x => x.RequestKind).HasMaxLength(80);
            entity.Property(x => x.RequestSummary).HasMaxLength(400);
            entity.Property(x => x.EstimatedInputCostSek).HasPrecision(18, 6);
            entity.Property(x => x.EstimatedOutputCostSek).HasPrecision(18, 6);
            entity.Property(x => x.EstimatedTotalCostSek).HasPrecision(18, 6);
            entity.Property(x => x.ErrorMessage).HasMaxLength(2000);
            entity.HasOne(x => x.AiProviderConfiguration)
                .WithMany(x => x.UsageLogs)
                .HasForeignKey(x => x.AiProviderConfigurationId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.HasIndex(x => x.Email).IsUnique();
            entity.HasIndex(x => x.UserName).IsUnique();
            entity.Property(x => x.GivenName).HasMaxLength(100);
            entity.Property(x => x.LastName).HasMaxLength(100);
            entity.Property(x => x.Email).HasMaxLength(200);
            entity.Property(x => x.UserName).HasMaxLength(100);
            entity.Property(x => x.PasswordHash).HasMaxLength(400);
            entity.Property(x => x.RoleName).HasMaxLength(40);
            entity.Property(x => x.FailedLoginCount).HasDefaultValue(0);
        });

        modelBuilder.Entity<ConfigurableFieldOption>(entity =>
        {
            entity.HasIndex(x => new { x.FieldName, x.Value }).IsUnique();
            entity.Property(x => x.FieldName).HasMaxLength(80);
            entity.Property(x => x.Value).HasMaxLength(120);
            entity.Property(x => x.SortOrder);
        });

        modelBuilder.Entity<ProductCatalogItem>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.Property(x => x.Vendor).HasMaxLength(120);
            entity.Property(x => x.Version).HasMaxLength(80);
            entity.Property(x => x.LifecycleStatus).HasMaxLength(80);
            entity.Property(x => x.DeletedReason).HasMaxLength(400);
        });

        modelBuilder.Entity<ProductCatalogItemOwner>(entity =>
        {
            entity.Property(x => x.OwnerValue).HasMaxLength(120);
            entity.HasIndex(x => x.OwnerValue);
            entity.HasIndex(x => new { x.ProductCatalogItemId, x.OwnerValue }).IsUnique();
            entity.HasOne(x => x.ProductCatalogItem)
                .WithMany(x => x.Owners)
                .HasForeignKey(x => x.ProductCatalogItemId)
                .OnDelete(DeleteBehavior.Cascade);
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
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(x => x.TrmCapability)
                .WithMany()
                .HasForeignKey(x => x.TrmCapabilityId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(x => x.TrmComponent)
                .WithMany()
                .HasForeignKey(x => x.TrmComponentId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<ServiceCatalogItem>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.Property(x => x.Description).HasMaxLength(2000);
            entity.Property(x => x.Owner).HasMaxLength(120);
            entity.Property(x => x.LifecycleStatus).HasMaxLength(80);
            entity.Property(x => x.AssetCriticalityScore).HasDefaultValue(1);
            entity.Property(x => x.DeletedReason).HasMaxLength(400);
        });

        modelBuilder.Entity<ServiceCatalogItemProduct>(entity =>
        {
            entity.HasIndex(x => new { x.ServiceCatalogItemId, x.SortOrder }).IsUnique();
            entity.HasIndex(x => x.ProductCatalogItemId);

            entity.HasOne(x => x.ServiceCatalogItem)
                .WithMany(x => x.ProductLinks)
                .HasForeignKey(x => x.ServiceCatalogItemId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.ProductCatalogItem)
                .WithMany()
                .HasForeignKey(x => x.ProductCatalogItemId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ServiceCatalogItemConnection>(entity =>
        {
            entity.HasIndex(x => new { x.ServiceCatalogItemId, x.SortOrder }).IsUnique();
            entity.HasIndex(x => new { x.ServiceCatalogItemId, x.FromProductCatalogItemId, x.ToProductCatalogItemId }).IsUnique();
            entity.HasIndex(x => x.FromProductCatalogItemId);
            entity.HasIndex(x => x.ToProductCatalogItemId);

            entity.HasOne(x => x.ServiceCatalogItem)
                .WithMany(x => x.ProductConnections)
                .HasForeignKey(x => x.ServiceCatalogItemId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.FromProductCatalogItem)
                .WithMany()
                .HasForeignKey(x => x.FromProductCatalogItemId)
                    .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(x => x.ToProductCatalogItem)
                .WithMany()
                .HasForeignKey(x => x.ToProductCatalogItemId)
                    .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<ApplicationCatalogItem>(entity =>
        {
            entity.ToTable("ApplicationCatalogItems");
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.Property(x => x.Description).HasMaxLength(2000);
            entity.Property(x => x.Notes).HasMaxLength(4000);
            entity.Property(x => x.DeletedReason).HasMaxLength(400);
        });

        modelBuilder.Entity<ApplicationCatalogItemMapping>(entity =>
        {
            entity.ToTable("ApplicationCatalogItemMappings");
            entity.HasIndex(x => new { x.ApplicationCatalogItemId, x.ArmComponentId, x.ProductMappingId }).IsUnique();
            entity.Property(x => x.Notes).HasMaxLength(1000);

            entity.HasOne(x => x.ApplicationCatalogItem)
                .WithMany(x => x.Mappings)
                .HasForeignKey(x => x.ApplicationCatalogItemId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.ArmComponent)
                .WithMany()
                .HasForeignKey(x => x.ArmComponentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.ProductMapping)
                .WithMany()
                .HasForeignKey(x => x.ProductMappingId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(x => x.ProductCatalogItem)
                .WithMany()
                .HasForeignKey(x => x.ProductCatalogItemId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BusinessCapabilityCatalogItem>(entity =>
        {
            entity.ToTable("BusinessCapabilityCatalogItems");
            entity.HasIndex(x => x.BrmModelId);
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.Property(x => x.Description).HasMaxLength(2000);
            entity.Property(x => x.Notes).HasMaxLength(4000);
            entity.HasOne(x => x.BrmModel)
                .WithMany(x => x.Capabilities)
                .HasForeignKey(x => x.BrmModelId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<BusinessCapabilityCatalogItemMapping>(entity =>
        {
            entity.ToTable("BusinessCapabilityCatalogItemMappings");
            entity.HasIndex(x => new { x.BusinessCapabilityCatalogItemId, x.BrmComponentId, x.ArmComponentId, x.ArmCapabilityId }).IsUnique();
            entity.Property(x => x.Notes).HasMaxLength(1000);

            entity.HasOne(x => x.BusinessCapabilityCatalogItem)
                .WithMany(x => x.Mappings)
                .HasForeignKey(x => x.BusinessCapabilityCatalogItemId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.BrmComponent)
                .WithMany()
                .HasForeignKey(x => x.BrmComponentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.ArmComponent)
                .WithMany()
                .HasForeignKey(x => x.ArmComponentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.ArmCapability)
                .WithMany()
                .HasForeignKey(x => x.ArmCapabilityId)
                .OnDelete(DeleteBehavior.NoAction);
        });
    }
}
