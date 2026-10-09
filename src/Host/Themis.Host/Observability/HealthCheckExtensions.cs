using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Themis.Host.Observability;

/// <summary>
/// Health endpoints (D-25). <c>/health/live</c> answers as soon as the process serves requests;
/// <c>/health/ready</c> also requires a round trip to PostgreSQL as the application's login role.
/// </summary>
internal static class HealthCheckExtensions
{
    internal const string ConnectionStringName = "Themis";

    public static IServiceCollection AddThemisHealthChecks(this IServiceCollection services, IConfiguration configuration)
    {
        IHealthChecksBuilder healthChecks = services.AddHealthChecks();

        string? connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
            healthChecks.AddCheck<PostgresHealthCheck>("postgresql", tags: ["ready"]);
        }
        else
        {
            healthChecks.AddCheck("postgresql", () => HealthCheckResult.Unhealthy($"Connection string '{ConnectionStringName}' is not configured."), tags: ["ready"]);
        }

        return services;
    }

    private sealed class PostgresHealthCheck : IHealthCheck
    {
        private readonly NpgsqlDataSource _dataSource;

        public PostgresHealthCheck(NpgsqlDataSource dataSource)
        {
            _dataSource = dataSource;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
                await using NpgsqlCommand command = connection.CreateCommand();
                command.CommandText = "SELECT 1";
                await command.ExecuteScalarAsync(cancellationToken);
                return HealthCheckResult.Healthy();
            }
            catch (NpgsqlException exception)
            {
                return HealthCheckResult.Unhealthy("PostgreSQL is not reachable.", exception);
            }
        }
    }
}
