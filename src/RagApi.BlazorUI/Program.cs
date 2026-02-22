using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using RagApi.BlazorUI;
using RagApi.BlazorUI.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Argha - 2026-02-21 - Configure HttpClient with API base URL and optional API key from wwwroot/appsettings.json 
var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "http://localhost:5000";
var apiKey = builder.Configuration["ApiKey"] ?? string.Empty;

builder.Services.AddScoped(sp =>
{
    var client = new HttpClient { BaseAddress = new Uri(apiBaseUrl) };
    if (!string.IsNullOrWhiteSpace(apiKey))
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
    return client;
});

builder.Services.AddScoped<LocalStorageService>();
builder.Services.AddScoped<ChatApiService>();
builder.Services.AddScoped<DocumentApiService>();
builder.Services.AddScoped<ConversationApiService>();

await builder.Build().RunAsync();
