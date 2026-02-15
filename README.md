# dotnet-rag-api

A production-ready **Retrieval-Augmented Generation (RAG)** API built with **.NET 8**. Upload documents, ask questions, and get AI-powered answers with source citations.

> Most RAG examples are Python-only. This project brings RAG to the .NET ecosystem with enterprise-grade architecture.

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=flat&logo=dotnet)
![C#](https://img.shields.io/badge/C%23-12-239120?style=flat&logo=csharp)
![License](https://img.shields.io/badge/License-MIT-green.svg)

---

## Features

- **Multi-format document support** — PDF, DOCX, TXT, Markdown
- **Semantic search** — Find relevant content using vector similarity
- **RAG-powered chat** — Get AI answers grounded in your documents
- **Source citations** — Every answer includes references to source documents
- **Dual AI provider support** — Ollama (local) or Azure OpenAI (cloud)
- **Docker-ready** — One command to spin up all dependencies
- **Swagger UI** — Interactive API documentation at the root URL

---

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                         API Layer                               │
│                    (ASP.NET Core Controllers)                   │
├─────────────────────────────────────────────────────────────────┤
│                      Application Layer                          │
│              (RagService, DocumentService)                      │
├─────────────────────────────────────────────────────────────────┤
│                     Infrastructure Layer                        │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────────┐  │
│  │   Ollama    │  │   Qdrant    │  │   Document Processor    │  │
│  │  Azure AI   │  │ VectorStore │  │   (PDF, DOCX, TXT)      │  │
│  └─────────────┘  └─────────────┘  └─────────────────────────┘  │
├─────────────────────────────────────────────────────────────────┤
│                        Domain Layer                             │
│            (Entities, Interfaces, Value Objects)                │
└─────────────────────────────────────────────────────────────────┘
```

---

## Prerequisites

Before you begin, make sure you have the following installed:

- [**.NET 8 SDK**](https://dotnet.microsoft.com/download/dotnet/8.0) (or later) — verify with `dotnet --version`
- [**Docker Desktop**](https://www.docker.com/get-started) — must be **running** (not just installed)
- **~8 GB free RAM** — Ollama models require 4-8 GB of memory

---

## Quick Start

### 1. Clone the Repository

```bash
git clone https://github.com/Argha713/dotnet-rag-api.git
cd dotnet-rag-api
```

### 2. Start Docker Services

Make sure **Docker Desktop is running**, then:

```bash
docker-compose up -d
```

This starts:
- **Qdrant** (vector database) on ports 6333 and 6334
- **Ollama** (local LLM) on port 11434

### 3. Pull Ollama Models (first time only)

```bash
docker exec rag-ollama ollama pull nomic-embed-text
docker exec rag-ollama ollama pull llama3.2
```

> **Note:** This downloads ~2.5 GB of model files. It only needs to be done once — the models persist in a Docker volume.

### 4. Run the API

```bash
dotnet run --project src/RagApi.Api
```

### 5. Open Swagger UI

Navigate to **http://localhost:5000** in your browser to explore the API interactively.

### Verify Everything is Working

```bash
# Health check — should return {"status":"healthy"}
curl http://localhost:5000/api/system/health

# System stats — shows connected models and vector store
curl http://localhost:5000/api/system/stats
```

> **Tip:** The first request after starting may be slow (10-30 seconds) as Ollama loads models into memory. Subsequent requests are much faster.

---

### Automated Setup (Alternative)

Instead of steps 2-3 above, you can use the setup scripts that handle prerequisites checking, Docker services, and model pulling:

```bash
# Linux / macOS
chmod +x setup.sh && ./setup.sh

# Windows
setup.bat
```

---

## API Endpoints

### Documents

| Method | Endpoint | Description |
|--------|----------|-------------|
| `POST` | `/api/documents` | Upload a document (PDF, DOCX, TXT, MD) |
| `GET` | `/api/documents` | List all documents |
| `GET` | `/api/documents/{id}` | Get document by ID |
| `DELETE` | `/api/documents/{id}` | Delete a document and its vector data |
| `GET` | `/api/documents/supported-types` | List supported file types |

### Chat

| Method | Endpoint | Description |
|--------|----------|-------------|
| `POST` | `/api/chat` | Ask a question (RAG-powered) |
| `POST` | `/api/chat/search` | Semantic search only (no LLM) |

### System

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/system/health` | Health check |
| `GET` | `/api/system/stats` | System statistics |

---

## Usage Example

### 1. Upload a Document

```bash
curl -X POST http://localhost:5000/api/documents \
  -F "file=@./sample.pdf"
```

### 2. Ask a Question

```bash
curl -X POST http://localhost:5000/api/chat \
  -H "Content-Type: application/json" \
  -d '{
    "query": "What are the main points discussed in the document?",
    "topK": 5
  }'
```

### Response

```json
{
  "answer": "Based on the document, the main points are...",
  "sources": [
    {
      "documentId": "abc-123",
      "fileName": "sample.pdf",
      "relevantText": "The document discusses...",
      "relevanceScore": 0.89
    }
  ],
  "model": "llama3.2"
}
```

### 3. Semantic Search (without LLM)

```bash
curl -X POST http://localhost:5000/api/chat/search \
  -H "Content-Type: application/json" \
  -d '{"query": "main topic", "topK": 3}'
```

---

## Configuration

### Default Configuration (Ollama)

The API uses Ollama by default. Configuration is in `src/RagApi.Api/appsettings.json`:

```json
{
  "AI": {
    "Provider": "Ollama",
    "Ollama": {
      "BaseUrl": "http://localhost:11434",
      "EmbeddingModel": "nomic-embed-text",
      "ChatModel": "llama3.2",
      "EmbeddingDimension": 768
    }
  },
  "Qdrant": {
    "Host": "localhost",
    "Port": 6334,
    "CollectionName": "documents"
  }
}
```

### Switch to Azure OpenAI

Change the `Provider` and fill in your Azure details:

```json
{
  "AI": {
    "Provider": "AzureOpenAI",
    "AzureOpenAI": {
      "Endpoint": "https://YOUR-RESOURCE.openai.azure.com",
      "ApiKey": "YOUR-API-KEY",
      "EmbeddingDeployment": "text-embedding-ada-002",
      "ChatDeployment": "gpt-4",
      "EmbeddingDimension": 1536
    }
  }
}
```

### Environment Variables

All settings can be overridden with environment variables (using `__` as separator):

```bash
AI__Provider=AzureOpenAI
AI__AzureOpenAI__Endpoint=https://...
AI__AzureOpenAI__ApiKey=your-key
Qdrant__Host=localhost
Qdrant__Port=6334
```

---

## Tech Stack

| Component | Technology |
|-----------|------------|
| **Framework** | .NET 8, ASP.NET Core |
| **AI (Local)** | Ollama (llama3.2, nomic-embed-text) |
| **AI (Cloud)** | Azure OpenAI |
| **Vector DB** | Qdrant |
| **PDF Parsing** | PdfPig |
| **DOCX Parsing** | DocumentFormat.OpenXml |
| **API Docs** | Swagger / OpenAPI |

---

## Project Structure

```
dotnet-rag-api/
├── src/
│   ├── RagApi.Api/              # Web API (Controllers, DTOs)
│   ├── RagApi.Application/      # Business logic, Service interfaces
│   ├── RagApi.Domain/           # Core entities
│   └── RagApi.Infrastructure/   # External services (Qdrant, Ollama, Azure)
├── tests/
│   └── RagApi.Tests/            # Unit & integration tests
├── docker-compose.yml           # Qdrant + Ollama containers
├── Dockerfile                   # Production container build
├── setup.sh / setup.bat         # Automated setup scripts
└── RagApi.sln                   # Solution file
```

---

## Troubleshooting

| Problem | Solution |
|---------|----------|
| **"Connection refused" to Ollama/Qdrant** | Make sure Docker Desktop is running: `docker ps` should show `rag-ollama` and `rag-qdrant` |
| **"Collection not found" in Qdrant** | The collection auto-creates on first API startup. Restart the API. |
| **First request is very slow** | Normal — Ollama loads the model into memory on first use. Subsequent requests are faster. |
| **Out of memory errors** | Ollama models need ~4-8 GB RAM. Try a smaller model: change `ChatModel` to `llama3.2:1b` in `appsettings.json` |
| **Port already in use** | Stop other services on port 5000, or change the port in `Properties/launchSettings.json` |

---

## Roadmap

### Phase 1: Foundation & Production Readiness
- [x] Global exception handling middleware
- [x] Request/response logging
- [ ] Persistent document storage (SQLite + EF Core)
- [ ] Real health checks (Qdrant + Ollama connectivity)
- [ ] Unit & integration tests

### Phase 2: Core Features
- [ ] Streaming chat responses (SSE)
- [ ] Conversation memory with server-side sessions
- [ ] Document metadata & tag filtering

### Phase 3: Search Improvements
- [ ] Hybrid search (keyword + semantic)
- [ ] Search result re-ranking
- [ ] Configurable chunking strategies

### Phase 4: Security & API Management
- [ ] API key authentication
- [ ] Rate limiting
- [ ] Production CORS configuration
- [ ] Input validation (FluentValidation)

### Phase 5: Advanced Features
- [ ] Azure AI Search integration
- [ ] Batch document upload
- [ ] Document update & re-process
- [ ] Export conversation history

### Phase 6: Frontend & DevOps
- [ ] Structured logging (Serilog)
- [ ] GitHub Actions CI/CD
- [ ] Blazor WebAssembly chat UI
- [ ] Full Docker Compose (API + UI + services)

---

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

---

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

---

## Author

**Argha Sarkar**

- LinkedIn: [argha-sarkar](https://www.linkedin.com/in/argha-sarkar-12538a21a)
- GitHub: [@Argha713](https://github.com/Argha713)

---

If you found this project helpful, please give it a star!
