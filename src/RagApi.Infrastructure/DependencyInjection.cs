using Azure;
using Azure.Search.Documents.Indexes;
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
        // Argha - 2026-02-20 - Register SearchOptions for hybrid search and re-ranking 
        services.Configure<SearchOptions>(configuration.GetSection(SearchOptions.SectionName));
        // Argha - 2026-02-20 - Register DocumentProcessingOptions for configurable chunking 
        services.Configure<DocumentProcessingOptions>(configuration.GetSection(DocumentProcessingOptions.SectionName));
        // Argha - 2026-02-21 - Register VectorStoreConfiguration for provider switching (Phase 5.1)
        services.Configure<VectorStoreConfiguration>(configuration.GetSection(VectorStoreConfiguration.SectionName));

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

        // Argha - 2026-02-21 - Switch vector store backend based on VectorStore:Provider (Phase 5.1)
        var vectorStoreConfig = configuration.GetSection(VectorStoreConfiguration.SectionName)
            .Get<VectorStoreConfiguration>() ?? new VectorStoreConfiguration();

        if (vectorStoreConfig.Provider.Equals("AzureAiSearch", StringComparison.OrdinalIgnoreCase))
        {
            var azSettings = vectorStoreConfig.AzureAiSearch;
            services.AddSingleton(_ =>
                new SearchIndexClient(
                    new Uri(azSettings.Endpoint),
                    new AzureKeyCredential(azSettings.ApiKey)));
            services.AddSingleton(sp =>
                sp.GetRequiredService<SearchIndexClient>()
                  .GetSearchClient(azSettings.IndexName));
            services.AddSingleton<IVectorStore, AzureAiSearchVectorStore>();
        }
        else
        {
            // Default: Qdrant (local / self-hosted)
            services.AddSingleton<IVectorStore, QdrantVectorStore>();
        }

        // Register document processor
        services.AddSingleton<IDocumentProcessor, DocumentProcessor>();

        // Argha - 2026-02-15 - SQLite persistent document storage 
        var connectionString = configuration.GetConnectionString("DefaultConnection") ?? "Data Source=ragapi.db";
        services.AddDbContext<RagApiDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<IDocumentRepository, DocumentRepository>();

        // Argha - 2026-02-19 - Conversation session repository and service 
        services.AddScoped<IConversationRepository, ConversationRepository>();

        // Register application services
        services.AddScoped<RagService>();
        services.AddScoped<DocumentService>();
        services.AddScoped<ConversationService>();

        // Argha - 2026-02-15 - Real health checks for all dependencies 
        var healthChecks = services.AddHealthChecks()
            .AddCheck<SqliteHealthCheck>("sqlite", tags: ["dependency"]);

        // Argha - 2026-02-21 - Register vector store health check based on active provider (Phase 5.1)
        if (vectorStoreConfig.Provider.Equals("AzureAiSearch", StringComparison.OrdinalIgnoreCase))
        {
            healthChecks.AddCheck<AzureAiSearchHealthCheck>("azure-ai-search", tags: ["dependency"]);
        }
        else
        {
            healthChecks.AddCheck<QdrantHealthCheck>("qdrant", tags: ["dependency"]);
        }

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
