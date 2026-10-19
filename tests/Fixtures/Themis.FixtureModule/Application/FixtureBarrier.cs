using System.Collections.Concurrent;

namespace Themis.FixtureModule.Application;

/// <summary>
/// A test seam (docs/development/test-seams.md): a barrier the touch-pair command waits at between its two writes,
/// on the first attempt of each command only. A test arms it for a number of parties; the parties release each other
/// once all have arrived, so two commands hold their first rows while both go for their second ones and PostgreSQL
/// detects the deadlock (RR-40, D-41). A command's retry passes without waiting, since its key already arrived once.
/// Unarmed, the barrier does nothing. Waits time out rather than hang a test.
/// </summary>
internal sealed class FixtureBarrier
{
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, byte> _arrived = new(StringComparer.Ordinal);
    private TaskCompletionSource? _release;
    private int _parties;
    private int _waiting;

    /// <summary>Arms the barrier for the next <paramref name="parties"/> distinct idempotency keys; 0 disarms it.</summary>
    public void Arm(int parties)
    {
        lock (_gate)
        {
            _parties = parties;
            _waiting = 0;
            _arrived.Clear();
            _release = parties > 0 ? new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) : null;
        }
    }

    public async Task WaitForPartnersAsync(string idempotencyKey, CancellationToken cancellationToken)
    {
        Task release;
        lock (_gate)
        {
            if (_release is null || !_arrived.TryAdd(idempotencyKey, 0))
            {
                // Not armed, or this key's first attempt already passed: a retry goes straight on (first attempt only).
                return;
            }

            _waiting++;
            release = _release.Task;
            if (_waiting >= _parties)
            {
                _release.TrySetResult();
            }
        }

        await release.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
    }
}
