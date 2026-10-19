using Microsoft.EntityFrameworkCore;
using Themis.Kernel.Persistence.Entities;
using Themis.Kernel.Persistence.Entities.Documents;
using Themis.Kernel.Persistence.Entities.Identity;
using Themis.Kernel.Persistence.Entities.MasterData;
using Themis.Kernel.Persistence.Entities.Organisation;
using Themis.Kernel.Persistence.Entities.Posting;

namespace Themis.Kernel.Persistence;

/// <summary>
/// The one data context of schema "kernel" (D-26): every kernel table maps here, snake_case names as the spec's
/// entity names, migrations history in the kernel schema. Used on the command's unit of work with the role
/// interceptor at runtime, and by Themis.Migrator with the themis_migrator connection for migrations.
/// </summary>
internal sealed class KernelDbContext : DbContext
{
    public const string Schema = "kernel";
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public KernelDbContext(DbContextOptions<KernelDbContext> options)
        : base(options)
    {
    }

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();

    public DbSet<Installation> Installations => Set<Installation>();

    public DbSet<Party> Parties => Set<Party>();

    public DbSet<ChartOfAccounts> ChartsOfAccounts => Set<ChartOfAccounts>();

    public DbSet<Company> Companies => Set<Company>();

    public DbSet<Site> Sites => Set<Site>();

    public DbSet<Warehouse> Warehouses => Set<Warehouse>();

    public DbSet<FiscalYear> FiscalYears => Set<FiscalYear>();

    public DbSet<FiscalPeriod> FiscalPeriods => Set<FiscalPeriod>();

    public DbSet<UserAccount> Users => Set<UserAccount>();

    public DbSet<Permission> Permissions => Set<Permission>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    public DbSet<RoleAssignment> RoleAssignments => Set<RoleAssignment>();

    public DbSet<SodConflict> SodConflicts => Set<SodConflict>();

    public DbSet<UnitOfMeasure> UnitsOfMeasure => Set<UnitOfMeasure>();

    public DbSet<Currency> Currencies => Set<Currency>();

    public DbSet<ItemPostingGroup> ItemPostingGroups => Set<ItemPostingGroup>();

    public DbSet<Item> Items => Set<Item>();

    public DbSet<ItemCompany> ItemCompanies => Set<ItemCompany>();

    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<PostingProfileRule> PostingProfileRules => Set<PostingProfileRule>();

    public DbSet<DocumentType> DocumentTypes => Set<DocumentType>();

    public DbSet<DocumentTransition> DocumentTransitions => Set<DocumentTransition>();

    public DbSet<Document> Documents => Set<Document>();

    public DbSet<DocumentLine> DocumentLines => Set<DocumentLine>();

    public DbSet<InventoryAdjustment> InventoryAdjustments => Set<InventoryAdjustment>();

    public DbSet<NumberSeries> NumberSeries => Set<NumberSeries>();

    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();

    public DbSet<JournalLine> JournalLines => Set<JournalLine>();

    public DbSet<InventoryEntry> InventoryEntries => Set<InventoryEntry>();

    public DbSet<ItemCostState> ItemCostStates => Set<ItemCostState>();

    public DbSet<StockBalance> StockBalances => Set<StockBalance>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        OrganisationModel.Configure(modelBuilder);
        IdentityModel.Configure(modelBuilder);
        MasterDataModel.Configure(modelBuilder);
        GeneralLedgerModel.Configure(modelBuilder);
        DocumentModel.Configure(modelBuilder);
        LedgerModel.Configure(modelBuilder);

        modelBuilder.Entity<IdempotencyRecord>(entity =>
        {
            entity.ToTable("idempotency_record");
            entity.HasKey(e => new { e.UserId, e.Key }).HasName("pk_idempotency_record");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.Key).HasColumnName("key").HasMaxLength(128);
            entity.Property(e => e.CommandName).HasColumnName("command_name").HasMaxLength(81);
            entity.Property(e => e.RequestHash).HasColumnName("request_hash").HasMaxLength(64);
            entity.Property(e => e.Response).HasColumnName("response").HasColumnType("jsonb");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at");
            entity.HasIndex(e => e.ExpiresAt).HasDatabaseName("ix_idempotency_record_expires_at");
        });

        modelBuilder.Entity<AuditEvent>(entity =>
        {
            entity.ToTable("audit_event");
            entity.HasKey(e => e.Id).HasName("pk_audit_event");
            entity.Property(e => e.Id).HasColumnName("id");
            // D-51: required from step 8 on by a NOT VALID check added in raw SQL (the posting migration, RR-39), so earlier rows stay as written.
            entity.Property(e => e.CommandId).HasColumnName("command_id");
            entity.Property(e => e.OccurredAt).HasColumnName("occurred_at");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.CommandName).HasColumnName("command_name").HasMaxLength(81);
            entity.Property(e => e.EntityType).HasColumnName("entity_type").HasMaxLength(40);
            entity.Property(e => e.EntityId).HasColumnName("entity_id");
            entity.Property(e => e.Changes).HasColumnName("changes").HasColumnType("jsonb");
            entity.Property(e => e.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(128);
            entity.Property(e => e.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128);
            entity.HasIndex(e => new { e.EntityType, e.EntityId }).HasDatabaseName("ix_audit_event_entity");
        });

        modelBuilder.Entity<OutboxEvent>(entity =>
        {
            entity.ToTable("outbox_event");
            entity.HasKey(e => e.Id).HasName("pk_outbox_event");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.EventType).HasColumnName("event_type").HasMaxLength(81);
            entity.Property(e => e.SchemaVersion).HasColumnName("schema_version");
            entity.Property(e => e.AggregateType).HasColumnName("aggregate_type").HasMaxLength(40);
            entity.Property(e => e.AggregateId).HasColumnName("aggregate_id");
            entity.Property(e => e.CompanyId).HasColumnName("company_id");
            entity.Property(e => e.CommandName).HasColumnName("command_name").HasMaxLength(81);
            entity.Property(e => e.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128);
            entity.Property(e => e.Payload).HasColumnName("payload").HasColumnType("jsonb");
            // D-50: the company is empty only for installation-scope aggregates; a company named must exist.
            entity.HasOne<Company>().WithMany().HasForeignKey(e => e.CompanyId).HasConstraintName("fk_outbox_event_company").OnDelete(DeleteBehavior.Restrict);
            entity.Property(e => e.OccurredAt).HasColumnName("occurred_at");
            entity.Property(e => e.PublishedAt).HasColumnName("published_at");
            entity.Property(e => e.Attempts).HasColumnName("attempts").HasDefaultValue(0);
            entity.HasIndex(e => e.OccurredAt).HasDatabaseName("ix_outbox_event_unpublished").HasFilter("published_at IS NULL");
        });
    }
}
