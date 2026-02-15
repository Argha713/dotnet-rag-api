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
}

// Argha - 2026-02-15 - Global exception handling — must be first in pipeline
app.UseMiddleware<GlobalExceptionMiddleware>();

// Argha - 2026-02-15 - Log method, path, status code, and elapsed time for every request
app.UseMiddleware<RequestLoggingMiddleware>();

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
