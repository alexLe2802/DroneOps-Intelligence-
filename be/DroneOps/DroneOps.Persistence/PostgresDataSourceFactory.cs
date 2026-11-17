using System.Security.Cryptography.X509Certificates;
using Npgsql;

namespace DroneOps.Persistence;

public static class PostgresDataSourceFactory
{
    public static NpgsqlDataSource Create(string connectionString)
    {
        var settings = new NpgsqlConnectionStringBuilder(connectionString);

        // Respect the configured Supabase endpoint. Port 5432 is the session pooler
        // and 6543 is the transaction pooler; silently rewriting one to the other
        // makes connection incidents impossible to diagnose or work around.
        var configuredPort = Environment.GetEnvironmentVariable("DRONEOPS_DB_PORT");
        if (!string.IsNullOrWhiteSpace(configuredPort))
        {
            if (!int.TryParse(configuredPort, out var port) || port is < 1 or > 65535)
                throw new InvalidOperationException("DRONEOPS_DB_PORT must be a valid TCP port.");
            settings.Port = port;
        }

        // Local configs may point at the original developer's machine. Resolve
        // relative CA paths from the repository root when that path is absent.
        if (!string.IsNullOrWhiteSpace(settings.RootCertificate) && !File.Exists(settings.RootCertificate))
        {
            var fileName = Path.GetFileName(settings.RootCertificate);
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
                 directory is not null;
                 directory = directory.Parent)
            {
                var repositoryCertificate = Path.Combine(
                    directory.FullName, "database", "certificates", fileName);
                if (!File.Exists(repositoryCertificate)) continue;
                settings.RootCertificate = repositoryCertificate;
                break;
            }
        }

        // Keep automatic prepared statements disabled so either pooler mode is safe.
        settings.MaxAutoPrepare = 0;
        // Supabase transaction mode owns the server session. Npgsql must not issue
        // DISCARD ALL when a logical connection closes because Supavisor may still
        // be inside its transaction wrapper (PostgreSQL 25001).
        settings.NoResetOnClose = settings.Port == 6543;
        settings.Pooling = true;
        // A Free Plan database has a small connection budget. Npgsql defaults to
        // 100 pooled connections per process, which can overwhelm Supavisor's
        // session pool during concurrent API/background requests and leave new
        // requests waiting in SCRAM authentication until the BFF times out.
        settings.MinPoolSize = 0;
        settings.MaxPoolSize = Math.Min(settings.MaxPoolSize, 8);
        settings.ConnectionIdleLifetime = Math.Min(settings.ConnectionIdleLifetime, 60);
        settings.ConnectionPruningInterval = Math.Min(settings.ConnectionPruningInterval, 10);
        // Remote TLS and SCRAM negotiation can legitimately exceed eight seconds
        // during a pooler recovery. Keep startup bounded without causing false
        // connection failures on a healthy but briefly busy endpoint.
        settings.Timeout = Math.Clamp(settings.Timeout, 15, 30);
        settings.CommandTimeout = Math.Clamp(settings.CommandTimeout, 15, 30);
        settings.ApplicationName = "DroneOps.API";
        settings.IncludeErrorDetail = false;
        settings.LogParameters = false;

        if (settings.SslMode != SslMode.VerifyFull)
        {
            throw new InvalidOperationException("Database connections must use SSL Mode=VerifyFull.");
        }

        if (string.IsNullOrWhiteSpace(settings.RootCertificate) || !File.Exists(settings.RootCertificate))
        {
            throw new InvalidOperationException("Configure an existing Supabase CA file using Root Certificate.");
        }

        // Validate both the configured CA chain and DNS name explicitly. This
        // avoids macOS reporting NameMismatch before the custom CA is trusted.
        var rootCertificate = X509Certificate2.CreateFromPem(File.ReadAllText(settings.RootCertificate));
        var hostname = settings.Host!;
        settings.RootCertificate = null;
        settings.SslMode = SslMode.Require; // TLS mandatory; full verification below.
        var builder = new NpgsqlDataSourceBuilder(settings.ConnectionString);
        builder.UseUserCertificateValidationCallback((_, certificate, presentedChain, _) =>
        {
            if (certificate is not X509Certificate2 serverCertificate ||
                !serverCertificate.MatchesHostname(hostname, allowWildcards: true, allowCommonName: false))
                return false;

            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(rootCertificate);
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
            // Require a certificate valid for TLS server authentication.
            chain.ChainPolicy.ApplicationPolicy.Add(new System.Security.Cryptography.Oid("1.3.6.1.5.5.7.3.1"));
            if (presentedChain is not null)
            {
                foreach (var element in presentedChain.ChainElements)
                    chain.ChainPolicy.ExtraStore.Add(element.Certificate);
            }
            return chain.Build(serverCertificate);
        });
        return builder.Build();
    }
}
