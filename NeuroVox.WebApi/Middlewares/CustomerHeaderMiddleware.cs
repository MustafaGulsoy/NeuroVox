using Microsoft.AspNetCore.Http;

namespace NeuroVox.WebApi.Middlewares
{
    public class CustomerHeaderMiddleware
    {
        private readonly RequestDelegate _next;
        private static readonly string[] ExemptPaths = { "/health", "/swagger", "/api/customers" };

        public CustomerHeaderMiddleware(RequestDelegate next) => _next = next;

        public async Task InvokeAsync(HttpContext context)
        {
            var path = context.Request.Path.Value ?? string.Empty;
            // Only the API needs the tenant header; static pages (labeling UI) are loaded by plain browser navigation.
            if (!path.StartsWith("/api", StringComparison.OrdinalIgnoreCase) || ExemptPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            {
                await _next(context);
                return;
            }

            if (!context.Request.Headers.TryGetValue("customerid", out var customerIdHeader)
                || !Guid.TryParse(customerIdHeader, out var customerId))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new { title = "Invalid or missing customerid header", status = 400 });
                return;
            }

            context.Items["customerid"] = customerId;
            await _next(context);
        }
    }
}
