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
    public class AuthController(IMediator mediator) : ControllerBase
    {
        [HttpPost("[action]")]
        public async Task<IActionResult> Login([FromBody] LoginUserCommandRequest request)
        {
            if (HttpContext.Items["customerid"] is Guid customerId) request.CustomerId = customerId;
            return Ok(await mediator.Send(request));
        }

        [HttpPost("[action]")]
        public async Task<IActionResult> RefreshTokenLogin([FromBody] RefreshTokenLoginCommandRequest request)
            => Ok(await mediator.Send(request));
    }

    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class UsersController(IMediator mediator) : ControllerBase
    {
        [HttpPost("[action]")]
        [AuthorizeDefinition(ActionType = ActionType.Writing, Definition = "Create User", Menu = "Users")]
        public async Task<IActionResult> CreateUser([FromBody] CreateUserCommandRequest request)
        {
            // Users are always created inside the caller's tenant.
            request.CustomerId = (HttpContext.Items["customerid"] as Guid?)?.ToString() ?? "";
            return Ok(await mediator.Send(request));
        }

        [HttpGet("[action]")]
        [AuthorizeDefinition(ActionType = ActionType.Reading, Definition = "Get All Users", Menu = "Users")]
        public async Task<IActionResult> GetAllUsers([FromQuery] GetAllUsersQueryRequest request)
        {
            request.Size = Math.Clamp(request.Size, 1, 200);
            return Ok(await mediator.Send(request));
        }

        [HttpGet("[action]/{UserId}")]
        [AuthorizeDefinition(ActionType = ActionType.Reading, Definition = "Get Roles To User", Menu = "Users")]
        public async Task<IActionResult> GetRolesToUser([FromRoute] GetRolesToUserQueryRequest request)
            => Ok(await mediator.Send(request));

        [HttpPost("[action]")]
        [AuthorizeDefinition(ActionType = ActionType.Writing, Definition = "Assign Role To User", Menu = "Users")]
        public async Task<IActionResult> AssignRoleToUser([FromBody] AssignRoleToUserCommandRequest request)
            => Ok(await mediator.Send(request));

        [HttpDelete("[action]/{Id}")]
        [AuthorizeDefinition(ActionType = ActionType.Deleting, Definition = "Delete User", Menu = "Users")]
        public async Task<IActionResult> DeleteUser([FromRoute] DeleteUserCommandRequest request)
            => Ok(await mediator.Send(request));
    }

    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class RolesController(IMediator mediator) : ControllerBase
    {
        [HttpGet("[action]")]
        [AuthorizeDefinition(ActionType = ActionType.Reading, Definition = "Get Roles", Menu = "Roles")]
        public async Task<IActionResult> GetRoles([FromQuery] GetRolesQueryRequest request)
        {
            request.Size = Math.Clamp(request.Size, 1, 200);
            return Ok(await mediator.Send(request));
        }

        [HttpPost("[action]")]
        [AuthorizeDefinition(ActionType = ActionType.Writing, Definition = "Create Role", Menu = "Roles")]
        public async Task<IActionResult> CreateRole([FromBody] CreateRoleCommandRequest request)
            => Ok(await mediator.Send(request));

        [HttpPut("[action]")]
        [AuthorizeDefinition(ActionType = ActionType.Updating, Definition = "Update Role", Menu = "Roles")]
        public async Task<IActionResult> UpdateRole([FromBody] UpdateRoleCommandRequest request)
            => Ok(await mediator.Send(request));

        [HttpDelete("[action]/{Id}")]
        [AuthorizeDefinition(ActionType = ActionType.Deleting, Definition = "Delete Role", Menu = "Roles")]
        public async Task<IActionResult> DeleteRole([FromRoute] DeleteRoleCommandRequest request)
            => Ok(await mediator.Send(request));
    }
}
