namespace RagApi.Application.Models;

// Argha - 2026-02-20 - Config POCO for CORS policy 
// Bound from the "Cors" section in appsettings.json.
// Empty AllowedOrigins (default) = allow any origin — dev-friendly.
// Set specific origins in appsettings.Production.json to restrict in production.
public class CorsSettings
{
    public string[] AllowedOrigins { get; set; } = Array.Empty<string>();
}
