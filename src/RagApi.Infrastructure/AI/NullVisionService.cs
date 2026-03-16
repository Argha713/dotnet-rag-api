// Argha - 2026-03-16 - #32 - Placeholder registered when Vision:Enabled=false or AI:Provider != OpenAI
using RagApi.Application.Interfaces;

namespace RagApi.Infrastructure.AI;

// Argha - 2026-03-16 - #32 - IsEnabled=false lets DocumentService (#36) skip vision processing
// without entering this class; DescribeImageAsync throws to catch misconfigured callers
internal sealed class NullVisionService : IVisionService
{
    public bool IsEnabled => false;

    public Task<string> DescribeImageAsync(
        byte[] imageBytes,
        string mimeType,
        string? context = null,
        CancellationToken ct = default)
        => throw new NotSupportedException(
            "Vision is not enabled. Set Vision:Enabled=true and AI:Provider=OpenAI to enable image description.");
}
