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
            // Only guard routes under /api/
            if (!context.Request.Path.StartsWithSegments("/api"))
            {
                await _next(context);
                return;
            }

            // Extract headers
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

            // Load scan settings and validate the key
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

            // Find the key that matches this client_id and is active
            var matchedKey = apiKeys.FirstOrDefault(k =>
                k.ClientId.Equals(suppliedClientId.ToString(), StringComparison.OrdinalIgnoreCase) &&
                k.Status == "active");

            if (matchedKey is null)
            {
                context.Response.StatusCode = 401;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    message = "Invalid or inactive Client ID."
                }));
                return;
            }

            // The stored key is encrypted — decrypt and compare
            try
            {
                var decryptedKey = Cryptor.Decrypt(matchedKey.Key, useHashing: true);
                if (!decryptedKey.Equals(suppliedKey.ToString(), StringComparison.Ordinal))
                {
                    context.Response.StatusCode = 401;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(JsonSerializer.Serialize(new
                    {
                        success = false,
                        message = "Invalid API key."
                    }));
                    return;
                }
            }
            catch
            {
                context.Response.StatusCode = 500;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    message = "Key validation error."
                }));
                return;
            }

            // Attach the validated client ID to the request for controllers to read
            context.Items["ApiClientId"] = matchedKey.ClientId;

            await _next(context);
        }
    }
}