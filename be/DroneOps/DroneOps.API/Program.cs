using System.Text;
using DroneOps.Application;
using DroneOps.Application.Settings;
using DroneOps.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var configurationArgs = args.Where(a => a is not "--migrate-auth" and not "--bootstrap-manager" and not "--migrate-telemetry" and not "--check-telemetry-schema" and not "--migrate-postflight" and not "--check-postflight-schema").ToArray();
var builder = WebApplication.CreateBuilder(configurationArgs);

#region Services

// Các thiết lập bảo mật local được nạp khi chạy môi trường Development
if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);
}
builder.Configuration.AddEnvironmentVariables();
builder.Configuration.AddCommandLine(configurationArgs);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Configure ConnectionStrings__DefaultConnection or development appsettings.Local.json.");
}

if (args.Contains("--migrate-telemetry") || args.Contains("--check-telemetry-schema"))
{
    await using var source = PostgresDataSourceFactory.Create(connectionString);
    if (args.Contains("--migrate-telemetry")) await TelemetrySchema.MigrateAsync(source);
    else await TelemetrySchema.CheckAsync(source);
    Console.WriteLine("Telemetry schema verified.");
    return;
}

if (args.Contains("--migrate-postflight") || args.Contains("--check-postflight-schema"))
{
    await using var source = PostgresDataSourceFactory.Create(connectionString);
    if (args.Contains("--migrate-postflight")) await PostFlightSchema.MigrateAsync(source);
    else await PostFlightSchema.CheckAsync(source);
    Console.WriteLine("Post-flight schema verified.");
    return;
}

// Khởi tạo nguồn dữ liệu PostgreSQL dùng chung duy nhất cho toàn ứng dụng
//builder.Services.AddSingleton(_ => PostgresDataSourceFactory.Create(connectionString));
builder.Services.AddControllers();
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o => o.MultipartBodyLengthLimit = 106 * 1024 * 1024);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 106 * 1024 * 1024);
builder.Services.AddEndpointsApiExplorer();

// 1. Cấu hình "Cái Khóa" (Authorize button) trên giao diện Swagger UI
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "DroneOps.API",
        Version = "v1"
    });

    // Dùng Full Name (kèm Namespace) làm SchemaId để tránh xung đột tên giữa các lớp Request DTO
    options.CustomSchemaIds(type => type.FullName?.Replace("+", "."));

    // Định nghĩa chuẩn xác thực JWT Bearer cho Swagger
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste your JWT accessToken received after login here (Swagger will automatically prefix with Bearer)."
    });

    // Tự động đính kèm Token vào Header khi gọi API trên Swagger
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddApplicationServices(builder.Configuration);
builder.Services.AddPersistenceServices(builder.Configuration);
builder.Services.AddHostedService<CommunicationLossMonitor>();
builder.Services.AddHostedService<PostFlightCompletionMonitor>();

// 2. Kích hoạt dịch vụ giải mã và kiểm tra JWT Token (Chuẩn bảo mật Production)
// Đọc cấu hình Jwt từ appsettings.json và đăng ký vào DI Container
var jwtSection = builder.Configuration.GetSection(JwtSettings.SectionName);
builder.Services.Configure<JwtSettings>(jwtSection);
var jwtSettings = jwtSection.Get<JwtSettings>()
    ?? throw new InvalidOperationException("Jwt configuration is missing.");

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.IncludeErrorDetails = true; // Bật thông báo chi tiết nếu xác thực không thành công

    options.TokenValidationParameters = new TokenValidationParameters
    {
        // Kiểm tra chữ ký bảo mật hợp lệ
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Key)),

        // Kiểm tra đúng nguồn cấp (Issuer) và đối tượng nhận (Audience)
        ValidateIssuer = true,
        ValidIssuer = jwtSettings.Issuer,
        ValidateAudience = true,
        ValidAudience = jwtSettings.Audience,

        // Kiểm tra hạn sử dụng của Token
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromMinutes(1) // Cho phép độ trễ đồng hồ tối đa 1 phút
    };

    // Bắt sự kiện lỗi xác thực và in ra Console để dễ theo dõi
    options.Events = new JwtBearerEvents
    {
        OnAuthenticationFailed = context =>
        {
            Console.WriteLine($"\n>>> [JWT AUTHENTICATION ERROR]: {context.Exception.Message}\n");
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();

#endregion

var app = builder.Build();
await MissionWorkflowSchema.CheckAsync(app.Services.GetRequiredService<Npgsql.NpgsqlDataSource>());

// Detect a missing deployment migration before background monitoring starts.
await TelemetrySchema.CheckAsync(app.Services.GetRequiredService<Npgsql.NpgsqlDataSource>());
await PostFlightSchema.CheckAsync(app.Services.GetRequiredService<Npgsql.NpgsqlDataSource>());


// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRouting();



app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
