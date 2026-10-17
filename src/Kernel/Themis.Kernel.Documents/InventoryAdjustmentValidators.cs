using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Contracts.Documents;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Documents;

/// <summary>
/// Validation before the transaction opens: shape and ranges, never a database lookup (Build Brief, Errors). Line
/// numbers are positive and unique in the request, a quantity is never 0 (step 7 brief: a 400), an entered unit
/// cost is zero or above and fits the cost column; whether a quantity fits the item's unit is decided inside the
/// transaction (R-INV-02, a 422).
/// </summary>
internal static class InventoryAdjustmentValidation
{
    public const int ExternalReferenceMaxLength = 200;

    /// <summary>The storage scale of a cost column, numeric(28,10) (Specification, Data types).</summary>
    public const int CostScale = 10;

    public static IEnumerable<Error> Header(string? externalReference)
    {
        if (externalReference is { } reference && reference.Length > ExternalReferenceMaxLength)
        {
            yield return Error.Validation("external_reference_too_long", $"An external reference holds at most {ExternalReferenceMaxLength} characters.");
        }
    }

    public static IEnumerable<Error> Lines(IReadOnlyList<InventoryAdjustmentLineInput>? lines)
    {
        if (lines is null)
        {
            yield break;
        }

        HashSet<int> numbers = [];
        foreach (InventoryAdjustmentLineInput line in lines)
        {
            Dictionary<string, string> details = new(StringComparer.Ordinal) { ["lineNumber"] = line.LineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture) };
            if (line.LineNumber <= 0)
            {
                yield return Error.Validation("line_number_not_positive", "A line number is above zero.", details: details);
            }
            else if (!numbers.Add(line.LineNumber))
            {
                yield return Error.Validation("line_number_duplicate", $"Line number {line.LineNumber} appears twice.", details: details);
            }

            if (line.ItemId == Guid.Empty)
            {
                yield return Error.Validation("line_item_required", "A line names an item.", details: details);
            }

            if (line.Quantity == 0)
            {
                yield return Error.Validation("line_quantity_zero", "A line quantity is not 0: positive adds stock, negative removes it.", details: details);
            }

            if (line.EnteredUnitCost is < 0)
            {
                yield return Error.Validation("line_entered_unit_cost_negative", "An entered unit cost is zero or above.", details: details);
            }
            else if (line.EnteredUnitCost is { } cost && DecimalRounding.Scale(cost) > CostScale && DecimalRounding.Round(cost, CostScale) != cost)
            {
                // A cost column holds ten decimals (Specification, Data types); PostgreSQL would round silently.
                yield return Error.Validation("line_entered_unit_cost_precision", $"An entered unit cost holds at most {CostScale} decimals.", details: details);
            }
        }
    }
}

internal sealed class CreateInventoryAdjustmentValidator : ICommandValidator<CreateInventoryAdjustment>
{
    public IEnumerable<Error> Validate(CreateInventoryAdjustment command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.CompanyId == Guid.Empty)
        {
            yield return Error.Validation("company_required", "An inventory adjustment is created in a company.");
        }

        foreach (Error error in InventoryAdjustmentValidation.Header(command.ExternalReference).Concat(InventoryAdjustmentValidation.Lines(command.Lines)))
        {
            yield return error;
        }
    }
}

internal sealed class UpdateInventoryAdjustmentValidator : ICommandValidator<UpdateInventoryAdjustment>
{
    public IEnumerable<Error> Validate(UpdateInventoryAdjustment command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return InventoryAdjustmentValidation.Header(command.ExternalReference).Concat(InventoryAdjustmentValidation.Lines(command.Lines));
    }
}
