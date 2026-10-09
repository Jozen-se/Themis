using System.Collections.Concurrent;
using System.Reflection;
using Themis.Kernel.Contracts.Commands;

namespace Themis.Kernel.Pipeline.Dispatch;

/// <summary>What the pipeline knows about a command type from its <see cref="CommandAttribute"/>: the permission code is the command name (R-SEC-01, P-04).</summary>
internal sealed record CommandMetadata(string Name, string TypeCode, string Verb, bool IsVersioned)
{
    private static readonly ConcurrentDictionary<Type, CommandMetadata> Cache = new();

    public static CommandMetadata For(Type commandType) => Cache.GetOrAdd(commandType, Read);

    private static CommandMetadata Read(Type commandType)
    {
        CommandAttribute attribute = commandType.GetCustomAttribute<CommandAttribute>(inherit: false)
            ?? throw new InvalidOperationException($"{commandType.FullName} carries no [Command(\"TYPE.COMMAND\")] attribute; every command names its permission (R-SEC-01).");
        return new CommandMetadata(attribute.PermissionCode, attribute.TypeCode, attribute.Verb, typeof(IVersionedCommand).IsAssignableFrom(commandType));
    }
}
