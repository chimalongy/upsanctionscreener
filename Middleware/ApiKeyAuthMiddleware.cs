// Middleware/ApiKeyAuthMiddleware.cs
using System.Text.Json;
using Upsanctionscreener.Classess.Utils;
using Upsanctionscreener.Data;

namespace Upsanctionscreener.Middleware
{
    public class ApiKeyAuthMiddleware
    {
        private const string HeaderName = "X-Api-Key";
        private const string ClientIdHeader = "X-Client-Id";
        private readonly RequestDelegate _next;

        public ApiKeyAuthMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, AppDbContext db)
        {
            if (!context.Request.Path.StartsWithSegments("/api"))
            {
                await _next(context);
                return;
            }

            if (!context.Request.Headers.TryGetValue(HeaderName, out var suppliedKey) ||
                !context.Request.Headers.TryGetValue(ClientIdHeader, out var suppliedClientId))
            {
                context.Response.StatusCode = 401;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    message = $"Missing required headers: '{HeaderName}' and '{ClientIdHeader}'."
                }));
                return;
            }

            var svc = new UpSanctionSettingsService(db);
            var result = await svc.GetScanSettingsAsync();

            if (!result.Success)
            {
                context.Response.StatusCode = 500;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    message = "Failed to validate API key. Try again."
                }));
                return;
            }

            var apiKeys = result.Data?.ApiKeys ?? new List<ApiKey>();

            // Find active key matching both client_id AND the encrypted key directly
            var matchedKey = apiKeys.FirstOrDefault(k =>
                k.ClientId.Equals(suppliedClientId.ToString(), StringComparison.OrdinalIgnoreCase) &&
                k.Status == "active" &&
                k.Key.Equals(suppliedKey.ToString(), StringComparison.Ordinal));

            if (matchedKey is null)
            {
                context.Response.StatusCode = 401;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    message = "Invalid or inactive API key."
                }));
                return;
            }

            context.Items["ApiClientId"] = matchedKey.ClientId;
            await _next(context);
        }
    }
}