using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RagApi.Application.Interfaces;
using RagApi.Application.Models;
using RagApi.Application.Services;
using RagApi.Infrastructure.AI;
using RagApi.Infrastructure.Data;
using RagApi.Infrastructure.DocumentProcessing;
using RagApi.Infrastructure.HealthChecks;
using RagApi.Infrastructure.VectorStore;

namespace RagApi.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, 
        IConfiguration configuration)
    {
        // Bind configuration
        services.Configure<AiConfiguration>(configuration.GetSection(AiConfiguration.SectionName));
        services.Configure<QdrantConfiguration>(configuration.GetSection(QdrantConfiguration.SectionName));
        // Argha - 2026-02-20 - Register SearchOptions for hybrid search and re-ranking (Phase 3.1)
        services.Configure<SearchOptions>(configuration.GetSection(SearchOptions.SectionName));

        var aiConfig = configuration.GetSection(AiConfiguration.SectionName).Get<AiConfiguration>() 
            ?? new AiConfiguration();

        // Register AI services based on provider configuration
        if (aiConfig.Provider.Equals("AzureOpenAI", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient<IEmbeddingService, AzureOpenAiEmbeddingService>();
            services.AddHttpClient<IChatService, AzureOpenAiChatService>();
        }
        else // Default to Ollama
        {
            services.AddHttpClient<IEmbeddingService, OllamaEmbeddingService>();
            services.AddHttpClient<IChatService, OllamaChatService>();
        }

        // Register vector store
        services.AddSingleton<IVectorStore, QdrantVectorStore>();

        // Register document processor
        services.AddSingleton<IDocumentProcessor, DocumentProcessor>();

        // Argha - 2026-02-15 - SQLite persistent document storage (Phase 1.3)
        var connectionString = configuration.GetConnectionString("DefaultConnection") ?? "Data Source=ragapi.db";
        services.AddDbContext<RagApiDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<IDocumentRepository, DocumentRepository>();

        // Argha - 2026-02-19 - Conversation session repository and service (Phase 2.2)
        services.AddScoped<IConversationRepository, ConversationRepository>();

        // Register application services
        services.AddScoped<RagService>();
        services.AddScoped<DocumentService>();
        services.AddScoped<ConversationService>();

        // Argha - 2026-02-15 - Real health checks for all dependencies (Phase 1.4)
        var healthChecks = services.AddHealthChecks()
            .AddCheck<QdrantHealthCheck>("qdrant", tags: ["dependency"])
            .AddCheck<SqliteHealthCheck>("sqlite", tags: ["dependency"]);

        if (aiConfig.Provider.Equals("AzureOpenAI", StringComparison.OrdinalIgnoreCase))
        {
            // Azure OpenAI health check can be added in a future phase
        }
        else
        {
            healthChecks.AddCheck<OllamaHealthCheck>("ollama", tags: ["dependency"]);
        }

        return services;
    }
}
