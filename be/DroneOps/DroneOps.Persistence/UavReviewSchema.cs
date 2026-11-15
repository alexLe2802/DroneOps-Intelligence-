using Npgsql;

namespace DroneOps.Persistence;

public static class UavReviewSchema
{
    private const string Resource =
        "DroneOps.Persistence.Migrations.20261114000100_uav_review_proposals.sql";

    public static async Task ApplyAsync(
        NpgsqlDataSource source,
        CancellationToken cancellationToken = default)
    {
        await using var stream = typeof(UavReviewSchema).Assembly
            .GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException(
                $"Embedded UAV review migration is missing: {Resource}.");
        using var reader = new StreamReader(stream);
        await using var command = source.CreateCommand(
            await reader.ReadToEndAsync(cancellationToken));
        command.CommandTimeout = 120;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await CheckAsync(source, cancellationToken);
    }

    public static async Task CheckAsync(
        NpgsqlDataSource source,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await SchemaCommandExecutor.ExecuteAsync(source, """
                SELECT "HasPendingProposal", "RegistryVersion", "HasApprovedInformation"
                FROM public."UAV" LIMIT 0;
                SELECT "Id", "UavId", "SubmittedBy", "SubmittedAt", "BaseVersion",
                       "Kind", "Status", "BaseInformationJson", "ProposedInformationJson",
                       "ReviewedBy", "ReviewedAt", "Reason"
                FROM public."UavReviewRequest" LIMIT 0;
                """, cancellationToken);
        }
        catch (PostgresException exception) when (
            exception.SqlState is "42P01" or "42703")
        {
            throw new InvalidOperationException(
                "Apply the UAV review migration with --migrate-uav-reviews before starting the API.",
                exception);
        }
    }
}
