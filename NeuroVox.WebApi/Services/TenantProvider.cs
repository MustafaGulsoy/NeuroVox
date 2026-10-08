using NeuroVox.Application.Abstractions.Services;

namespace NeuroVox.WebApi.Services
{
    public class TenantProvider(IHttpContextAccessor accessor) : ITenantProvider
    {
        public Guid? CustomerId =>
            accessor.HttpContext?.Items["customerid"] is Guid id ? id : null;
    }
}
