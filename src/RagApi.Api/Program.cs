using RagApi.Api.Middleware;
using RagApi.Application.Interfaces;
using RagApi.Infrastructure;

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

app.Run();
