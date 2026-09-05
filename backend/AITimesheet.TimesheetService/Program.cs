using System.Text;
using System.Threading.RateLimiting;
using AITimesheet.TimesheetService.Data;
using AITimesheet.TimesheetService.Health;
using AITimesheet.TimesheetService.RepositoryLayer.Implementations;
using AITimesheet.TimesheetService.RepositoryLayer.Interfaces;
using AITimesheet.TimesheetService.ServiceLayer.Clients;
using AITimesheet.TimesheetService.ServiceLayer.Implementations;
using AITimesheet.TimesheetService.ServiceLayer.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// ---- Configuration guards --------------------------------------------------------
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:DefaultConnection is not configured. Run scripts/set-dev-secrets.sh " +
        "or set the ConnectionStrings__DefaultConnection environment variable.");
}

var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey) || Encoding.UTF8.GetByteCount(jwtKey) < 32)
{
    throw new InvalidOperationException(
        "Jwt:Key is missing or shorter than 32 bytes. It must match the identity service's key.");
}

// ---- Database (PostgreSQL) -------------------------------------------------------
builder.Services.AddDbContext<TimesheetDbContext>(options => options.UseNpgsql(connectionString));

// ---- Repositories ----------------------------------------------------------------
builder.Services.AddScoped<ITimesheetRepository, TimesheetRepository>();
builder.Services.AddScoped<IConnectionRepository, ConnectionRepository>();
builder.Services.AddScoped<IActivityRepository, ActivityRepository>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

// ---- Cross-cutting services ------------------------------------------------------
builder.Services.AddMemoryCache();
builder.Services.AddDataProtection();
builder.Services.AddSingleton<ITokenProtector, TokenProtector>();
builder.Services.AddScoped<ITimesheetGenerationService, TimesheetGenerationService>();

// ---- Internal service clients ----------------------------------------------------
builder.Services.AddHttpClient<IdentityServiceClient>();

// ---- Integrations ----------------------------------------------------------------
// Each provider is registered under its own concrete type so it gets its own named
// HttpClient. Registering all four as AddHttpClient<IIntegrationService, T> made them
// share one configuration entry keyed on the interface name, so a timeout or retry
// policy added to one would silently apply to all four.
builder.Services.AddHttpClient<GitHubIntegrationService>();
builder.Services.AddHttpClient<AzureDevOpsIntegrationService>();
builder.Services.AddHttpClient<JiraIntegrationService>();
builder.Services.AddHttpClient<GraphCalendarService>();

builder.Services.AddScoped<IIntegrationService>(sp => sp.GetRequiredService<GitHubIntegrationService>());
builder.Services.AddScoped<IIntegrationService>(sp => sp.GetRequiredService<AzureDevOpsIntegrationService>());
builder.Services.AddScoped<IIntegrationService>(sp => sp.GetRequiredService<JiraIntegrationService>());
builder.Services.AddScoped<IIntegrationService>(sp => sp.GetRequiredService<GraphCalendarService>());

// ---- AI engine -------------------------------------------------------------------
builder.Services.AddHttpClient<IAiTimesheetService, OpenAiTimesheetService>();

// ---- JWT Bearer Authentication (validation only) ---------------------------------
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,

            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),

            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

// ---- Rate limiting ---------------------------------------------------------------
// The chat and generate endpoints fan out to paid AI and third-party APIs, so they are
// limited per authenticated user rather than globally.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("ai", context => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: context.User.Identity?.Name
                      ?? context.Connection.RemoteIpAddress?.ToString()
                      ?? "anonymous",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 20,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
});

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("timesheet-db");

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Timesheet Service API", Version = "v1" });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the token returned by the identity service's /api/auth/login."
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// ---- Database migrations ---------------------------------------------------------
if (builder.Configuration.GetValue("Database:AutoMigrate", app.Environment.IsDevelopment()))
{
    using var scope = app.Services.CreateScope();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        scope.ServiceProvider.GetRequiredService<TimesheetDbContext>().Database.Migrate();
        logger.LogInformation("Timesheet database migrations applied.");
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex, "Failed to apply timesheet database migrations.");
        throw;
    }
}

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health").AllowAnonymous();

app.Run();
