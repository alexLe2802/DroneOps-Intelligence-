using DroneOps.API.HealthChecks;
using DroneOps.API.Auth;
using DroneOps.Persistence;

var configurationArgs = args.Where(a => a is not "--migrate-auth" and not "--bootstrap-manager").ToArray();
var builder = WebApplication.CreateBuilder(configurationArgs);

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


builder.Services.AddDroneOpsAuth(builder.Configuration, builder.Environment);
builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (await AuthDatabaseCommands.RunAsync(args, app.Services, app.Configuration)) return;
app.UseAuthErrorHandling();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Production TLS terminates at the same-origin reverse proxy. Local HTTP is development only.
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthCsrfProtection();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health/database").AllowAnonymous();

app.Run();
