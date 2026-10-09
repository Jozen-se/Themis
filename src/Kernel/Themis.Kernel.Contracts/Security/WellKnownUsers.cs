using Themis.Kernel.Primitives;

namespace Themis.Kernel.Contracts.Security;

/// <summary>
/// The user id that rows written by the system itself carry in created_by and updated_by: migrations and fixture
/// seeds run by Themis.Migrator. The identity step maps it to a User row when the User table exists (R-AUD-01).
/// </summary>
public static class WellKnownUsers
{
    public static UserId System { get; } = new(Guid.Parse("00000000-0000-7000-8000-000000000001"));
}
