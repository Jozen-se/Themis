using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Pipeline.Json;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Pipeline.Dispatch;

/// <summary>
/// The request hash stored with an idempotency record: SHA-256 over the command type and the command serialised with
/// the API's JSON options, so formatting differences do not count and value differences do (R-API-04).
/// </summary>
internal static class RequestHash
{
    public static string Compute<TCommand>(TCommand command)
    {
        string json = JsonSerializer.Serialize(command, ThemisJson.Options);
        byte[] bytes = Encoding.UTF8.GetBytes(typeof(TCommand).FullName + "\n" + json);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}

/// <summary>
/// The 64-bit key of the transaction-scoped advisory lock a command takes on its idempotency key, derived from the
/// user and the key so that one user's duplicate blocks nobody else (R-API-01).
/// </summary>
internal static class AdvisoryLockKey
{
    public static long For(UserId userId, IdempotencyKey key)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{userId.Value:N}:{key.Value}"));
        return BinaryPrimitives.ReadInt64LittleEndian(hash);
    }
}
