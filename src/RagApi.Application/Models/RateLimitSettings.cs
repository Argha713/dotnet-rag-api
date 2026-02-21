namespace RagApi.Application.Models;

// Argha - 2026-02-20 - Config POCO for rate limiting 
// Bound from the "RateLimit" section in appsettings.json.
// Enabled=false (default) disables rate limiting entirely — safe for local development.
public class RateLimitSettings
{
    public bool Enabled { get; set; } = false;

    // Argha - 2026-02-20 - Maximum requests allowed per window period 
    public int PermitLimit { get; set; } = 60;

    // Argha - 2026-02-20 - Duration of the fixed window in seconds 
    public int WindowSeconds { get; set; } = 60;

    // Argha - 2026-02-20 - Number of requests to queue when limit is hit; 0 = reject immediately 
    public int QueueLimit { get; set; } = 0;
}
