using System.Text;
using DroneOps.Application;
using DroneOps.Application.Settings;
using DroneOps.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var configurationArgs = args.Where(a => a is not "--migrate-auth" and not "--bootstrap-manager" and not "--migrate-mission-workflow" and not "--migrate-drone-connections").ToArray();
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

if (args.Contains("--migrate-drone-connections"))
{
    await using var source = PostgresDataSourceFactory.Create(connectionString);
    await DroneConnectionSchema.ApplyAsync(source);
    Console.WriteLine("Drone connection schema verified.");
    return;
}

if (args.Contains("--migrate-mission-workflow"))
{
    await using var source = PostgresDataSourceFactory.Create(connectionString);
    await MissionWorkflowSchema.ApplyAsync(source);
    Console.WriteLine("Mission workflow schema verified.");
    return;
}

// Khởi tạo nguồn dữ liệu PostgreSQL dùng chung duy nhất cho toàn ứng dụng
//builder.Services.AddSingleton(_ => PostgresDataSourceFactory.Create(connectionString));
builder.Services.AddControllers(options => options.Filters.Add<MissionValidationExceptionFilter>());
builder.Services.Configure<DroneOps.Application.Services.Missions.MissionWeatherOptions>(builder.Configuration.GetSection("MissionWeather"));
builder.Services.AddHostedService<MissionPreflightMonitor>();
builder.Services.AddHttpClient<DroneOps.API.Storage.UserMediaStorage>();
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
        OnMessageReceived = context =>
        {
            // Gateway routes validate a separate scoped opaque token in the service.
            // Do not attempt to parse it as a pilot JWT or log false JWT failures.
            if (context.Request.Path.StartsWithSegments("/api/drone-gateway")) context.NoResult();
            return Task.CompletedTask;
        },
        OnAuthenticationFailed = context =>
        {
            Console.WriteLine($"\n>>> [JWT AUTHENTICATION ERROR]: {context.Exception.Message}\n");
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("gateway-pairing", context => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
        { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("gateway-ingest", context => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
        { PermitLimit = 240, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

#endregion

var app = builder.Build();
await MissionWorkflowSchema.CheckAsync(app.Services.GetRequiredService<Npgsql.NpgsqlDataSource>());
await DroneConnectionSchema.CheckAsync(app.Services.GetRequiredService<Npgsql.NpgsqlDataSource>());


// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRouting();



app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();

app.Run();
