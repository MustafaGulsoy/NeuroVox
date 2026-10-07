using NeuroVox.WebApi.Middlewares;

namespace NeuroVox.WebApi.Extensions
{
    public static class CustomerMiddlewareExtensions
    {
        public static IApplicationBuilder UseCustomerHeader(this IApplicationBuilder builder)
            => builder.UseMiddleware<CustomerHeaderMiddleware>();
    }
}
