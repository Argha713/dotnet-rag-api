namespace RagApi.Infrastructure;

/// <summary>
/// Configuration for AI providers
/// </summary>
public class AiConfiguration
{
    public const string SectionName = "AI";
    
    /// <summary>
    /// The active AI provider: "Ollama" or "AzureOpenAI"
    /// </summary>
    public string Provider { get; set; } = "Ollama";
    
    public OllamaSettings Ollama { get; set; } = new();
    public AzureOpenAiSettings AzureOpenAI { get; set; } = new();
}

public class OllamaSettings
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string EmbeddingModel { get; set; } = "nomic-embed-text";
    public string ChatModel { get; set; } = "llama3.2";
    public int EmbeddingDimension { get; set; } = 768;
}

public class AzureOpenAiSettings
{
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string EmbeddingDeployment { get; set; } = "text-embedding-ada-002";
    public string ChatDeployment { get; set; } = "gpt-4";
    public int EmbeddingDimension { get; set; } = 1536;
}

/// <summary>
/// Configuration for Qdrant vector database
/// </summary>
public class QdrantConfiguration
{
    public const string SectionName = "Qdrant";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 6334;
    public string CollectionName { get; set; } = "documents";
    public bool UseTls { get; set; } = false;
    public string? ApiKey { get; set; }
}

// Argha - 2026-02-21 - Vector store provider switching 
/// <summary>
/// Top-level configuration for the vector store backend selection
/// </summary>
public class VectorStoreConfiguration
{
    public const string SectionName = "VectorStore";

    /// <summary>
    /// Active vector store provider: "Qdrant" (default) or "AzureAiSearch"
    /// </summary>
    public string Provider { get; set; } = "Qdrant";

    public AzureAiSearchSettings AzureAiSearch { get; set; } = new();
}

/// <summary>
/// Settings for the Azure AI Search vector store provider
/// </summary>
public class AzureAiSearchSettings
{
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string IndexName { get; set; } = "documents";
    public int EmbeddingDimension { get; set; } = 768;
}
