using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RagApi.Application.Interfaces;
using RagApi.Application.Models;
using RagApi.Domain.Entities;

namespace RagApi.Infrastructure.Data;

// Argha - 2026-03-16 - #33 - PostgreSQL-backed image store; persists raw image bytes
// as bytea rows in DocumentImages; all queries workspace-scoped via IWorkspaceContext
public class PostgresImageStore : IImageStore
{
    private readonly RagApiDbContext _dbContext;
    private readonly IWorkspaceContext _workspaceContext;
    // Argha - 2026-03-17 - #37 - NpgsqlDataSource for streaming GetStreamAsync (zero-copy path).
    // Singleton injected into Scoped service — safe; only the reverse direction is forbidden.
    private readonly NpgsqlDataSource _dataSource;

    public PostgresImageStore(
        RagApiDbContext dbContext,
        IWorkspaceContext workspaceContext,
        NpgsqlDataSource dataSource)
    {
        _dbContext = dbContext;
        _workspaceContext = workspaceContext;
        _dataSource = dataSource;
    }

    public async Task<Guid> SaveAsync(DocumentImage image, CancellationToken ct = default)
    {
        // Argha - 2026-03-16 - #33 - Bind to current workspace before persisting;
        // overwrites any caller-supplied WorkspaceId to prevent cross-tenant writes
        image.WorkspaceId = _workspaceContext.Current.Id;
        _dbContext.DocumentImages.Add(image);
        await _dbContext.SaveChangesAsync(ct);
        return image.Id;
    }

    public async Task<DocumentImage?> GetAsync(Guid id, CancellationToken ct = default)
    {
        // Argha - 2026-03-16 - #33 - WorkspaceId filter prevents cross-tenant image access
        return await _dbContext.DocumentImages
            .FirstOrDefaultAsync(
                i => i.Id == id && i.WorkspaceId == _workspaceContext.Current.Id,
                ct);
    }

    public async Task<ImageStreamResult?> GetStreamAsync(Guid id, CancellationToken ct = default)
    {
        // Argha - 2026-03-17 - #37 - OpenConnectionAsync returns an already-open connection.
        // Connection is intentionally NOT closed here — it is handed off to NpgsqlImageStream
        // and disposed by ASP.NET Core's FileStreamResult after the response is written.
        // Argha - 2026-03-17 - #39 - No workspace filter: GUID is the capability token.
        // Workspace is enforced at write time (SaveAsync); read is open-by-GUID.
        var conn = await _dataSource.OpenConnectionAsync(ct);

        var cmd = conn.CreateCommand();
        // Argha - 2026-03-17 - #37 - SequentialAccess requires columns read in SELECT order.
        // ContentType (string) MUST be read before Data (bytea stream) — never reorder.
        cmd.CommandText =
            """
            SELECT "ContentType", "Data"
            FROM "DocumentImages"
            WHERE "Id" = @id
            """;
        cmd.Parameters.AddWithValue("id", id);

        var reader = await cmd.ExecuteReaderAsync(
            CommandBehavior.SequentialAccess | CommandBehavior.SingleRow, ct);

        if (!await reader.ReadAsync(ct))
        {
            await reader.DisposeAsync();
            await cmd.DisposeAsync();
            await conn.DisposeAsync();
            return null;
        }

        // Argha - 2026-03-17 - #37 - Column 0 (ContentType) MUST be read before column 1 (Data).
        // SequentialAccess enforces strict forward-only column access.
        var contentType = reader.GetString(0);
        var bodyStream  = reader.GetStream(1);

        // Argha - 2026-03-17 - #37 - Pass NpgsqlImageStream directly — do NOT wrap in BufferedStream.
        return new ImageStreamResult(new NpgsqlImageStream(bodyStream, reader, conn, cmd), contentType);
    }

    public async Task DeleteByDocumentAsync(Guid documentId, CancellationToken ct = default)
    {
        // Argha - 2026-03-16 - #33 - Workspace filter added for defence-in-depth:
        // ensures we only delete images we own even if called with an arbitrary documentId
        var images = await _dbContext.DocumentImages
            .Where(i => i.DocumentId == documentId && i.WorkspaceId == _workspaceContext.Current.Id)
            .ToListAsync(ct);

        if (images.Count > 0)
        {
            _dbContext.DocumentImages.RemoveRange(images);
            await _dbContext.SaveChangesAsync(ct);
        }
    }

    // Argha - 2026-03-17 - #37 - Wraps the Npgsql bytea stream and owns the reader + connection.
    // Dispose() closes all four with best-effort cleanup: each resource is attempted independently
    // so a failure on one does not prevent cleanup of the others.
    // Not exposed outside PostgresImageStore.
    private sealed class NpgsqlImageStream(
        Stream inner,
        NpgsqlDataReader reader,
        NpgsqlConnection connection,
        // Argha - 2026-03-17 - #37 - cmd owned here so it is disposed in the same chain as reader + connection
        NpgsqlCommand command) : Stream
    {
        private bool _disposed;

        public override bool CanRead  => inner.CanRead;
        public override bool CanSeek  => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length   => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count)
            => inner.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
            => inner.ReadAsync(buffer, ct);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            => inner.ReadAsync(buffer, offset, count, ct);

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

        public override void SetLength(long value) => inner.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;
            if (disposing)
            {
                // Argha - 2026-03-17 - #37 - Best-effort cleanup: dispose each resource independently
                // so a failure on one does not prevent the others from being released.
                try { inner.Dispose(); } catch { /* best-effort */ }
                try { reader.Dispose(); } catch { /* best-effort */ }
                try { connection.Dispose(); } catch { /* best-effort */ }
                try { command.Dispose(); } catch { /* best-effort */ }
            }
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            // Argha - 2026-03-17 - #37 - Best-effort async cleanup in order: stream → reader → connection → command
            try { await inner.DisposeAsync(); } catch { /* best-effort */ }
            try { await reader.DisposeAsync(); } catch { /* best-effort */ }
            try { await connection.DisposeAsync(); } catch { /* best-effort */ }
            try { await command.DisposeAsync(); } catch { /* best-effort */ }
            GC.SuppressFinalize(this);
        }
    }
}
