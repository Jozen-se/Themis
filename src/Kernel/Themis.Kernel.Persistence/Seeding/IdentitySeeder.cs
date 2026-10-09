using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Themis.Kernel.Contracts.Identity;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Persistence.Entities;
using Themis.Kernel.Persistence.Entities.Identity;
using Themis.Kernel.Persistence.Entities.Organisation;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Persistence.Seeding;

/// <summary>
/// Writes the system-defined permission catalogue and, on request, an identity fixture as themis_migrator, in one
/// transaction each, idempotently. The catalogue is seeded on every run of Themis.Migrator: a missing permission
/// or conflict pair is inserted, a permission's risk level is brought in line, nothing is ever removed (Phase 0
/// Specification, Security entities; R-SEC-02). The fixture inserts users, roles, role permissions and
/// assignments whose ids are missing. Every row inserted gets one audit event under the system user (R-AUD-01);
/// the seed is not a command and writes no outbox event. A fixture whose roles or assignments would give one user
/// both permissions of a conflict pair is refused before anything is written (R-SEC-02); the check itself lives in
/// Themis.Kernel.Identity and arrives as a function, since capabilities meet only through Contracts (ARCH-08).
/// </summary>
internal static class IdentitySeeder
{
    public const string CatalogueCommandName = "SYSTEM.SEED_CATALOGUE";
    public const string FixtureCommandName = OrganisationFixtureSeeder.CommandName;

    private static readonly JsonSerializerOptions ChangesJson = new(JsonSerializerDefaults.Web);

