// Argha - 2026-03-02 - #5 - Thin seam over sealed QdrantClient to enable unit testing of resilience logic
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace RagApi.Infrastructure.VectorStore;

internal interface IQdrantClient
{
    Task<IReadOnlyList<string>> ListCollectionsAsync(CancellationToken ct = default);
    Task CreateCollectionAsync(string name, VectorParams vectorParams, CancellationToken ct = default);
    Task CreatePayloadIndexAsync(string name, string field, PayloadSchemaType schema, CancellationToken ct = default);
    Task UpsertAsync(string name, IReadOnlyList<PointStruct> points, CancellationToken ct = default);
    Task<IReadOnlyList<ScoredPoint>> SearchAsync(string name, float[] vector, ulong limit,
        Filter? filter = null, bool payloadSelector = false, bool vectorsSelector = false, CancellationToken ct = default);
    Task<ScrollResponse> ScrollAsync(string name, Filter? filter, uint limit,
        bool payloadSelector = false, CancellationToken ct = default);
    Task DeleteAsync(string name, Filter filter, CancellationToken ct = default);
    Task<CollectionInfo> GetCollectionInfoAsync(string name, CancellationToken ct = default);
}

internal sealed class QdrantClientAdapter : IQdrantClient
{
    private readonly QdrantClient _client;

    public QdrantClientAdapter(QdrantClient client) => _client = client;

    public Task<IReadOnlyList<string>> ListCollectionsAsync(CancellationToken ct = default)
        => _client.ListCollectionsAsync(ct);

    public async Task CreateCollectionAsync(string name, VectorParams vectorParams, CancellationToken ct = default)
        => await _client.CreateCollectionAsync(collectionName: name, vectorsConfig: vectorParams, cancellationToken: ct);

    public async Task CreatePayloadIndexAsync(string name, string field, PayloadSchemaType schema, CancellationToken ct = default)
        => await _client.CreatePayloadIndexAsync(collectionName: name, fieldName: field, schemaType: schema, cancellationToken: ct);

    public async Task UpsertAsync(string name, IReadOnlyList<PointStruct> points, CancellationToken ct = default)
        => await _client.UpsertAsync(collectionName: name, points: points, cancellationToken: ct);

    public Task<IReadOnlyList<ScoredPoint>> SearchAsync(string name, float[] vector, ulong limit,
        Filter? filter = null, bool payloadSelector = false, bool vectorsSelector = false, CancellationToken ct = default)
        => _client.SearchAsync(collectionName: name, vector: vector, limit: limit, filter: filter,
            payloadSelector: payloadSelector, vectorsSelector: vectorsSelector, cancellationToken: ct);

    public Task<ScrollResponse> ScrollAsync(string name, Filter? filter, uint limit,
        bool payloadSelector = false, CancellationToken ct = default)
        => _client.ScrollAsync(collectionName: name, filter: filter, limit: limit,
            payloadSelector: payloadSelector, cancellationToken: ct);

    public async Task DeleteAsync(string name, Filter filter, CancellationToken ct = default)
        => await _client.DeleteAsync(collectionName: name, filter: filter, cancellationToken: ct);

    public Task<CollectionInfo> GetCollectionInfoAsync(string name, CancellationToken ct = default)
        => _client.GetCollectionInfoAsync(name, ct);
}
