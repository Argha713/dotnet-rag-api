using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using RagApi.BlazorUI;
using RagApi.BlazorUI.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Argha - 2026-03-04 - #17 - Read API base URL; static ApiKey kept for backward-compat seeding in WorkspaceStateService
var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "http://localhost:5000";

// Argha - 2026-03-04 - #17 - WorkspaceStateService: Singleton; persists workspace list + active selection to localStorage
builder.Services.AddSingleton<WorkspaceStateService>();

// Argha - 2026-03-04 - #17 - WorkspaceKeyHandler: Transient DelegatingHandler; injects X-Api-Key from active workspace
builder.Services.AddTransient<WorkspaceKeyHandler>();

// Argha - 2026-03-04 - #17 - HttpClient wired through WorkspaceKeyHandler for dynamic key injection on every request
// Static X-Api-Key default header removed; key now injected per-request via handler
builder.Services.AddScoped(sp =>
{
    var handler = sp.GetRequiredService<WorkspaceKeyHandler>();
    handler.InnerHandler = new HttpClientHandler();
    return new HttpClient(handler) { BaseAddress = new Uri(apiBaseUrl) };
});

builder.Services.AddScoped<LocalStorageService>();
builder.Services.AddScoped<ChatApiService>();
builder.Services.AddScoped<DocumentApiService>();
builder.Services.AddScoped<ConversationApiService>();
builder.Services.AddScoped<WorkspaceApiService>();

await builder.Build().RunAsync();
