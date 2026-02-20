using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RagApi.Api.Middleware;
using RagApi.Application.Interfaces;
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

    // Argha - 2026-02-20 - Add API key security definition so Swagger UI shows Authorize button (Phase 4.1)
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

// Add CORS for development
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Add Infrastructure services (AI, Vector Store, Document Processing)
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

// Initialize vector store
using (var scope = app.Services.CreateScope())
{
    var vectorStore = scope.ServiceProvider.GetRequiredService<IVectorStore>();
    await vectorStore.InitializeAsync();

    // Argha - 2026-02-15 - Create SQLite database if it doesn't exist (Phase 1.3)
    var dbContext = scope.ServiceProvider.GetRequiredService<RagApiDbContext>();
    await dbContext.Database.EnsureCreatedAsync();

    // Argha - 2026-02-19 - Create ConversationSessions table on existing DBs (Phase 2.2)
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

    // Argha - 2026-02-19 - Add TagsJson column to existing Documents tables (Phase 2.3)
    // SQLite does not support DROP COLUMN; catch and ignore if column already exists
    try
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            "ALTER TABLE Documents ADD COLUMN TagsJson TEXT NOT NULL DEFAULT '[]'");
    }
    catch { /* Column already exists — safe to ignore */ }
}

// Argha - 2026-02-15 - Global exception handling — must be first in pipeline
app.UseMiddleware<GlobalExceptionMiddleware>();

// Argha - 2026-02-15 - Log method, path, status code, and elapsed time for every request
app.UseMiddleware<RequestLoggingMiddleware>();

// Argha - 2026-02-20 - Reject requests missing or with invalid X-Api-Key header (Phase 4.1)
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
app.MapControllers();

// Argha - 2026-02-15 - Real health check endpoint with dependency status (Phase 1.4)
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
