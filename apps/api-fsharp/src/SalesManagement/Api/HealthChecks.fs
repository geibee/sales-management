module SalesManagement.Api.HealthChecks

open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Diagnostics.HealthChecks
open Npgsql

type PostgresHealthCheck(connectionString: string) =
    interface IHealthCheck with
        member _.CheckHealthAsync(_: HealthCheckContext, ct: CancellationToken) : Task<HealthCheckResult> = task {
            try
                use conn = new NpgsqlConnection(connectionString)
                do! conn.OpenAsync(ct)
                use cmd = new NpgsqlCommand("SELECT 1", conn)
                let! _ = cmd.ExecuteScalarAsync(ct)
                return HealthCheckResult.Healthy("PostgreSQL is reachable")
            with ex ->
                return HealthCheckResult.Unhealthy("PostgreSQL is unreachable", ex)
        }
