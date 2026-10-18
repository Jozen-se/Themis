using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Contracts.Posting;
using Themis.Kernel.Persistence.Entities;
using Themis.Kernel.Persistence.Entities.Organisation;
using Themis.Kernel.Persistence.Entities.Posting;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Persistence.Seeding;

/// <summary>
/// Writes a <see cref="GeneralLedgerFixtureDefinition"/> as themis_migrator, in one transaction, idempotently, after
/// the caller's rule check has passed (account in the company's chart, Active, R-PP-04, R-PP-06; the database
/// constraints are the second line of defence). Accounts and posting profile rules are company configuration, so
/// they come with the test and development fixture only, never with a migration (step 6 brief). Every row inserted
/// gets one audit event under the system user (R-AUD-01); no outbox event, because no command ran.
/// </summary>
internal static class GeneralLedgerFixtureSeeder
{
    private static readonly JsonSerializerOptions ChangesJson = new(JsonSerializerDefaults.Web);

    public static async Task<Result<int>> ApplyAsync(string migratorConnectionString, GeneralLedgerFixtureDefinition fixture, Func<GeneralLedgerFixtureDefinition, Result> ruleCheck, DateTimeOffset now, UserId seededBy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentNullException.ThrowIfNull(ruleCheck);
        Result check = ruleCheck(fixture);
        if (check.IsFailure)
        {
            return Result<int>.Failure(check.Errors);
        }

        await using KernelDbContext context = KernelDbContextFactory.ForMigrations(migratorConnectionString);
        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        Guid by = seededBy.Value;
        // D-51: one command id per seeder call, shared by every audit event it writes.
        Guid commandId = EntityId.New();
        int inserted = 0;

        foreach (AccountDefinition account in fixture.Accounts)
        {
            inserted += await AddIfMissingAsync(context, commandId, "Account", account.Id, now, by, () => new Account
            {
                Id = account.Id,
                ChartId = account.ChartId,
                AccountNumber = account.AccountNumber.Value,
                Name = account.Name,
                AccountType = account.AccountType.ToString(),
                IsControl = account.IsControl,
                Status = account.Status.ToString(),
                CreatedAt = now,
                CreatedBy = by,
                UpdatedAt = now,
                UpdatedBy = by,
            }, new Dictionary<string, FieldChange>(StringComparer.Ordinal)
            {
                ["chartId"] = new(null, account.ChartId.ToString()),
                ["accountNumber"] = new(null, account.AccountNumber.Value),
                ["name"] = new(null, account.Name),
                ["accountType"] = new(null, account.AccountType.ToString()),
                ["isControl"] = new(null, account.IsControl ? "True" : "False"),
                ["status"] = new(null, account.Status.ToString()),
            }, cancellationToken);
        }

        foreach (PostingProfileRuleDefinition rule in fixture.Rules)
        {
            inserted += await AddIfMissingAsync(context, commandId, "PostingProfileRule", rule.Id, now, by, () => new PostingProfileRule
            {
                Id = rule.Id,
                CompanyId = rule.CompanyId,
                AccountRole = rule.AccountRole,
                ItemPostingGroup = rule.ItemPostingGroup?.Value,
                PartyPostingGroup = rule.PartyPostingGroup?.ToString(),
                ShipToScope = rule.ShipToScope?.ToString(),
                TaxCodeId = rule.TaxCodeId,
                ReasonCode = rule.ReasonCode?.ToString(),
                AccountId = rule.AccountId,
                ValidFrom = rule.ValidFrom.Value,
                ValidTo = rule.ValidTo?.Value,
                CreatedAt = now,
                CreatedBy = by,
                UpdatedAt = now,
                UpdatedBy = by,
            }, new Dictionary<string, FieldChange>(StringComparer.Ordinal)
            {
                ["companyId"] = new(null, rule.CompanyId.ToString()),
                ["accountRole"] = new(null, rule.AccountRole),
                ["itemPostingGroup"] = new(null, rule.ItemPostingGroup?.Value),
                ["partyPostingGroup"] = new(null, rule.PartyPostingGroup?.ToString()),
                ["shipToScope"] = new(null, rule.ShipToScope?.ToString()),
                ["taxCodeId"] = new(null, rule.TaxCodeId?.ToString()),
                ["reasonCode"] = new(null, rule.ReasonCode?.ToString()),
                ["accountId"] = new(null, rule.AccountId.ToString()),
                ["validFrom"] = new(null, rule.ValidFrom.ToString()),
                ["validTo"] = new(null, rule.ValidTo?.ToString()),
            }, cancellationToken);
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return inserted;
    }

    private static async Task<int> AddIfMissingAsync<T>(KernelDbContext context, Guid commandId, string entityType, Guid id, DateTimeOffset now, Guid by, Func<T> create, IReadOnlyDictionary<string, FieldChange> changes, CancellationToken cancellationToken)
        where T : MasterDataRow
    {
        if (await context.Set<T>().AnyAsync(row => row.Id == id, cancellationToken))
        {
            return 0;
        }

        context.Set<T>().Add(create());
        context.AuditEvents.Add(new AuditEvent
        {
            Id = EntityId.New(),
            CommandId = commandId,
            OccurredAt = now,
            UserId = by,
            CommandName = OrganisationFixtureSeeder.CommandName,
            EntityType = entityType,
            EntityId = id,
            Changes = JsonSerializer.Serialize(changes, ChangesJson),
        });
        return 1;
    }
}
