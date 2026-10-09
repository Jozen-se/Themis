namespace Themis.Kernel.Contracts.Commands;

/// <summary>
/// A command's HTTP route as a module declares it, framework-free (ARCH-06): create commands on
/// <c>POST /v1/{area}/{resource}</c>, every other command on <c>POST /v1/{area}/{resource}/{id}/{command}</c>
/// (brief, API conventions; P-04). The composition root maps the routes through the kernel pipeline, which the
/// module never references (D-26); the double dispatch through <see cref="ICommandRouteMapper"/> keeps the generic
/// command and result types without reflection.
/// </summary>
public interface ICommandRoute
{
    string Pattern { get; }

    void Map(ICommandRouteMapper mapper);
}

/// <summary>Implemented by the kernel pipeline: maps one route to its HTTP endpoint.</summary>
public interface ICommandRouteMapper
{
    void Map<TCommand, TResult>(string pattern)
        where TCommand : ICommand<TResult>;
}

public sealed record CommandRoute<TCommand, TResult>(string Pattern) : ICommandRoute
    where TCommand : ICommand<TResult>
{
    public void Map(ICommandRouteMapper mapper)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        mapper.Map<TCommand, TResult>(Pattern);
    }
}
