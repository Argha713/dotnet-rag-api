using System.Text.Json;
using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RagApi.Api.Middleware;
using RagApi.Api.Models;
using RagApi.Api.Validators;
using RagApi.Application.Interfaces;
using RagApi.Application.Models;
using RagApi.Infrastructure;
using RagApi.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "RAG API",
        Version = "v1",
        Description = "A production-ready Retrieval-Augmented Generation (RAG) API built with .NET 8. " +
                      "Upload documents, ask questions, and get AI-powered answers with source citations.",
        Contact = new Microsoft.OpenApi.Models.OpenApiContact
        {
            Name = "Argha Sarkar",
            Url = new Uri("https://github.com/Argha713")
        }
    });

    // Argha - 2026-02-20 - Add API key security definition so Swagger UI shows Authorize button 
    options.AddSecurityDefinition("ApiKey", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Name = "X-Api-Key",
        Description = "API key authentication. Enter your key in the field below."
    });

    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "ApiKey"
                }
            },
            Array.Empty<string>()
        }
    });
});

// Argha - 2026-02-20 - Configurable CORS: empty AllowedOrigins = allow any (dev); specify origins for production 
var corsSettings = builder.Configuration.GetSection("Cors").Get<CorsSettings>() ?? new CorsSettings();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (corsSettings.AllowedOrigins.Length == 0)
        {
            // Argha - 2026-02-20 - Wildcard CORS when no origins configured 
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        }
        else
        {
            // Argha - 2026-02-20 - Restrict to configured origins for production 
            policy.WithOrigins(corsSettings.AllowedOrigins)
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        }
    });
});

// Argha - 2026-02-20 - Fixed-window rate limiter keyed by client IP; disabled by default 
var rateLimitSettings = builder.Configuration.GetSection("RateLimit").Get<RateLimitSettings>() ?? new RateLimitSettings();
if (rateLimitSettings.Enabled)
{
    builder.Services.AddRateLimiter(options =>
    {
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            // Argha - 2026-02-20 - Health check is always exempt from rate limiting 
            if (context.Request.Path.StartsWithSegments("/api/system/health", StringComparison.OrdinalIgnoreCase))
                return RateLimitPartition.GetNoLimiter("health");

            return RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rateLimitSettings.PermitLimit,
                    Window = TimeSpan.FromSeconds(rateLimitSettings.WindowSeconds),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = rateLimitSettings.QueueLimit
                });
        });

        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        // Argha - 2026-02-20 - Return structured JSON on rate limit rejection 
        options.OnRejected = async (ctx, cancellationToken) =>
        {
            ctx.HttpContext.Response.ContentType = "application/json";
            var response = new
            {
                error = "TooManyRequests",
                message = "Rate limit exceeded. Please try again later."
            };
            await ctx.HttpContext.Response.WriteAsync(
                JsonSerializer.Serialize(response, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }), cancellationToken);
        };
    });
}

// Argha - 2026-02-20 - Register FluentValidation validators for complex request rules 
builder.Services.AddScoped<IValidator<ChatRequest>, ChatRequestValidator>();
builder.Services.AddScoped<IValidator<SearchRequest>, SearchRequestValidator>();

// Add Infrastructure services (AI, Vector Store, Document Processing)
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

// Initialize vector store
using (var scope = app.Services.CreateScope())
{
    var vectorStore = scope.ServiceProvider.GetRequiredService<IVectorStore>();
    await vectorStore.InitializeAsync();

    // Argha - 2026-02-15 - Create SQLite database if it doesn't exist 
    var dbContext = scope.ServiceProvider.GetRequiredService<RagApiDbContext>();
    await dbContext.Database.EnsureCreatedAsync();

    // Argha - 2026-02-19 - Create ConversationSessions table on existing DBs 
    // EnsureCreatedAsync only creates new DBs; this handles schema evolution without migrations
    await dbContext.Database.ExecuteSqlRawAsync(@"
        CREATE TABLE IF NOT EXISTS ConversationSessions (
            Id TEXT NOT NULL PRIMARY KEY,
            CreatedAt TEXT NOT NULL,
            LastMessageAt TEXT NOT NULL,
            Title TEXT NULL,
            MessagesJson TEXT NOT NULL
        )
    ");

    // Argha - 2026-02-19 - Add TagsJson column to existing Documents tables
    // SQLite does not support DROP COLUMN; catch and ignore if column already exists
    try
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            "ALTER TABLE Documents ADD COLUMN TagsJson TEXT NOT NULL DEFAULT '[]'");
    }
    catch { /* Column already exists — safe to ignore */ }

    // Argha - 2026-02-21 - Add UpdatedAt column to existing Documents tables 
    try
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            "ALTER TABLE Documents ADD COLUMN UpdatedAt TEXT NULL");
    }
    catch { /* Column already exists — safe to ignore */ }
}

// Argha - 2026-02-15 - Global exception handling — must be first in pipeline
app.UseMiddleware<GlobalExceptionMiddleware>();

// Argha - 2026-02-15 - Log method, path, status code, and elapsed time for every request
app.UseMiddleware<RequestLoggingMiddleware>();

// Argha - 2026-02-20 - Reject requests missing or with invalid X-Api-Key header 
app.UseMiddleware<ApiKeyMiddleware>();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "RAG API v1");
        options.RoutePrefix = string.Empty; // Serve Swagger UI at root
    });
}

app.UseCors();
app.UseAuthorization();

// Argha - 2026-02-20 - Apply rate limiting after auth; only active when Enabled=true in config 
if (rateLimitSettings.Enabled)
    app.UseRateLimiter();

app.MapControllers();

// Argha - 2026-02-15 - Real health check endpoint with dependency status 
app.MapHealthChecks("/api/system/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";

        var result = new
        {
            status = report.Status.ToString(),
            timestamp = DateTime.UtcNow,
            totalDuration = report.TotalDuration.TotalMilliseconds + "ms",
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                duration = e.Value.Duration.TotalMilliseconds + "ms",
                error = e.Value.Exception?.Message
            })
        };

        await context.Response.WriteAsync(
            JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }));
    }
});

app.Run();
