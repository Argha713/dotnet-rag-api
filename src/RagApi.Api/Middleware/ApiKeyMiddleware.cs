using System.Text.Json;
using RagApi.Application.Interfaces;
using RagApi.Application.Services;
using RagApi.Domain.Entities;

namespace RagApi.Api.Middleware;

// Argha - 2026-02-20 - API key authentication middleware
// Argha - 2026-03-04 - #17 - Extended for workspace resolution:
//   1. Read X-Api-Key header; 401 if missing (except health check path)
//   2. SHA-256(key) → look up Workspace in DB via IWorkspaceRepository
//   3. If workspace found → set IWorkspaceContext.Current = workspace → next()
//   4. Else if key == ApiAuth:ApiKey config → set Current = default workspace → next()
//   5. Else → 401
// The health check path is always public regardless of key configuration.
// Auth is bypassed entirely when ApiAuth:ApiKey is empty (development mode).
public class ApiKeyMiddleware
{
    private const string ApiKeyHeader = "X-Api-Key";
    private const string HealthPath = "/api/system/health";
    // Argha - 2026-03-17 - #39 - Images are publicly accessible by GUID (unguessable capability token);
    // browser <img> tags cannot send custom headers so auth bypass is required
    private const string ImagesPath = "/api/images";

    private readonly RequestDelegate _next;
    private readonly string? _configuredKey;

    public ApiKeyMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _configuredKey = configuration["ApiAuth:ApiKey"];
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Argha - 2026-02-20 - Bypass when no key is configured (development mode)
        if (string.IsNullOrEmpty(_configuredKey))
        {
            // Argha - 2026-03-04 - #17 - Still resolve default workspace so controllers have a context
            var workspaceContext = context.RequestServices.GetRequiredService<IWorkspaceContext>();
            var workspaceRepo = context.RequestServices.GetRequiredService<IWorkspaceRepository>();
            var defaultWs = await workspaceRepo.GetByIdAsync(Workspace.DefaultWorkspaceId);
            workspaceContext.Current = defaultWs ?? CreateFallbackWorkspace();
            await _next(context);
            return;
        }

        // Argha - 2026-02-20 - Health check is always public
        if (context.Request.Path.StartsWithSegments(HealthPath, StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // Argha - 2026-03-17 - #39 - Images are public by GUID; no workspace context needed
        // (PostgresImageStore.GetStreamAsync queries by ID only — see #39)
        if (context.Request.Path.StartsWithSegments(ImagesPath, StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(ApiKeyHeader, out var providedKey)
            || string.IsNullOrEmpty(providedKey))
        {
            await WriteUnauthorized(context);
            return;
        }

        // Argha - 2026-03-04 - #17 - Resolve workspace from Scoped services (middleware is singleton)
        var ctx = context.RequestServices.GetRequiredService<IWorkspaceContext>();
        var repo = context.RequestServices.GetRequiredService<IWorkspaceRepository>();

        // Argha - 2026-03-04 - #17 - Check DB first: SHA-256(providedKey) → Workspace row
        var hashedKey = WorkspaceService.ComputeSha256(providedKey!);
        var workspace = await repo.GetByApiKeyHashAsync(hashedKey);

        if (workspace != null)
        {
            ctx.Current = workspace;
            await _next(context);
            return;
        }

        // Argha - 2026-03-04 - #17 - Fall back to global config key → default workspace
        if (providedKey == _configuredKey)
        {
            var defaultWorkspace = await repo.GetByIdAsync(Workspace.DefaultWorkspaceId)
                ?? CreateFallbackWorkspace();
            ctx.Current = defaultWorkspace;
            await _next(context);
            return;
        }

        await WriteUnauthorized(context);
    }

    // Argha - 2026-03-04 - #17 - In-memory fallback when DB not yet seeded (e.g. first startup before migration)
    private static Workspace CreateFallbackWorkspace() => new()
    {
        Id = Workspace.DefaultWorkspaceId,
        Name = "Default",
        HashedApiKey = string.Empty,
        CollectionName = "documents",
        CreatedAt = DateTime.UtcNow
    };

    private static async Task WriteUnauthorized(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json";

        var response = new
        {
            error = "Unauthorized",
            message = "Missing or invalid API key. Provide a valid key in the X-Api-Key header."
        };

        await context.Response.WriteAsync(
            JsonSerializer.Serialize(response, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));
    }
}
