using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Contracts.Documents;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Documents;

/// <summary>
/// Validation before any business statement: shape and ranges, never a database lookup (Build Brief, Errors). Line
/// numbers are positive and unique in the request, a quantity is never 0 (step 7 brief: a 400), an entered unit
/// cost is zero or above and fits the cost column, and every value fits the range of the column that stores it: the
/// quantity and the base quantity numeric(20,6), the entered unit cost numeric(28,10), and quantity times entered
/// cost numeric(20,4) (RR-46, code value_out_of_range; the product is compared without computing it in decimal).
/// Whether a quantity fits the item's unit is decided inside the transaction (R-INV-02, a 422).
/// </summary>
internal static class InventoryAdjustmentValidation
{
    public const int ExternalReferenceMaxLength = 200;

    /// <summary>The storage scale of a cost column, numeric(28,10) (Specification, Data types).</summary>
    public const int CostScale = 10;

    /// <summary>The columns' ranges (Specification, Data types): quantities numeric(20,6), unit costs numeric(28,10), amounts numeric(20,4).</summary>
    public const int QuantityPrecision = 20;
    public const int QuantityScale = 6;
    public const int UnitCostPrecision = 28;
    public const int AmountPrecision = 20;
    public const int AmountScale = 4;

    public const string ValueOutOfRangeCode = "value_out_of_range";

    /// <summary>Milestone 1 posts in the base unit only, so the base quantity is the quantity times 1 (InventoryAdjustmentDrafts).</summary>
    private const decimal UnitFactor = 1m;

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

            // RR-46: magnitudes against the columns, so that no value reaches the database or the posting arithmetic out of range.
            if (!DecimalBounds.FitsNumeric(line.Quantity, QuantityPrecision, QuantityScale) || !DecimalBounds.ProductFitsNumeric(line.Quantity, UnitFactor, QuantityPrecision, QuantityScale))
            {
                yield return Error.Validation(ValueOutOfRangeCode, $"A quantity holds at most {QuantityPrecision - QuantityScale} digits before the decimal point.", details: Field(details, "quantity"));
            }
            else if (line.EnteredUnitCost is { } unitCost)
            {
                if (!DecimalBounds.FitsNumeric(unitCost, UnitCostPrecision, CostScale))
                {
                    yield return Error.Validation(ValueOutOfRangeCode, $"An entered unit cost holds at most {UnitCostPrecision - CostScale} digits before the decimal point.", details: Field(details, "enteredUnitCost"));
                }
                else if (!DecimalBounds.ProductFitsNumeric(line.Quantity, unitCost, AmountPrecision, AmountScale))
                {
                    yield return Error.Validation(ValueOutOfRangeCode, $"Quantity times entered unit cost holds at most {AmountPrecision - AmountScale} digits before the decimal point.", details: Field(details, "enteredUnitCost"));
                }
            }
        }
    }

    private static Dictionary<string, string> Field(Dictionary<string, string> details, string field)
    {
        Dictionary<string, string> copy = new(details, StringComparer.Ordinal) { ["field"] = field };
        return copy;
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
