namespace RagApi.BlazorUI.Services;

// Argha - 2026-03-04 - #17 - DelegatingHandler: injects X-Api-Key from the active workspace on every outgoing request
// Does not override if the request already carries the header (allows per-request override for import validation)
public class WorkspaceKeyHandler : DelegatingHandler
{
    private readonly WorkspaceStateService _wsState;

    public WorkspaceKeyHandler(WorkspaceStateService wsState)
    {
        _wsState = wsState;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (!request.Headers.Contains("X-Api-Key"))
        {
            var key = _wsState.ApiKey;
            if (!string.IsNullOrEmpty(key))
                request.Headers.Add("X-Api-Key", key);
        }
        return base.SendAsync(request, ct);
    }
}
