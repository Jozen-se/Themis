using Themis.Kernel.Contracts.Documents;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Documents;

/// <summary>
/// The state machine engine (R-DOC-01): a command on a document is allowed only if the registry lists the
/// transition for the document's type and current status, read from kernel.document_transition on the command's
/// unit of work (D-40, table-driven). Guards and effects per type stay in the type's handlers; the engine decides
/// the transition and names the event it writes (D-44). Numbering-only registry rows have no transitions and are
/// skipped.
/// </summary>
internal interface IDocumentStateMachine
{
    Task<Result<DocumentTransition>> ResolveAsync(IUnitOfWork unitOfWork, string documentType, DocumentStatus current, string command, CancellationToken cancellationToken);
}

internal sealed class DocumentStateMachine : IDocumentStateMachine
{
    public const string TransitionNotAllowed = "transition_not_allowed";

    public async Task<Result<DocumentTransition>> ResolveAsync(IUnitOfWork unitOfWork, string documentType, DocumentStatus current, string command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        IReadOnlyList<DocumentTransition> rows = await unitOfWork.QueryAsync(DatabaseRole.Kernel,
            """
            SELECT tr.command, tr.from_status, tr.to_status, tr.event_name
            FROM kernel.document_transition tr
            JOIN kernel.document_type dt ON dt.code = tr.document_type AND dt.number_assigned <> 'NumberingOnly'
            WHERE tr.document_type = @type AND tr.command = @command AND tr.from_status = @from
            """,
            new Dictionary<string, object?> { ["type"] = documentType, ["command"] = command, ["from"] = current.ToString() },
            reader => new DocumentTransition(reader.GetString(0), Enum.Parse<DocumentStatus>(reader.GetString(1)), Enum.Parse<DocumentStatus>(reader.GetString(2)), reader.GetString(3)),
            cancellationToken);

        // R-DOC-01: only listed transitions are allowed.
        return rows.Count == 0
            ? Error.Rule("R-DOC-01", TransitionNotAllowed, $"{documentType} does not allow {command} from {current}.")
            : rows[0];
    }
}
