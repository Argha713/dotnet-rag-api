// Argha - 2026-03-04 - #17 - Unit tests for WorkspaceStateService: localStorage persistence and state management
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.JSInterop;
using RagApi.BlazorUI.Models;
using RagApi.BlazorUI.Services;
using System.Text.Json;

namespace RagApi.Tests.Unit.Services;

public class WorkspaceStateServiceTests
{
    private static readonly JsonSerializerOptions _opts = new(JsonSerializerDefaults.Web);

    private static WorkspaceStateService CreateSut(out FakeJSRuntime fakeJs, string? fallbackApiKey = null)
    {
        fakeJs = new FakeJSRuntime();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { { "ApiKey", fallbackApiKey ?? string.Empty } })
            .Build();
        return new WorkspaceStateService(fakeJs, config);
    }

    [Fact]
    public async Task InitializeAsync_LoadsFromLocalStorage()
    {
        var sut = CreateSut(out var fakeJs);
        var stored = new List<StoredWorkspace>
        {
            new() { Id = Guid.NewGuid(), Name = "Acme", ApiKey = "key123", CreatedAt = DateTime.UtcNow }
        };
        fakeJs.SetItem("rag_workspaces", JsonSerializer.Serialize(stored, _opts));
        fakeJs.SetItem("rag_active_workspace_id", stored[0].Id.ToString());

        await sut.InitializeAsync();

        sut.Workspaces.Should().HaveCount(1);
        sut.Workspaces[0].Name.Should().Be("Acme");
        sut.Active.Should().NotBeNull();
        sut.Active!.Id.Should().Be(stored[0].Id);
        sut.ApiKey.Should().Be("key123");
    }

    [Fact]
    public async Task AddWorkspaceAsync_AppendsAndPersists()
    {
        var sut = CreateSut(out var fakeJs);
        await sut.InitializeAsync();

        var ws = new StoredWorkspace { Id = Guid.NewGuid(), Name = "New Corp", ApiKey = "k", CreatedAt = DateTime.UtcNow };
        await sut.AddWorkspaceAsync(ws);

        sut.Workspaces.Should().HaveCount(1);
        sut.Workspaces[0].Name.Should().Be("New Corp");
        fakeJs.GetItem("rag_workspaces").Should().Contain("New Corp");
    }

    [Fact]
    public async Task SetActiveAsync_UpdatesActiveAndFiresEvent()
    {
        var sut = CreateSut(out _);
        await sut.InitializeAsync();
        var ws = new StoredWorkspace { Id = Guid.NewGuid(), Name = "Corp", ApiKey = "k", CreatedAt = DateTime.UtcNow };
        await sut.AddWorkspaceAsync(ws);

        bool eventFired = false;
        sut.OnChanged += () => eventFired = true;
        await sut.SetActiveAsync(ws.Id);

        sut.Active.Should().NotBeNull();
        sut.Active!.Id.Should().Be(ws.Id);
        eventFired.Should().BeTrue();
    }

    [Fact]
    public async Task RemoveWorkspaceAsync_ClearsActiveWhenRemoved()
    {
        var sut = CreateSut(out _);
        await sut.InitializeAsync();
        var ws = new StoredWorkspace { Id = Guid.NewGuid(), Name = "ToDelete", ApiKey = "k", CreatedAt = DateTime.UtcNow };
        await sut.AddWorkspaceAsync(ws);
        await sut.SetActiveAsync(ws.Id);

        await sut.RemoveWorkspaceAsync(ws.Id);

        sut.Workspaces.Should().BeEmpty();
        sut.Active.Should().BeNull();
    }

    [Fact]
    public void ApiKey_ReturnsEmptyStringWhenNoActive()
    {
        // Not initialized — no active workspace
        var sut = CreateSut(out _);

        sut.ApiKey.Should().BeEmpty();
    }

    // Argha - 2026-03-07 - #20 - Verify seed block removal: fallbackApiKey no longer creates default workspace
    [Fact]
    public async Task InitializeAsync_WithNoStoredData_DoesNotSeedDefaultWorkspace()
    {
        var sut = CreateSut(out _, fallbackApiKey: "global-key");

        await sut.InitializeAsync();

        sut.Workspaces.Should().BeEmpty();
        sut.Active.Should().BeNull();
        sut.HasWorkspaces.Should().BeFalse();
        sut.IsInitialized.Should().BeTrue();
    }

    // Argha - 2026-03-07 - #20 - HasWorkspaces reflects actual workspace list state
    [Fact]
    public async Task HasWorkspaces_ReturnsFalseInitially_TrueAfterAdd()
    {
        var sut = CreateSut(out _);
        await sut.InitializeAsync();
        sut.HasWorkspaces.Should().BeFalse();

        var ws = new StoredWorkspace { Id = Guid.NewGuid(), Name = "X", ApiKey = "k", CreatedAt = DateTime.UtcNow };
        await sut.AddWorkspaceAsync(ws);

        sut.HasWorkspaces.Should().BeTrue();
    }

    // ── Fake IJSRuntime backed by in-memory dictionary ──────────────────
    // Argha - 2026-03-04 - #17 - Minimal stub; handles localStorage.getItem / setItem / removeItem only
    private class FakeJSRuntime : IJSRuntime
    {
        private readonly Dictionary<string, string?> _storage = new();

        public void SetItem(string key, string? value) => _storage[key] = value;
        public string? GetItem(string key) => _storage.TryGetValue(key, out var v) ? v : null;

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            if (identifier == "localStorage.getItem" && args?.Length > 0)
            {
                _storage.TryGetValue((string)args[0]!, out var val);
                TValue typed = (TValue)(object?)val!;
                return ValueTask.FromResult(typed);
            }
            if (identifier == "localStorage.setItem" && args?.Length >= 2)
                _storage[(string)args[0]!] = (string?)args[1];
            if (identifier == "localStorage.removeItem" && args?.Length > 0)
                _storage.Remove((string)args[0]!);
            return ValueTask.FromResult<TValue>(default!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => InvokeAsync<TValue>(identifier, args);
    }
}
