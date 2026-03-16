# dotnet-rag-api

A production-ready **Retrieval-Augmented Generation (RAG) API** built with **.NET 8**. Upload documents, ask questions, and get AI-powered answers grounded in your content — with source citations.

> Most RAG examples are Python-only. This project brings RAG to the .NET ecosystem with enterprise-grade Clean Architecture.

[![CI](https://github.com/Argha713/dotnet-rag-api/actions/workflows/ci.yml/badge.svg)](https://github.com/Argha713/dotnet-rag-api/actions/workflows/ci.yml)
[![Deploy](https://github.com/Argha713/dotnet-rag-api/actions/workflows/deploy.yml/badge.svg)](https://github.com/Argha713/dotnet-rag-api/actions/workflows/deploy.yml)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=flat&logo=dotnet)
![C#](https://img.shields.io/badge/C%23-12-239120?style=flat&logo=csharp)
![Tests](https://img.shields.io/badge/tests-287%20passing-brightgreen)
![Phase](https://img.shields.io/badge/phase-14.1%20IVisionService%20%2B%20IImageStore-brightgreen)
![License](https://img.shields.io/badge/License-MIT-green.svg)

---

## Live Demo

| | URL |
|---|---|
| **Blazor Chat UI** | https://ambitious-glacier-0b62ea10f.6.azurestaticapps.net |
| **REST API** | https://rag-api.calmsand-4a05cfa0.eastus.azurecontainerapps.io |
| **Health Check** | https://rag-api.calmsand-4a05cfa0.eastus.azurecontainerapps.io/api/system/health |

---

## Features

### Document Processing
- **Multi-format ingestion** — PDF, DOCX, TXT, Markdown
- **Configurable chunking** — Fixed, Sentence, or Paragraph strategy per upload
- **Batch upload** — Process 1–20 documents in a single request
- **Document update & re-index** — Replace content in-place via `PUT /api/documents/{id}`; preserves ID and updates vector index
- **Tag-based filtering** — Tag documents on upload; scope retrieval to specific tags

### Search & Retrieval
- **Semantic search** — Vector similarity via Qdrant
- **Hybrid search** — Combines vector similarity with full-text keyword search using Reciprocal Rank Fusion (RRF)
- **MMR re-ranking** — Maximal Marginal Relevance reorders results to reduce redundancy
- **Source citations** — Every answer includes references with relevance scores

### AI Providers
- **OpenAI** — `gpt-4o-mini` + `text-embedding-3-small` (production default, lowest cost)
- **Azure OpenAI** — Any deployed model via your Azure resource
- **Ollama** — Local models (`llama3.2`, `nomic-embed-text`) for offline/private use

### Conversations
- **Streaming responses** — Real-time answers via Server-Sent Events (SSE)
- **Conversation memory** — Server-side session management with full message history
- **Export history** — Download any session as JSON, Markdown, or plain text

### Multi-tenancy / Workspaces ✅
- **Isolated workspaces** — Each workspace has its own Qdrant collection + scoped PostgreSQL rows; zero cross-tenant data leakage
- **Per-workspace API keys** — 32-byte random hex key; SHA-256 hash stored; plaintext shown once on creation
- **Workspace CRUD** — `POST /api/workspaces` (create), `GET /api/workspaces/{id}` (get), `GET /api/workspaces/current` (resolve from key), `DELETE /api/workspaces/{id}` (cascade delete)
- **Cascade delete** — Deleting a workspace removes its Qdrant collection, all documents, and all conversations atomically
- **Backward compatibility** — Default workspace (`documents` collection) maps to the global `ApiAuth:ApiKey` config key

### Workspace UI ✅
- **Workspaces page** — Create, switch, delete, and import workspaces from `/workspaces`
- **Navbar chip** — Active workspace shown as indigo pill in the top navigation bar; amber "Get Started →" badge shown to new users
- **Onboarding guide** — 4-step setup instructions shown in the Workspaces empty state; step 2 highlighted in amber to warn about one-time API key
- **Workspace gate** — Documents and Chat pages blocked behind a lock screen until at least one workspace is created
- **No-documents banner** — Chat panel shows an info banner and disables the textarea when the active workspace has no documents uploaded
- **Server-side conversation list** — Sidebar loads from `GET /api/conversations` (newest-first); cross-device visible on workspace import ✅
- **Per-workspace chat history** — Conversation list scoped by workspace; switches automatically on workspace change
- **API key modal** — One-time display with clipboard copy; Done button re-enabled after 10 s if clipboard is blocked
- **Import flow** — Paste an existing API key to link a workspace to this browser session (validates via `GET /api/documents`, resolves ID via `GET /api/workspaces/current`)
- **Dynamic key injection** — `WorkspaceKeyHandler` DelegatingHandler replaces the static `X-Api-Key` header on every outgoing request

### Security & Reliability
- **API key authentication** — Protect all endpoints via `X-Api-Key` header; resolves workspace from DB hash lookup
- **Rate limiting** — Configurable fixed-window limiter keyed by IP; health check always exempt
- **Production CORS** — Configurable allowed origins; wildcard in development
- **Input validation** — FluentValidation with structured error responses
- **Global error handling** — Exception middleware with JSON error responses
- **Structured logging** — Serilog with rolling daily files, per-request correlation IDs (`X-Correlation-ID`)
- **Real health checks** — Per-dependency status for Qdrant, Ollama/OpenAI, and PostgreSQL
- **Qdrant auto-reinitialize** — Recovers automatically if the collection is deleted externally; retries once without downtime

### Infrastructure
- **Clean Architecture** — Domain → Application → Infrastructure → API
- **Dual vector store** — Qdrant (local or cloud) or Azure AI Search
- **PostgreSQL + EF Core migrations** — Persistent document metadata survives container restarts; Neon free tier in production
- **Docker Compose** — One command to spin up all local dependencies (API + UI + Qdrant + Ollama + PostgreSQL)
- **GitHub Actions CI/CD** — Automated test, build, and deploy pipeline
- **Azure deployment** — Container Apps (scales to zero) + Static Web Apps (free tier)
- **Modern SaaS UI ✅** — Inter design system, indigo theme, drag-drop uploads, glassmorphism health dashboard, footer
- **287 unit tests** — xUnit + Moq + FluentAssertions across all layers

---

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                         API Layer                               │
│              (ASP.NET Core Controllers, Middleware)             │
├─────────────────────────────────────────────────────────────────┤
│                      Application Layer                          │
│              (RagService, DocumentService)                      │
├─────────────────────────────────────────────────────────────────┤
│                     Infrastructure Layer                        │
│  ┌──────────────────┐  ┌─────────────┐  ┌────────────────────┐  │
│  │  OpenAI / Azure  │  │   Qdrant /  │  │ Document Processor │  │
│  │  OpenAI / Ollama │  │  AI Search  │  │ (PDF, DOCX, TXT)   │  │
│  └──────────────────┘  └─────────────┘  └────────────────────┘  │
├─────────────────────────────────────────────────────────────────┤
│                        Domain Layer                             │
│            (Entities, Interfaces, Value Objects)                │
└─────────────────────────────────────────────────────────────────┘
```

### RAG Pipeline

`POST /api/chat` → `RagService.ChatAsync()`:
1. Generate query embedding via `IEmbeddingService`
2. Hybrid search: vector similarity + full-text via `IVectorStore`
3. MMR re-ranking to reduce redundancy
4. Build context from top-K chunks
5. Call `IChatService.GenerateResponseAsync()` with system prompt + context
6. Return answer with source citations

---

## Quick Start (Local)

**Prerequisites:** .NET 8 SDK, Docker Desktop

```bash
git clone https://github.com/Argha713/dotnet-rag-api.git
cd dotnet-rag-api

# Start Qdrant + Ollama
docker-compose up -d

# Pull models (first time only — ~2.5 GB)
docker exec rag-ollama ollama pull nomic-embed-text
docker exec rag-ollama ollama pull llama3.2

# Run the API (Swagger UI at http://localhost:5000)
dotnet run --project src/RagApi.Api

# Optional: run the Blazor chat UI
dotnet run --project src/RagApi.BlazorUI
```

Verify with:
```bash
curl http://localhost:5000/api/system/health
```

> **Tip:** Use `setup.sh` (Linux/macOS) or `setup.bat` (Windows) to automate Docker + model setup.

---

## API Reference

### Documents

| Method | Endpoint | Description |
|--------|----------|-------------|
| `POST` | `/api/documents` | Upload a document; optional `tags` form fields |
| `POST` | `/api/documents/batch` | Upload 1–20 documents in one request |
| `GET` | `/api/documents` | List all documents; optional `?tag=` filter |
| `GET` | `/api/documents/{id}` | Get document by ID |
| `PUT` | `/api/documents/{id}` | Replace document content and re-index |
| `DELETE` | `/api/documents/{id}` | Delete document and its vector data |
| `GET` | `/api/documents/supported-types` | List supported file types |

### Chat

| Method | Endpoint | Description |
|--------|----------|-------------|
| `POST` | `/api/chat` | RAG-powered question answering |
| `POST` | `/api/chat/search` | Semantic search only (no LLM) |

### Conversations

| Method | Endpoint | Description |
|--------|----------|-------------|
| `POST` | `/api/conversations` | Create a new session |
| `GET` | `/api/conversations/{id}` | Get session with full message history |
| `GET` | `/api/conversations/{id}/export` | Export as JSON, Markdown, or text |
| `DELETE` | `/api/conversations/{id}` | Delete a session |

### Workspaces

| Method | Endpoint | Description |
|--------|----------|-------------|
| `POST` | `/api/workspaces` | Create workspace; returns plaintext API key (shown once) |
| `GET` | `/api/workspaces/current` | Resolve current workspace from X-Api-Key header |
| `GET` | `/api/workspaces/{id}` | Get workspace metadata |
| `DELETE` | `/api/workspaces/{id}` | Cascade delete workspace, documents, and vector data |

### System

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/system/health` | Per-dependency health status |
| `GET` | `/api/system/stats` | Connected models and vector store info |

---

## Usage Example

```bash
# Upload a document
curl -X POST http://localhost:5000/api/documents \
  -F "file=@./report.pdf" \
  -F "tags=finance" \
  -F "tags=q4"

# Ask a question (scoped to tag)
curl -X POST http://localhost:5000/api/chat \
  -H "Content-Type: application/json" \
  -d '{
    "query": "What were the key findings?",
    "topK": 5,
    "tags": ["finance"]
  }'
```

**Response:**
```json
{
  "answer": "Based on the Q4 report, the key findings are...",
  "sources": [
    {
      "documentId": "abc-123",
      "fileName": "report.pdf",
      "relevantText": "The report highlights...",
      "relevanceScore": 0.91
    }
  ],
  "model": "gpt-4o-mini"
}
```

---

## Configuration

### AI Provider

Set `AI__Provider` to `OpenAI`, `AzureOpenAI`, or `Ollama`:

```bash
# OpenAI (production default)
AI__Provider=OpenAI
AI__OpenAi__ApiKey=sk-...
AI__OpenAi__ChatModel=gpt-4o-mini
AI__OpenAi__EmbeddingModel=text-embedding-3-small

# Azure OpenAI
AI__Provider=AzureOpenAI
AI__AzureOpenAI__Endpoint=https://YOUR-RESOURCE.openai.azure.com
AI__AzureOpenAI__ApiKey=your-key

# Ollama (local)
AI__Provider=Ollama
AI__Ollama__BaseUrl=http://localhost:11434
```

### Qdrant Cloud

```bash
Qdrant__Host=your-cluster.aws.cloud.qdrant.io
Qdrant__Port=6334
Qdrant__UseTls=true
Qdrant__ApiKey=your-qdrant-api-key
```

### Security

```bash
ApiAuth__ApiKey=your-strong-api-key        # empty = auth disabled
RateLimit__Enabled=true
RateLimit__PermitLimit=10
RateLimit__WindowSeconds=60
Cors__AllowedOrigins__0=https://your-frontend.azurestaticapps.net
```

> **Cost:** OpenAI gpt-4o-mini + text-embedding-3-small costs ~$0.20–1.50/month for 50 visitors at 10 queries each.

---

## Tech Stack

| Component | Technology |
|-----------|------------|
| **Framework** | .NET 8, ASP.NET Core |
| **AI (Cloud)** | OpenAI API (gpt-4o-mini), Azure OpenAI |
| **AI (Local)** | Ollama (llama3.2, nomic-embed-text) |
| **Vector DB** | Qdrant (local or cloud) |
| **Vector DB (Cloud)** | Azure AI Search |
| **Document Parsing** | PdfPig, DocumentFormat.OpenXml |
| **Database** | PostgreSQL (Neon) + Entity Framework Core |
| **Logging** | Serilog (Console + File sinks) |
| **Frontend** | Blazor WebAssembly (.NET 8) — Inter design system, indigo theme |
| **Hosting** | Azure Container Apps + Azure Static Web Apps |
| **CI/CD** | GitHub Actions → GHCR → Azure |
| **Testing** | xUnit, Moq, FluentAssertions (273 tests) |
| **API Docs** | Swagger / OpenAPI |

---

## Project Structure

```
dotnet-rag-api/
├── src/
│   ├── RagApi.Api/              # Controllers, DTOs, Middleware
│   ├── RagApi.Application/      # Interfaces, RagService, DocumentService
│   ├── RagApi.BlazorUI/         # Blazor WebAssembly chat UI
│   ├── RagApi.Domain/           # Core entities
│   └── RagApi.Infrastructure/   # Qdrant, OpenAI, Azure, EF Core
├── tests/
│   └── RagApi.Tests/            # 273 unit tests
├── .github/workflows/           # CI, Deploy API, Deploy UI
├── docker-compose.yml           # Local Qdrant + Ollama + PostgreSQL
├── Dockerfile                   # Production container image
└── RagApi.sln
```

---

## Troubleshooting

| Problem | Solution |
|---------|----------|
| **"Connection refused" to Ollama/Qdrant** | Run `docker ps` — both `rag-ollama` and `rag-qdrant` must be running |
| **"Collection not found" in Qdrant** | The API auto-reinitializes the collection and retries — no restart needed. |
| **First request is slow** | Ollama loads the model on first use (~10–30s). Subsequent requests are fast. |
| **Out of memory** | Ollama needs ~4–8 GB RAM. Switch to `llama3.2:1b` in `appsettings.json`. |
| **Port conflict** | Change the port in `src/RagApi.Api/Properties/launchSettings.json`. |

---

## Contributing

Contributions are welcome. Please open an issue before submitting a pull request for significant changes.

---

## License

MIT — see [LICENSE](LICENSE) for details.

---

## Author

**Argha Sarkar**

- LinkedIn: [argha-sarkar](https://www.linkedin.com/in/argha-sarkar-12538a21a)
- GitHub: [@Argha713](https://github.com/Argha713)

---

If you found this project helpful, please consider giving it a star!
