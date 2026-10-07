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
using NeuroVox.WebApi.Extensions;
using NeuroVox.WebApi.Services;
using Serilog;
using Serilog.Context;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
builder.Configuration.AddEnvironmentVariables();

var jwtSecurityKey = builder.Configuration["Auth:Token:SecurityKey"];
if (string.IsNullOrEmpty(jwtSecurityKey) || jwtSecurityKey.Length < 32)
{
    throw new InvalidOperationException(
        "Auth:Token:SecurityKey must be at least 32 characters long. " +
        "Set environment variable Auth__Token__SecurityKey or update appsettings.json");
}

builder.Services.AddHttpContextAccessor();
builder.Services.AddHealthChecks();

builder.Services.AddRateLimitService(builder.Configuration);
builder.Services.AddBaseAuthExceptionHandlers();
builder.Services.AddAuthServices(builder.Configuration);
builder.Services.AddBaseAuthPersistenceServices(builder.Configuration);
builder.Services.AddBaseAuthInfrastructureServices();
builder.Services.AddBaseAuthApplicationServices();

builder.Services.AddNeuroVoxApplicationServices();
builder.Services.AddNeuroVoxInfrastructureServices();
builder.Services.AddNeuroVoxPersistenceServices(builder.Configuration);

builder.Services.AddHttpClient<ISpeechAnalysisClient, SpeechAnalysisClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["NeuroVox:AiBaseUrl"] ?? "http://localhost:8000/");
    client.Timeout = TimeSpan.FromMinutes(10);
});

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

app.UseSwagger();
app.UseSwaggerUI();
app.UseExceptionHandler();

app.UseStatusCodePages();

app.UseForwardedHeaders();

app.UseHsts();

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
app.UseAuthorization();
app.MapControllers();

app.Run();
