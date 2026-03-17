namespace RagApi.Application.Models;

// Argha - 2026-03-17 - #37 - Carries a streaming image body + content type from IImageStore.GetStreamAsync.
// Body is NpgsqlImageStream (Infrastructure); disposing Body closes the reader and Npgsql connection.
// Pass Body directly to File() — do not wrap in BufferedStream or the connection will not be disposed.
// Argha - 2026-03-17 - #37 - IAsyncDisposable added so RegisterForDisposeAsync in ImagesController
// can register this for cleanup after the HTTP response is written.
public record ImageStreamResult(Stream Body, string ContentType) : IDisposable, IAsyncDisposable
{
    public void Dispose() => Body.Dispose();

    public ValueTask DisposeAsync() => Body.DisposeAsync();
}
