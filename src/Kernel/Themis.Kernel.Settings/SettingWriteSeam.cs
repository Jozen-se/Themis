using Themis.Kernel.Contracts.Settings;

namespace Themis.Kernel.Settings;

/// <summary>
/// A pause point in SETTING.SET and SETTING.CLEAR between the slot's version check and the insert, for the test that
/// proves a write losing the race on the slot's version gets the same 412 as a stale If-Match (R-API-02, RR-45). The
/// production registration is <see cref="NoSettingWriteSeam"/>, which returns at once (docs/development/test-seams.md).
/// </summary>
internal interface ISettingWriteSeam
{
    Task AfterVersionCheckAsync(string key, SettingLevel level, CancellationToken cancellationToken);
}

internal sealed class NoSettingWriteSeam : ISettingWriteSeam
{
    public Task AfterVersionCheckAsync(string key, SettingLevel level, CancellationToken cancellationToken) => Task.CompletedTask;
}
