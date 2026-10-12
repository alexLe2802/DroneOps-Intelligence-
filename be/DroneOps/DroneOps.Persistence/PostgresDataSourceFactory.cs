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

        // Npgsql sẽ tự động xử lý xác thực chứng chỉ SSL/TLS dựa theo cấu hình
        // SslMode và các thiết lập Root Certificate trong chuỗi kết nối.
        //
        // Khi phát triển (Development):
        //     Ssl Mode=Require
        //
        // Khi triển khai thực tế (Production):
        //     Ssl Mode=VerifyFull
        //     Root Certificate=<Đường dẫn file chứng chỉ CA của Supabase>
        //
        // Hoàn toàn không cần viết callback chứng chỉ thủ công.
        var builder = new NpgsqlDataSourceBuilder(settings.ConnectionString);

        return builder.Build();
    }
}