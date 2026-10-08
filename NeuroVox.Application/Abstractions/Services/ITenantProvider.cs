namespace NeuroVox.Application.Abstractions.Services
{
    // Current request's tenant (customer). Null outside a request (migrations, background work).
    public interface ITenantProvider
    {
        Guid? CustomerId { get; }
    }
}
