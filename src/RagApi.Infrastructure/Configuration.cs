namespace RagApi.Infrastructure;

/// <summary>
/// Configuration for AI providers
/// </summary>
public class AiConfiguration
{
    public const string SectionName = "AI";

    /// <summary>
    /// The active AI provider: "Ollama", "AzureOpenAI", or "OpenAI"
    /// </summary>
    public string Provider { get; set; } = "Ollama";

    public OllamaSettings Ollama { get; set; } = new();
    public AzureOpenAiSettings AzureOpenAI { get; set; } = new();
    // Argha - 2026-03-01 - OpenAI direct API provider (Phase 7)
    public OpenAiSettings OpenAi { get; set; } = new();
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

// Argha - 2026-03-01 - OpenAI direct API settings (Phase 7)
public class OpenAiSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string ChatModel { get; set; } = "gpt-4o-mini";
    public string EmbeddingModel { get; set; } = "text-embedding-3-small";
    public int EmbeddingDimension { get; set; } = 1536;
}

// Argha - 2026-03-16 - #32 - Vision configuration for GPT-4o image description during document ingestion
public class VisionConfiguration
{
    public const string SectionName = "Vision";

    // Argha - 2026-03-18 - #52 - Default true; NullVisionService is registered when Provider != OpenAI, so local Ollama dev is unaffected
    public bool Enabled { get; set; } = true;

    // Argha - 2026-03-16 - #32 - gpt-4o-mini balances cost and quality for document image description
    public string Model { get; set; } = "gpt-4o-mini";

    // Argha - 2026-03-18 - #52 - Cap GPT-4o-mini calls per upload; ~$0.003 ceiling at 20 images
    public int MaxImagesPerDocument { get; set; } = 20;
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
