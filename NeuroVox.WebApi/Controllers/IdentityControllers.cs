using BaseAuth.Application.CustomAttributes;
using BaseAuth.Application.Features.Commands.AppUser.AssignRoleToUser;
using BaseAuth.Application.Features.Commands.AppUser.CreateUser;
using BaseAuth.Application.Features.Commands.AppUser.DeleteUser;
using BaseAuth.Application.Features.Commands.AppUser.LoginUser;
using BaseAuth.Application.Features.Commands.AppUser.RefreshTokenLogin;
using BaseAuth.Application.Features.Commands.Role.CreateRole;
using BaseAuth.Application.Features.Commands.Role.DeleteRole;
using BaseAuth.Application.Features.Commands.Role.UpdateRole;
using BaseAuth.Application.Features.Queries.AppUser.GetAllUsers;
using BaseAuth.Application.Features.Queries.AppUser.GetRolesToUser;
using BaseAuth.Application.Features.Queries.Role.GetRoles;
using BaseAuth.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

// Single-API deployment: BaseAuth.Library ships services/handlers but not controllers, so the
// identity surface lives here as thin MediatR wrappers. Definitions/menus match BaseAuth's own, so the
// endpoint permission rows seeded from BaseAuth.WebApi keep working.
namespace NeuroVox.WebApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController(IMediator mediator, BaseAuth.Persistence.Contexts.AuthDbContext db) : ControllerBase
    {
        [HttpPost("[action]")]
        public async Task<IActionResult> Login([FromBody] LoginUserCommandRequest request)
        {
            if (HttpContext.Items["customerid"] is Guid customerId)
            {
                request.CustomerId = customerId;
                // A deactivated institution or an expired subscription cannot sign in.
                if (!await NeuroVox.WebApi.Services.Tenants.IsOpenAsync(db, customerId))
                    return StatusCode(StatusCodes.Status403Forbidden, new { title = "Kurum hesabı pasif veya süresi dolmuş." });
            }
            return Ok(await mediator.Send(request));
        }

        [HttpPost("[action]")]
        public async Task<IActionResult> RefreshTokenLogin([FromBody] RefreshTokenLoginCommandRequest request)
            => Ok(await mediator.Send(request));
    }
}
