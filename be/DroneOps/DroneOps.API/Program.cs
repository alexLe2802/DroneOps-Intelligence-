using DroneOps.API.HealthChecks;
using DroneOps.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Local development secrets are ignored by Git. Deployment settings override them.
if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);
}
builder.Configuration.AddEnvironmentVariables();
builder.Configuration.AddCommandLine(args);

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


builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health/database");

app.Run();
