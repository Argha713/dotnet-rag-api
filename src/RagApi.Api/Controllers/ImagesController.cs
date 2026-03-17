using Microsoft.AspNetCore.Mvc;
using RagApi.Application.Interfaces;

namespace RagApi.Api.Controllers;

// Argha - 2026-03-17 - #37 - Serves stored image bytes for multimodal RAG chat responses.
// Workspace-scoped via ApiKeyMiddleware + IWorkspaceContext (set before this controller runs).
[ApiController]
[Route("api/[controller]")]
public class ImagesController : ControllerBase
{
    private readonly IImageStore _imageStore;

    public ImagesController(IImageStore imageStore) => _imageStore = imageStore;

    // Argha - 2026-03-17 - #37 - Streams image bytes. 404 if image does not exist or belongs
    // to a different workspace. Cache-Control set to 1 day — images are immutable once stored.
    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetImage(Guid id, CancellationToken ct)
    {
        var result = await _imageStore.GetStreamAsync(id, ct);
        if (result is null) return NotFound();
        // Argha - 2026-03-17 - #37 - Register disposal so the Npgsql connection is released
        // after the response is written (or on request abort), independently of FileStreamResult.
        HttpContext.Response.RegisterForDisposeAsync(result);
        Response.Headers.CacheControl = "public, max-age=86400";
        return File(result.Body, result.ContentType);
    }
}
