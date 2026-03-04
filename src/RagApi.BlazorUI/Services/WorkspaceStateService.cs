using Microsoft.Extensions.Configuration;
using Microsoft.JSInterop;
using System.Text.Json;
using RagApi.BlazorUI.Models;

namespace RagApi.BlazorUI.Services;

// Argha - 2026-03-04 - #17 - Singleton state manager for workspace selection; persists to localStorage
public class WorkspaceStateService
{
    private readonly IJSRuntime _js;
    private readonly string _fallbackApiKey;
    private static readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web);

    private List<StoredWorkspace> _workspaces = new();
    private Guid? _activeId;
    private bool _initialized;

    public IReadOnlyList<StoredWorkspace> Workspaces => _workspaces;

    public StoredWorkspace? Active => _activeId.HasValue
        ? _workspaces.FirstOrDefault(w => w.Id == _activeId)
        : null;

    // Argha - 2026-03-04 - #17 - Consumed by WorkspaceKeyHandler on every outgoing request
    public string ApiKey => Active?.ApiKey ?? string.Empty;

    public event Action? OnChanged;

    public WorkspaceStateService(IJSRuntime js, IConfiguration config)
    {
        _js = js;
        _fallbackApiKey = config["ApiKey"] ?? string.Empty;
    }

    // Argha - 2026-03-04 - #17 - Idempotent: safe to call from multiple components; only loads once
    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;

        var storedJson = await _js.InvokeAsync<string?>("localStorage.getItem", "rag_workspaces");
        if (!string.IsNullOrEmpty(storedJson))
            _workspaces = JsonSerializer.Deserialize<List<StoredWorkspace>>(storedJson, _options) ?? new();

        var activeIdStr = await _js.InvokeAsync<string?>("localStorage.getItem", "rag_active_workspace_id");
        if (Guid.TryParse(activeIdStr, out var id))
            _activeId = id;

        // Argha - 2026-03-04 - #17 - Backward compat: seed default workspace from appsettings ApiKey on first launch
        if (_workspaces.Count == 0 && !string.IsNullOrEmpty(_fallbackApiKey))
        {
            var defaultWs = new StoredWorkspace
            {
                Id = Guid.NewGuid(),
                Name = "Default",
                ApiKey = _fallbackApiKey,
                CreatedAt = DateTime.UtcNow
            };
            _workspaces.Add(defaultWs);
            _activeId = defaultWs.Id;
            await PersistAsync();
        }
    }

    public async Task AddWorkspaceAsync(StoredWorkspace ws)
    {
        _workspaces.Add(ws);
        await PersistAsync();
        OnChanged?.Invoke();
    }

    public async Task SetActiveAsync(Guid id)
    {
        _activeId = id;
        await PersistAsync();
        OnChanged?.Invoke();
    }

    public async Task RemoveWorkspaceAsync(Guid id)
    {
        _workspaces.RemoveAll(w => w.Id == id);
        if (_activeId == id)
            _activeId = null;
        await PersistAsync();
        OnChanged?.Invoke();
    }

    private async Task PersistAsync()
    {
        var json = JsonSerializer.Serialize(_workspaces, _options);
        await _js.InvokeVoidAsync("localStorage.setItem", "rag_workspaces", json);

        if (_activeId.HasValue)
            await _js.InvokeVoidAsync("localStorage.setItem", "rag_active_workspace_id", _activeId.Value.ToString());
        else
            await _js.InvokeVoidAsync("localStorage.removeItem", "rag_active_workspace_id");
    }
}
