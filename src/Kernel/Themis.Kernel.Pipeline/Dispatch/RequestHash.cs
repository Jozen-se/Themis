using System.Buffers.Binary;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Pipeline.Json;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Pipeline.Dispatch;

/// <summary>
/// The request fingerprint stored with an idempotency record: SHA-256 over the command type, the command serialised
/// with the API's JSON options (so formatting differences do not count and value differences do) and the If-Match
/// version of a versioned command, so the same key with another version is another request (R-API-04, R-API-02).
/// Only the command's own data is serialised: the members <see cref="IScopedRequest"/> declares are computed from that
/// data, and reading them before the validators ran would turn a missing company into a 500 (RR-60).
/// </summary>
internal static class RequestHash
{
    /// <summary>The members every scoped request computes (D-47), never part of the fingerprint.</summary>
    private static readonly HashSet<string> ComputedMembers = [.. typeof(IScopedRequest).GetProperties().Select(property => property.Name)];

    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static string Compute<TCommand>(TCommand command, long? expectedVersion)
    {
        string json = JsonSerializer.Serialize(command, Options);
        string version = expectedVersion?.ToString(CultureInfo.InvariantCulture) ?? "-";
        byte[] bytes = Encoding.UTF8.GetBytes(typeof(TCommand).FullName + "\n" + json + "\n" + version);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    /// <summary>The API's options with one contract change for every scoped request: its computed members are left out (RR-60).</summary>
    private static JsonSerializerOptions CreateOptions()
    {
        JsonSerializerOptions options = new(ThemisJson.Options)
        {
            TypeInfoResolver = (ThemisJson.Options.TypeInfoResolver ?? new DefaultJsonTypeInfoResolver()).WithAddedModifier(SkipComputedScope),
        };
        options.MakeReadOnly();
        return options;
    }

    private static void SkipComputedScope(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object || !typeof(IScopedRequest).IsAssignableFrom(typeInfo.Type))
        {
            return;
        }

        for (int i = typeInfo.Properties.Count - 1; i >= 0; i--)
        {
            if (typeInfo.Properties[i].AttributeProvider is PropertyInfo property && ComputedMembers.Contains(property.Name))
            {
                typeInfo.Properties.RemoveAt(i);
            }
        }
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
