using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Themis.Kernel.Contracts.Documents;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Persistence.Entities;
using Themis.Kernel.Primitives;
using DocumentType = Themis.Kernel.Persistence.Entities.Documents.DocumentType;
using TransitionRow = Themis.Kernel.Persistence.Entities.Documents.DocumentTransition;

namespace Themis.Kernel.Persistence.Seeding;

/// <summary>
/// Writes the document type registry from the catalogue in code as themis_migrator, on every run of
/// Themis.Migrator, in one transaction (D-40, step 7 brief): a missing type or transition is inserted, a row whose
/// values differ is brought in line, nothing is ever removed. The catalogue is checked against the registry rules
/// first (Specification, DocumentType), so a numbering-only row with a posting flag never reaches the database,
/// whose check constraint is the second line of defence. Every row written gets one audit event under the system
/// user (R-AUD-01); no outbox event, because no command ran.
/// </summary>
internal static class DocumentTypeSeeder
{
    public const string CommandName = IdentitySeeder.CatalogueCommandName;

    private static readonly JsonSerializerOptions ChangesJson = new(JsonSerializerDefaults.Web);

    public static async Task<Result<int>> ApplyCatalogueAsync(string migratorConnectionString, IReadOnlyList<DocumentTypeDefinition> catalogue, DateTimeOffset now, UserId seededBy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        IReadOnlyList<string> violations = DocumentTypes.Violations(catalogue);
        if (violations.Count > 0)
        {
            return Result<int>.Failure(violations.Select(violation => Error.Validation("document_type_registry_invalid", violation)));
        }

        await using KernelDbContext context = KernelDbContextFactory.ForMigrations(migratorConnectionString);
        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        Guid by = seededBy.Value;
        // D-51: one command id per seeder call, shared by every audit event it writes.
        Guid commandId = EntityId.New();
        int changed = 0;

        foreach (DocumentTypeDefinition definition in catalogue)
        {
            string code = definition.Code;
            DocumentType? existing = await context.DocumentTypes.SingleOrDefaultAsync(t => t.Code == code, cancellationToken);
            Dictionary<string, FieldChange> changes = new(StringComparer.Ordinal);
            if (existing is null)
            {
                existing = new DocumentType
                {
                    Id = EntityId.New(),
                    Code = code,
                    NumberAssigned = definition.NumberAssigned.ToString(),
                    Gapless = definition.Gapless,
                    PostsInventory = definition.PostsInventory,
                    PostsGeneralLedger = definition.PostsGeneralLedger,
                    PartyRole = definition.PartyRole,
                    CreatedAt = now,
                    CreatedBy = by,
                    UpdatedAt = now,
                    UpdatedBy = by,
                };
                context.DocumentTypes.Add(existing);
                changes["code"] = new(null, code);
                changes["numberAssigned"] = new(null, existing.NumberAssigned);
                changes["gapless"] = new(null, Text(existing.Gapless));
                changes["postsInventory"] = new(null, Text(existing.PostsInventory));
                changes["postsGeneralLedger"] = new(null, Text(existing.PostsGeneralLedger));
                changes["partyRole"] = new(null, existing.PartyRole);
            }
            else
            {
                Align(changes, "numberAssigned", existing.NumberAssigned, definition.NumberAssigned.ToString(), value => existing.NumberAssigned = value!);
                Align(changes, "gapless", Text(existing.Gapless), Text(definition.Gapless), value => existing.Gapless = value == "True");
                Align(changes, "postsInventory", Text(existing.PostsInventory), Text(definition.PostsInventory), value => existing.PostsInventory = value == "True");
                Align(changes, "postsGeneralLedger", Text(existing.PostsGeneralLedger), Text(definition.PostsGeneralLedger), value => existing.PostsGeneralLedger = value == "True");
                Align(changes, "partyRole", existing.PartyRole, definition.PartyRole, value => existing.PartyRole = value);
                if (changes.Count > 0)
                {
                    existing.UpdatedAt = now;
                    existing.UpdatedBy = by;
                    existing.Version++;
                }
            }

            if (changes.Count > 0)
            {
                Audit(context, commandId, "DocumentType", existing.Id, now, by, changes);
                changed++;
            }

            // Numbering-only rows have no transitions (Specification, DocumentType); the catalogue check above guarantees it.
            foreach (DocumentTransition transition in definition.Transitions)
            {
                string command = transition.Command;
                string from = transition.From.ToString();
                TransitionRow? row = await context.DocumentTransitions.SingleOrDefaultAsync(t => t.DocumentType == code && t.Command == command && t.FromStatus == from, cancellationToken);
                Dictionary<string, FieldChange> transitionChanges = new(StringComparer.Ordinal);
                if (row is null)
                {
                    row = new TransitionRow
                    {
                        Id = EntityId.New(),
                        DocumentType = code,
                        Command = command,
                        FromStatus = from,
                        ToStatus = transition.To.ToString(),
                        EventName = transition.EventName,
                        CreatedAt = now,
                        CreatedBy = by,
                        UpdatedAt = now,
                        UpdatedBy = by,
                    };
                    context.DocumentTransitions.Add(row);
                    transitionChanges["documentType"] = new(null, code);
                    transitionChanges["command"] = new(null, command);
                    transitionChanges["fromStatus"] = new(null, from);
                    transitionChanges["toStatus"] = new(null, row.ToStatus);
                    transitionChanges["eventName"] = new(null, row.EventName);
                }
                else
                {
                    Align(transitionChanges, "toStatus", row.ToStatus, transition.To.ToString(), value => row.ToStatus = value!);
                    Align(transitionChanges, "eventName", row.EventName, transition.EventName, value => row.EventName = value!);
                    if (transitionChanges.Count > 0)
                    {
                        row.UpdatedAt = now;
                        row.UpdatedBy = by;
                        row.Version++;
                    }
                }

                if (transitionChanges.Count > 0)
                {
                    Audit(context, commandId, "DocumentTransition", row.Id, now, by, transitionChanges);
                    changed++;
                }
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return changed;
    }

    private static void Align(Dictionary<string, FieldChange> changes, string field, string? current, string? wanted, Action<string?> apply)
    {
        if (!string.Equals(current, wanted, StringComparison.Ordinal))
        {
            apply(wanted);
            changes[field] = new FieldChange(current, wanted);
        }
    }

    private static string Text(bool value) => value ? "True" : "False";

    private static void Audit(KernelDbContext context, Guid commandId, string entityType, Guid entityId, DateTimeOffset now, Guid by, IReadOnlyDictionary<string, FieldChange> changes) =>
        context.AuditEvents.Add(new AuditEvent
        {
            Id = EntityId.New(),
            CommandId = commandId,
            OccurredAt = now,
            UserId = by,
            CommandName = CommandName,
            EntityType = entityType,
            EntityId = entityId,
            Changes = JsonSerializer.Serialize(changes, ChangesJson),
        });
}
