using System.Text;
using DroneOps.API.Auth;
using DroneOps.API.HealthChecks;
using DroneOps.API.AI;
using DroneOps.Application;
using DroneOps.Application.Settings;
using DroneOps.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var configurationArgs = args.Where(a => a is not "--migrate-auth" and not "--bootstrap-manager").ToArray();
var builder = WebApplication.CreateBuilder(configurationArgs);

#region Services

// Local development secrets are ignored by Git. Deployment settings override them.
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

// One application-managed data source, disposed with the DI container.
builder.Services.AddSingleton(_ => PostgresDataSourceFactory.Create(connectionString));
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("postgres", timeout: TimeSpan.FromSeconds(10));

builder.Services.Configure<AiSettings>(builder.Configuration.GetSection(AiSettings.SectionName));
builder.Services.AddHttpClient<IAiAssessmentClient, GeminiAssessmentClient>(client =>
{
    client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/");
    client.Timeout = TimeSpan.FromSeconds(builder.Configuration.GetValue("Ai:TimeoutSeconds", 30));
});


builder.Services.AddDroneOpsAuth(builder.Configuration, builder.Environment);
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// 1. Cấu hình "Cái Khóa" (Authorize button) trên giao diện Swagger
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "DroneOps.API",
        Version = "v1"
    });

    // Định nghĩa chuẩn xác thực JWT Bearer cho Swagger
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Dán chuỗi accessToken nhận được sau khi Login vào đây."
    });

    // Yêu cầu Swagger tự động đính kèm Token vào Header khi gọi API
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

builder.Services.AddApplicationServices();
builder.Services.AddPersistenceServices(builder.Configuration);

// 2. Kích hoạt JWT cho API clients, đồng thời giữ FirebaseSession cho web.
var jwtSection = builder.Configuration.GetSection(JwtSettings.SectionName);
builder.Services.Configure<JwtSettings>(jwtSection);
var jwtSettings = jwtSection.Get<JwtSettings>()
    ?? throw new InvalidOperationException("Jwt configuration is missing.");

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = "DroneOpsAuth";
    options.DefaultChallengeScheme = "DroneOpsAuth";
})
.AddPolicyScheme("DroneOpsAuth", "Bearer or Firebase session", options =>
{
    options.ForwardDefaultSelector = context =>
        context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? JwtBearerDefaults.AuthenticationScheme
            : SessionAuthenticationHandler.SchemeName;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings.Issuer,
        ValidAudience = jwtSettings.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Key))
    };
});

#endregion

var app = builder.Build();

#region Middleware

if (await AuthDatabaseCommands.RunAsync(args, app.Services, app.Configuration)) return;
app.UseAuthErrorHandling();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthCsrfProtection();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health/database").AllowAnonymous();

#endregion

app.Run();
