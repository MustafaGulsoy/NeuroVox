using Microsoft.Extensions.DependencyInjection;

namespace NeuroVox.Infrastructure
{
    public static class ServiceExtensions
    {
        public static void AddNeuroVoxInfrastructureServices(this IServiceCollection services)
        {
            services.AddMemoryCache();
        }
    }
}
