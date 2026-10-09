namespace Themis.Kernel.Primitives;

/// <summary>
/// How a midpoint is rounded (R-MON-02). The default is half away from zero; a company may configure half even
/// (D-10, Company.rounding_mode).
/// </summary>
public enum RoundingMode
{
    HalfAwayFromZero,
    HalfEven,
}
