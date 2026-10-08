using System.Security.Claims;

namespace NeuroVox.WebApi.Middlewares
{
    // Runs after authentication. The JWT's customer claim is authoritative; the client-sent
    // `customerid` header must match it, otherwise a user could address another tenant.
    public class TenantBindingMiddleware(RequestDelegate next)
    {
        public async Task InvokeAsync(HttpContext context)
        {
            if (context.User.Identity?.IsAuthenticated == true)
            {
                var claim = context.User.FindFirst(ClaimTypes.UserData)?.Value;
                if (!Guid.TryParse(claim, out var tokenTenant)
                    || (context.Items["customerid"] is Guid header && header != tokenTenant))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsJsonAsync(new { title = "Tenant mismatch", status = 403 });
                    return;
                }
                context.Items["customerid"] = tokenTenant;
            }
            await next(context);
        }
    }
}
