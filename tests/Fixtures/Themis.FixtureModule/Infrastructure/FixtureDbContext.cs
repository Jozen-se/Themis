using Microsoft.EntityFrameworkCore;
using Themis.FixtureModule.Application;
using Themis.FixtureModule.Contracts;
using Themis.FixtureModule.Domain;
using Themis.Kernel.Contracts.Persistence;

namespace Themis.FixtureModule.Infrastructure;

/// <summary>
/// An Entity Framework data context over the fixture counter table, enlisted in the command's connection and
/// transaction, so that a test can produce a real database error through SaveChanges, wrapped the way Entity
/// Framework wraps it (RR-40: a DbUpdateException around the PostgresException).
/// </summary>
internal sealed class FixtureDbContext : DbContext
{
    public FixtureDbContext(DbContextOptions<FixtureDbContext> options)
        : base(options)
    {
    }

    public DbSet<CounterRow> Counters => Set<CounterRow>();

    public static FixtureDbContext OnUnitOfWork(IUnitOfWork unitOfWork)
    {
        DbContextOptions<FixtureDbContext> options = new DbContextOptionsBuilder<FixtureDbContext>()
            .UseNpgsql(unitOfWork.Connection)
            .Options;
        FixtureDbContext context = new(options);
        context.Database.UseTransaction(unitOfWork.Transaction);
        return context;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CounterRow>(entity =>
        {
            entity.ToTable("counter", FixtureContracts.SchemaName);
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CompanyId).HasColumnName("company_id");
            entity.Property(e => e.SiteId).HasColumnName("site_id");
            entity.Property(e => e.Value).HasColumnName("value");
            entity.Property(e => e.Version).HasColumnName("version");
            entity.Property(e => e.Sample).HasColumnName("sample");
            entity.Property(e => e.Posted).HasColumnName("posted");
        });
    }

    /// <summary>The counter row as Entity Framework tracks it (ARCH-07: internal, like every type outside Contracts).</summary>
    internal sealed class CounterRow
    {
        public Guid Id { get; set; }

        public Guid CompanyId { get; set; }

        public Guid? SiteId { get; set; }

        public long Value { get; set; }

        public long Version { get; set; }

        public decimal Sample { get; set; }

        public bool Posted { get; set; }

        public Counter ToCounter() => new(Id, CompanyId, SiteId, Value, Version, Sample, Posted);
    }
}

/// <summary>
/// The tracked counter store over <see cref="FixtureDbContext"/>: one context per touch on the command's connection
/// and transaction. Before each statement the unit of work is switched to themis_fixture, as the raw SQL store does
/// (R-DB-01, D-41); the deadlock a test provokes (RR-40) surfaces from SaveChangesAsync as a DbUpdateException.
/// </summary>
internal sealed class EntityFrameworkCounterStore : ITrackedCounterStore
{
    private static readonly DatabaseRole Role = DatabaseRole.From(FixtureContracts.RoleName);

    public async Task<CounterChange?> TouchAsync(IUnitOfWork unitOfWork, Guid id, long increment, CancellationToken cancellationToken)
    {
        await using FixtureDbContext data = FixtureDbContext.OnUnitOfWork(unitOfWork);
        await unitOfWork.EnsureRoleAsync(Role, cancellationToken);
        FixtureDbContext.CounterRow? row = await data.Counters.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (row is null)
        {
            return null;
        }

        Counter before = row.ToCounter();
        row.Value += increment;
        row.Version += 1;
        await unitOfWork.EnsureRoleAsync(Role, cancellationToken);
        await data.SaveChangesAsync(cancellationToken);
        return new CounterChange(before, row.ToCounter());
    }
}
