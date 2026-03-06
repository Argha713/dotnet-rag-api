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
using RagApi.Infrastructure.Services;
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
        // Argha - 2026-02-21 - Register VectorStoreConfiguration for provider switching 
        services.Configure<VectorStoreConfiguration>(configuration.GetSection(VectorStoreConfiguration.SectionName));
        // Argha - 2026-02-21 - Register BatchUploadOptions for concurrent batch document upload 
        services.Configure<BatchUploadOptions>(configuration.GetSection(BatchUploadOptions.SectionName));

        var aiConfig = configuration.GetSection(AiConfiguration.SectionName).Get<AiConfiguration>() 
            ?? new AiConfiguration();

        // Register AI services based on provider configuration
        if (aiConfig.Provider.Equals("AzureOpenAI", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient<IEmbeddingService, AzureOpenAiEmbeddingService>();
            services.AddHttpClient<IChatService, AzureOpenAiChatService>();
        }
        // Argha - 2026-03-01 - OpenAI direct API provider (Phase 7): no extra compute cost
        else if (aiConfig.Provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient<IEmbeddingService, OpenAiEmbeddingService>();
            services.AddHttpClient<IChatService, OpenAiChatService>();
            // No Ollama health check needed; OpenAI is a managed external service
        }
        else // Default to Ollama
        {
            services.AddHttpClient<IEmbeddingService, OllamaEmbeddingService>();
            services.AddHttpClient<IChatService, OllamaChatService>();
        }

        // Argha - 2026-02-21 - Switch vector store backend based on VectorStore:Provider
        var vectorStoreConfig = configuration.GetSection(VectorStoreConfiguration.SectionName)
            .Get<VectorStoreConfiguration>() ?? new VectorStoreConfiguration();

        if (vectorStoreConfig.Provider.Equals("AzureAiSearch", StringComparison.OrdinalIgnoreCase))
        {
            var azSettings = vectorStoreConfig.AzureAiSearch;
            services.AddSingleton(_ =>
                new SearchIndexClient(
                    new Uri(azSettings.Endpoint),
                    new AzureKeyCredential(azSettings.ApiKey)));
            // Argha - 2026-03-04 - #17 - SearchClient no longer registered as singleton;
            // AzureAiSearchVectorStore creates per-collection clients from SearchIndexClient
            services.AddSingleton<IVectorStore, AzureAiSearchVectorStore>();
        }
        else
        {
            // Default: Qdrant (local / self-hosted)
            services.AddSingleton<IVectorStore, QdrantVectorStore>();
        }

        // Register document processor
        services.AddSingleton<IDocumentProcessor, DocumentProcessor>();

        // Argha - 2026-03-02 - #6 - PostgreSQL replaces SQLite for persistent cross-deployment storage
        // EnableLegacyTimestampBehavior treats all DateTime as timestamp without time zone (no UTC conversion)
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required");
        services.AddDbContext<RagApiDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IDocumentRepository, DocumentRepository>();

        // Argha - 2026-02-19 - Conversation session repository and service
        services.AddScoped<IConversationRepository, ConversationRepository>();

        // Argha - 2026-03-04 - #17 - Workspace repository and context (Scoped per request)
        services.AddScoped<IWorkspaceRepository, WorkspaceRepository>();
        services.AddScoped<IWorkspaceContext, WorkspaceContext>();

        // Register application services
        services.AddScoped<RagService>();
        services.AddScoped<DocumentService>();
        services.AddScoped<ConversationService>();
        // Argha - 2026-02-21 - Conversation export service registered
        services.AddScoped<ConversationExportService>();
        // Argha - 2026-03-04 - #17 - Workspace service for workspace lifecycle management
        services.AddScoped<WorkspaceService>();

        // Argha - 2026-03-02 - #6 - PostgreSQL health check replaces SqliteHealthCheck
        var healthChecks = services.AddHealthChecks()
            .AddCheck<PostgresHealthCheck>("postgres", tags: ["dependency"]);

        // Argha - 2026-02-21 - Register vector store health check based on active provider 
        if (vectorStoreConfig.Provider.Equals("AzureAiSearch", StringComparison.OrdinalIgnoreCase))
        {
            healthChecks.AddCheck<AzureAiSearchHealthCheck>("azure-ai-search", tags: ["dependency"]);
        }
        else
        {
            healthChecks.AddCheck<QdrantHealthCheck>("qdrant", tags: ["dependency"]);
        }

        // Argha - 2026-03-07 - #22 - Register AI provider health check based on active provider
        if (aiConfig.Provider.Equals("AzureOpenAI", StringComparison.OrdinalIgnoreCase))
            healthChecks.AddCheck<AzureOpenAiHealthCheck>("azure-openai", tags: ["dependency"]);
        else if (aiConfig.Provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
            healthChecks.AddCheck<OpenAiHealthCheck>("openai", tags: ["dependency"]);
        else
            healthChecks.AddCheck<OllamaHealthCheck>("ollama", tags: ["dependency"]);

        return services;
    }
}
