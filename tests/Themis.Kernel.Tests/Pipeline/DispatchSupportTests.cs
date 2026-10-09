using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Pipeline.Dispatch;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Tests.Pipeline;

/// <summary>Command metadata (R-SEC-01, P-04), the request hash (R-API-04) and the advisory lock key (R-API-01).</summary>
[Trait("Category", "Unit")]
public sealed class DispatchSupportTests
{
    [Command("ITEM.CREATE")]
    private sealed record CreateItem(string Number, decimal Weight) : ICommand<Guid>;

    [Command("ITEM.UPDATE")]
    private sealed record UpdateItem(Guid Id, long ExpectedVersion, string Number) : ICommand<Guid>, IVersionedCommand;

    private sealed record Unnamed(string Number) : ICommand<Guid>;

    [Fact]
    public void R_SEC_01_the_command_attribute_is_the_permission_code_split_into_type_and_verb()
    {
        CommandMetadata metadata = CommandMetadata.For(typeof(CreateItem));

        Assert.Equal("ITEM.CREATE", metadata.Name);
        Assert.Equal("ITEM", metadata.TypeCode);
        Assert.Equal("CREATE", metadata.Verb);
        Assert.False(metadata.IsVersioned);
        Assert.True(CommandMetadata.For(typeof(UpdateItem)).IsVersioned);
    }

    [Fact]
    public void R_SEC_01_a_command_without_the_attribute_cannot_be_dispatched()
    {
        Assert.Throws<InvalidOperationException>(() => CommandMetadata.For(typeof(Unnamed)));
    }

    [Theory]
    [InlineData("item.create")]
    [InlineData("ITEM")]
    [InlineData("ITEM.CREATE.NOW")]
    [InlineData("Item.Create")]
    public void P_04_permission_codes_are_TYPE_dot_COMMAND(string code)
    {
        Assert.Throws<ArgumentException>(() => new CommandAttribute(code));
    }

    [Fact]
    public void R_API_04_the_request_hash_depends_on_the_values_and_the_command_type_only()
    {
        string first = RequestHash.Compute(new CreateItem("A-1", 1.5m));
        string same = RequestHash.Compute(new CreateItem("A-1", 1.50m));
        string other = RequestHash.Compute(new CreateItem("A-1", 1.51m));

        Assert.Equal(64, first.Length);
        Assert.NotEqual(first, same);
        Assert.NotEqual(first, other);
        Assert.Equal(first, RequestHash.Compute(new CreateItem("A-1", 1.5m)));
        Assert.NotEqual(RequestHash.Compute(new UpdateItem(Guid.Empty, 1, "A-1")), RequestHash.Compute(new CreateItem("A-1", 1.5m)));
    }

    [Fact]
    public void R_API_01_the_advisory_lock_key_is_stable_per_user_and_key_and_differs_between_users()
    {
        UserId alice = new(Guid.Parse("11111111-1111-4111-8111-111111111111"));
        UserId bob = new(Guid.Parse("22222222-2222-4222-8222-222222222222"));
        IdempotencyKey key = IdempotencyKey.From("order-1");

        Assert.Equal(AdvisoryLockKey.For(alice, key), AdvisoryLockKey.For(alice, key));
        Assert.NotEqual(AdvisoryLockKey.For(alice, key), AdvisoryLockKey.For(bob, key));
        Assert.NotEqual(AdvisoryLockKey.For(alice, key), AdvisoryLockKey.For(alice, IdempotencyKey.From("order-2")));
    }
}
