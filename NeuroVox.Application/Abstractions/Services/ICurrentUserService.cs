namespace NeuroVox.Application.Abstractions.Services
{
    public interface ICurrentUserService
    {
        string GetUsername();
        string? GetCustomerId();
        string? GetIpAddress();
        Guid? UserId { get; }
        string? UserName { get; }
        string? Email { get; }
        IEnumerable<string> Roles { get; }
        bool IsInRole(string role);
    }
}
