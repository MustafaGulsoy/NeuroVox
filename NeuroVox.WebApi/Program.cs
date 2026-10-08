using System.Text.Json.Serialization;
using AspNetCoreRateLimit;
using BaseAuth.Application;
using BaseAuth.Infrastructure;
using BaseAuth.Infrastructure.Filters;
using BaseAuth.Library.WebApi.Filters;
using BaseAuth.Library.WebApi.Handler;
using BaseAuth.Library.WebApi.Registirations;
using BaseAuth.Persistence;
using NeuroVox.Application;
using NeuroVox.Infrastructure;
using NeuroVox.Persistence;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using NeuroVox.Application.Abstractions.Services;
using NeuroVox.Persistence.Contexts;
using NeuroVox.WebApi.Background;
using NeuroVox.WebApi.Extensions;
using NeuroVox.WebApi.Middlewares;
using NeuroVox.WebApi.Services;
using Serilog;
using Serilog.Context;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
builder.Configuration.AddEnvironmentVariables();

var jwtSecurityKey = builder.Configuration["Auth:Token:SecurityKey"];
if (string.IsNullOrEmpty(jwtSecurityKey) || jwtSecurityKey.Length < 32 || jwtSecurityKey.StartsWith("CHANGE_ME"))
{
    throw new InvalidOperationException(
        "Auth:Token:SecurityKey must be at least 32 characters long. " +
        "Set environment variable Auth__Token__SecurityKey or update appsettings.json");
}

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantProvider, TenantProvider>();
builder.Services.AddSingleton<AudioStorage>();
builder.Services.AddSingleton<AnalysisQueue>();
builder.Services.AddSingleton<ModelStorage>();
builder.Services.AddHostedService<RecordingAnalysisWorker>();
builder.Services.AddHostedService<KaggleKeeper>();
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 200_000_000);
// Runs behind a reverse proxy (TLS terminates there); trust its X-Forwarded-* headers.
// ponytail: KnownNetworks cleared = trust any proxy in front; pin the proxy network if the API port is ever exposed directly.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
});
builder.Services.AddHealthChecks();

builder.Services.AddRateLimitService(builder.Configuration);
builder.Services.AddBaseAuthExceptionHandlers();
// UseExceptionHandler() below needs this (or a path); without it the app crashes at startup. Unhandled errors become RFC 7807 500s.
builder.Services.AddProblemDetails();
builder.Services.AddAuthServices(builder.Configuration);
builder.Services.AddBaseAuthPersistenceServices(builder.Configuration);
builder.Services.AddBaseAuthInfrastructureServices();
builder.Services.AddBaseAuthApplicationServices();

// BaseAuth's AddIdentity makes the Identity cookie the default scheme, so a bare [Authorize] never looks at the
// JWT (every request would be 401). Pin the default to BaseAuth's "Admin" JWT-bearer scheme, as BaseAuth.WebApi does per controller.
builder.Services.AddAuthentication(o =>
{
    o.DefaultScheme = o.DefaultAuthenticateScheme = o.DefaultChallengeScheme = o.DefaultForbidScheme = "Admin";
});
builder.Services.AddAuthorization(o =>
    o.DefaultPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder("Admin").RequireAuthenticatedUser().Build());

builder.Services.AddNeuroVoxApplicationServices();
builder.Services.AddNeuroVoxInfrastructureServices();
builder.Services.AddNeuroVoxPersistenceServices(builder.Configuration);

builder.Services.AddHttpClient<ISpeechAnalysisClient, SpeechAnalysisClient>(client => client.Timeout = TimeSpan.FromMinutes(10));
builder.Services.AddHttpClient<IKaggleClient, KaggleClient>(client => client.Timeout = TimeSpan.FromSeconds(60));
builder.Services.AddSingleton<SecretProtector>();
builder.Services.AddSingleton<AiHostPool>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var arrayForm = builder.Configuration.GetSection("CORS:AllowedOrigins").Get<string[]>();
        var scalarForm = builder.Configuration["CORS:AllowedOrigins"]?.Split(',');
        var allowedOrigins = (arrayForm != null && arrayForm.Length > 0 ? arrayForm : scalarForm)
            ?? new[] { "http://localhost:4200" };

        if (allowedOrigins.Any(o => o.Trim() == "*"))
            throw new InvalidOperationException("Wildcard (*) origin cannot be used with AllowCredentials.");

        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var log = new LoggerConfiguration()
    .WriteTo.File("logs/log.txt")
    .WriteTo.PostgreSQL(
        builder.Configuration["Auth:ConnectionString"],
        builder.Configuration["Auth:LogTable"] ?? "logs",
        needAutoCreateTable: true)
    .Enrich.FromLogContext()
    .MinimumLevel.Information()
    .CreateLogger();

builder.Host.UseSerilog(log);

builder.Services.AddControllers(options =>
{
    options.Filters.Add<ValidationFilter>();
    options.Filters.Add<RolePermissionFilter>();
})
.AddJsonOptions(options =>
{
    options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    options.JsonSerializerOptions.WriteIndented = true;
})
.ConfigureApiBehaviorOptions(options => options.SuppressModelStateInvalidFilter = true);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Swagger:Enabled"))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseExceptionHandler();

app.UseStatusCodePages();

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment()) app.UseHsts();

app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");

    if (context.Request.IsHttps)
        context.Response.Headers.Append("Strict-Transport-Security", "max-age=31536000; includeSubDomains");

    await next();
});

app.UseCustomerHeader();
app.UseHttpsRedirection();
app.UseIpRateLimiting();
app.UseStaticFiles();
app.UseCors();
app.UseAuthentication();
app.UseMiddleware<TenantBindingMiddleware>();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");

if (app.Configuration.GetValue("NeuroVox:AutoMigrate", true))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<NeuroVoxDbContext>().Database.Migrate();
}

app.Run();

// Needed by WebApplicationFactory in NeuroVox.Tests.
public partial class Program { }
