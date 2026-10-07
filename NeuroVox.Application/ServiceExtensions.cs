using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace NeuroVox.Application
{
    public static class ServiceExtensions
    {
        public static void AddNeuroVoxApplicationServices(this IServiceCollection services)
        {
            services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly()));
        }
    }
}
