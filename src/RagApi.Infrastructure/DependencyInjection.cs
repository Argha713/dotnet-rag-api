using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RagApi.Application.Interfaces;
using RagApi.Application.Services;
using RagApi.Infrastructure.AI;
using RagApi.Infrastructure.DocumentProcessing;
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

        // Register application services
        services.AddScoped<RagService>();
        services.AddScoped<DocumentService>();

        return services;
    }
}
