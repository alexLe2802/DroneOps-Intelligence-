using System.Security.Cryptography.X509Certificates;
using Npgsql;

namespace DroneOps.Persistence;

public static class PostgresDataSourceFactory
{
    public static NpgsqlDataSource Create(string connectionString)
    {
        var settings = new NpgsqlConnectionStringBuilder(connectionString);

        // Pooler transaction mode: do not enable automatic prepared statements.
        settings.MaxAutoPrepare = 0;
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
        builder.UseSslClientAuthenticationOptionsCallback(options =>
        {
            options.RemoteCertificateValidationCallback = (_, certificate, presentedChain, _) =>
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
            };
        });
        return builder.Build();
    }
}