    public static async Task<int> ApplyCatalogueAsync(string migratorConnectionString, PermissionCatalogDefinition catalogue, DateTimeOffset now, UserId seededBy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        await using KernelDbContext context = KernelDbContextFactory.ForMigrations(migratorConnectionString);
        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        Guid by = seededBy.Value;
        int changed = 0;

        foreach (PermissionDefinition definition in catalogue.Permissions)
        {
            PermissionCodes.Validated(definition.Code);
            string code = definition.Code;
            Permission? existing = await context.Permissions.SingleOrDefaultAsync(p => p.Code == code, cancellationToken);
            string riskLevel = definition.RiskLevel.ToString();
            if (existing is null)
            {
                Permission permission = new() { Id = EntityId.New(), Code = code, RiskLevel = riskLevel, CreatedAt = now, CreatedBy = by, UpdatedAt = now, UpdatedBy = by };
                context.Permissions.Add(permission);
                Audit(context, CatalogueCommandName, "Permission", permission.Id, now, by, new Dictionary<string, FieldChange> { ["code"] = new(null, code), ["riskLevel"] = new(null, riskLevel) });
                changed++;
            }
            else if (!string.Equals(existing.RiskLevel, riskLevel, StringComparison.Ordinal))
            {
                string before = existing.RiskLevel;
                existing.RiskLevel = riskLevel;
                existing.UpdatedAt = now;
                existing.UpdatedBy = by;
                existing.Version++;
                Audit(context, CatalogueCommandName, "Permission", existing.Id, now, by, new Dictionary<string, FieldChange> { ["riskLevel"] = new(before, riskLevel) });
                changed++;
            }
        }

        foreach (SodConflictDefinition pair in catalogue.Conflicts)
        {
            if (string.CompareOrdinal(pair.PermissionA, pair.PermissionB) >= 0)
            {
                throw new ArgumentException($"Conflict pair {pair.PermissionA} with {pair.PermissionB} is not in code order.", nameof(catalogue));
            }

            string a = pair.PermissionA;
            string b = pair.PermissionB;
            if (!await context.SodConflicts.AnyAsync(c => c.PermissionA == a && c.PermissionB == b, cancellationToken))
            {
                SodConflict conflict = new() { Id = EntityId.New(), PermissionA = a, PermissionB = b, CreatedAt = now, CreatedBy = by, UpdatedAt = now, UpdatedBy = by };
                context.SodConflicts.Add(conflict);
                Audit(context, CatalogueCommandName, "SodConflict", conflict.Id, now, by, new Dictionary<string, FieldChange> { ["permissionA"] = new(null, a), ["permissionB"] = new(null, b) });
                changed++;
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return changed;
    }

    public static async Task<Result<int>> ApplyFixtureAsync(string migratorConnectionString, IdentityFixtureDefinition fixture, Func<IdentityFixtureDefinition, Result> segregationOfDutiesCheck, DateTimeOffset now, UserId seededBy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentNullException.ThrowIfNull(segregationOfDutiesCheck);
        // R-SEC-02: refused before any row is written.
        Result check = segregationOfDutiesCheck(fixture);
        if (check.IsFailure)
        {
            return Result<int>.Failure(check.Errors);
        }

        await using KernelDbContext context = KernelDbContextFactory.ForMigrations(migratorConnectionString);
        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        Guid by = seededBy.Value;
        int inserted = 0;

        foreach (UserDefinition user in fixture.Users)
        {
            inserted += await AddIfMissingAsync(context, "User", user, user.Id, now, by, () => new UserAccount
            {
                Id = user.Id,
                ExternalSubject = user.ExternalSubject,
                DisplayName = user.DisplayName,
                Email = user.Email,
                IsServiceAccount = user.IsServiceAccount,
                Status = user.Status.ToString(),
                CreatedAt = now,
                CreatedBy = by,
                UpdatedAt = now,
                UpdatedBy = by,
            }, cancellationToken);
        }

        foreach (RoleDefinition role in fixture.Roles)
        {
            inserted += await AddIfMissingAsync(context, "Role", role with { Permissions = [] }, role.Id, now, by, () => new Role
            {
                Id = role.Id,
                Code = role.Code.Value,
                Name = role.Name,
                CreatedAt = now,
                CreatedBy = by,
                UpdatedAt = now,
                UpdatedBy = by,
            }, cancellationToken);

            foreach (string permissionCode in role.Permissions)
            {
                Guid roleId = role.Id;
                if (await context.RolePermissions.AnyAsync(rp => rp.RoleId == roleId && rp.PermissionCode == permissionCode, cancellationToken))
                {
                    continue;
                }

                RolePermission rolePermission = new() { Id = EntityId.New(), RoleId = role.Id, PermissionCode = PermissionCodes.Validated(permissionCode), CreatedAt = now, CreatedBy = by, UpdatedAt = now, UpdatedBy = by };
                context.RolePermissions.Add(rolePermission);
                Audit(context, FixtureCommandName, "RolePermission", rolePermission.Id, now, by, new Dictionary<string, FieldChange> { ["roleId"] = new(null, role.Id.ToString()), ["permissionCode"] = new(null, permissionCode) });
                inserted++;
            }
        }

        foreach (RoleAssignmentDefinition assignment in fixture.Assignments)
        {
            inserted += await AddIfMissingAsync(context, "RoleAssignment", assignment, assignment.Id, now, by, () => new RoleAssignment
            {
                Id = assignment.Id,
                UserId = assignment.UserId,
                RoleId = assignment.RoleId,
                CompanyId = assignment.CompanyId,
                SiteId = assignment.SiteId,
                ValidFrom = assignment.ValidFrom.Value,
                ValidTo = assignment.ValidTo?.Value,
                AssignedBy = by,
                CreatedAt = now,
                CreatedBy = by,
                UpdatedAt = now,
                UpdatedBy = by,
            }, cancellationToken);
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return inserted;
    }

    private static async Task<int> AddIfMissingAsync<T>(KernelDbContext context, string entityType, object definition, Guid id, DateTimeOffset now, Guid by, Func<T> create, CancellationToken cancellationToken)
        where T : MasterDataRow
    {
        if (await context.Set<T>().AnyAsync(row => row.Id == id, cancellationToken))
        {
            return 0;
        }

        context.Set<T>().Add(create());
        Audit(context, FixtureCommandName, entityType, id, now, by, ChangesFrom(definition));
        return 1;
    }

    /// <summary>R-AUD-01: one audit event per seeded row.</summary>
    private static void Audit(KernelDbContext context, string commandName, string entityType, Guid entityId, DateTimeOffset now, Guid by, IReadOnlyDictionary<string, FieldChange> changes) =>
        context.AuditEvents.Add(new AuditEvent
        {
            Id = EntityId.New(),
            OccurredAt = now,
            UserId = by,
            CommandName = commandName,
            EntityType = entityType,
            EntityId = entityId,
            Changes = JsonSerializer.Serialize(changes, ChangesJson),
        });

    private static Dictionary<string, FieldChange> ChangesFrom(object definition)
    {
        JsonObject values = JsonSerializer.SerializeToNode(definition, definition.GetType(), ChangesJson)!.AsObject();
        Dictionary<string, FieldChange> changes = new(StringComparer.Ordinal);
        foreach ((string name, JsonNode? value) in values)
        {
            JsonNode? unwrapped = value is JsonObject { Count: 1 } single && single.TryGetPropertyValue("value", out JsonNode? inner) ? inner : value;
            string? after = unwrapped switch
            {
                null => null,
                JsonValue scalar => scalar.ToString(),
                _ => unwrapped.ToJsonString(ChangesJson),
            };
            changes[name] = new FieldChange(null, after);
        }

        return changes;
    }
}
