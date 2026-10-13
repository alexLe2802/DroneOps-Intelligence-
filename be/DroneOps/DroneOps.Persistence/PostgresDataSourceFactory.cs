using System.Security.Cryptography.X509Certificates;
using Npgsql;

namespace DroneOps.Persistence;

/// <summary>
/// Khởi tạo nguồn dữ liệu PostgreSQL (NpgsqlDataSource) dùng chung cho toàn bộ ứng dụng.
///
/// Nguồn dữ liệu này được khởi tạo một lần duy nhất khi ứng dụng khởi động và sau đó
/// được tái sử dụng bởi Health checks, các lệnh xác thực database và Entity Framework Core.
///
/// Cơ chế mã hóa SSL được điều khiển trực tiếp qua cấu hình chuỗi kết nối (ConnectionStrings:DefaultConnection)
/// thay vì sử dụng các hàm callback chứng chỉ thủ công.
/// </summary>
public static class PostgresDataSourceFactory
{
    public static NpgsqlDataSource Create(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "PostgreSQL connection string is missing.");
        }

        // Phân tích cú pháp và chuẩn hóa chuỗi kết nối.
        var settings = new NpgsqlConnectionStringBuilder(connectionString);

        // Supabase Transaction Pooler không hỗ trợ tính năng tự động prepared statements,
        // do đó luôn giữ tính năng này ở mức vô hiệu hóa (0).
        settings.MaxAutoPrepare = 0;

        // Không bao gồm chi tiết lỗi cơ sở dữ liệu trong phản hồi/log thông thường
        // vì chúng có thể chứa các thông tin nhạy cảm.
        settings.IncludeErrorDetail = false;

        // Tuyệt đối không ghi log giá trị của tham số SQL vì chúng có thể chứa mật khẩu
        // hoặc các dữ liệu người dùng nhạy cảm khác.
        settings.LogParameters = false;

        // Tuyệt đối không cho phép kết nối PostgreSQL không mã hóa (dạng plaintext).
        //
        // Nếu cấu hình đang dùng Disable/Allow/Prefer, tự động nâng cấp lên
        // Require để đảm bảo ứng dụng luôn luôn sử dụng SSL.
        if (settings.SslMode == SslMode.Disable ||
            settings.SslMode == SslMode.Allow ||
            settings.SslMode == SslMode.Prefer)
        {
            settings.SslMode = SslMode.Require;
        }

        var builder = new NpgsqlDataSourceBuilder(settings.ConnectionString);

        // macOS native hostname validation can reject Supabase's valid wildcard
        // certificate. Validate the configured CA chain and hostname explicitly,
        // using .NET's hostname matcher, without bypassing either TLS check.
        if (OperatingSystem.IsMacOS() && settings.SslMode == SslMode.VerifyFull &&
            !string.IsNullOrWhiteSpace(settings.RootCertificate))
        {
            var rootCertificatePath = settings.RootCertificate;
            var hostname = settings.Host ?? throw new InvalidOperationException("PostgreSQL host is required.");
            var checkRevocation = settings.CheckCertificateRevocation;
            settings.RootCertificate = null;
            // Require enables TLS; the callback enforces the full verification.
            settings.SslMode = SslMode.Require;
            builder = new NpgsqlDataSourceBuilder(settings.ConnectionString);
            builder.UseUserCertificateValidationCallback((_, certificate, suppliedChain, _) =>
            {
                if (certificate is null) return false;
                using var leaf = new X509Certificate2(certificate);
                using var root = new X509Certificate2(rootCertificatePath);
                using var chain = new X509Chain();
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.Add(root);
                chain.ChainPolicy.RevocationMode = checkRevocation
                    ? X509RevocationMode.Online : X509RevocationMode.NoCheck;
                if (suppliedChain is not null)
                    foreach (var element in suppliedChain.ChainElements)
                        chain.ChainPolicy.ExtraStore.Add(element.Certificate);
                return leaf.MatchesHostname(hostname) && chain.Build(leaf);
            });
        }

        return builder.Build();
    }
}