namespace Themis.Kernel.Primitives;

/// <summary>
/// An effective-dated period: valid from (inclusive) to valid to (exclusive; null means open-ended), as the
/// Phase 0 Specification defines effective dating (R-MD-02). Periods for one business key never overlap.
/// </summary>
public readonly record struct DateRange
{
    public DateRange(BusinessDate validFrom, BusinessDate? validTo)
    {
        if (validTo is { } to && to <= validFrom)
        {
            throw new ArgumentException("valid_to must be after valid_from.", nameof(validTo));
        }

        ValidFrom = validFrom;
        ValidTo = validTo;
    }

    public BusinessDate ValidFrom { get; }

    public BusinessDate? ValidTo { get; }

    public bool IsOpenEnded => ValidTo is null;

    public bool Contains(BusinessDate date) => date >= ValidFrom && (ValidTo is null || date < ValidTo.Value);

    public bool Overlaps(DateRange other) =>
        (ValidTo is null || other.ValidFrom < ValidTo.Value) && (other.ValidTo is null || ValidFrom < other.ValidTo.Value);
}
