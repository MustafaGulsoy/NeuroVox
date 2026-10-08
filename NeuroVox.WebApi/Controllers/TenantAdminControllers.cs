using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using BaseAuth.Application.CustomAttributes;
using BaseAuth.Application.Features.Commands.AppUser.CreateUser;
using BaseAuth.Domain.Entities;
using BaseAuth.Domain.Entities.Identity;
using BaseAuth.Domain.Enums;
using BaseAuth.Persistence.Contexts;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NeuroVox.Persistence.Contexts;
using NeuroVox.WebApi.Services;

// Multi-tenant administration. BaseAuth.Library's user/role handlers ignore the caller's institution (GetAllUsers lists
// every tenant, AssignRoleToUser accepts any user id), so these controllers do the same jobs scoped to the JWT's tenant.
namespace NeuroVox.WebApi.Controllers
{
    public abstract class TenantControllerBase : ControllerBase
    {
        protected Guid Tenant => HttpContext.Items["customerid"] is Guid g ? g : Guid.Empty;
        protected Guid? CallerId => Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var g) ? g : null;
    }

    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class UsersController(AuthDbContext db, IMediator mediator, SystemAccess system) : TenantControllerBase
    {
        public class AssignRequest { [Required] public string UserId { get; set; } = string.Empty; public string[] Roles { get; set; } = []; }

        [HttpPost("[action]")]
        [AuthorizeDefinition(ActionType = ActionType.Writing, Definition = "Create User", Menu = "Users")]
        public async Task<IActionResult> CreateUser([FromBody] CreateUserCommandRequest request)
        {
            var max = await db.Customers.Where(c => c.Id == Tenant).Select(c => c.MaxUsers).FirstOrDefaultAsync();
            if (max is { } m && await db.Users.CountAsync(u => u.CustomerId == Tenant && !u.RowIsDeleted) >= m)
                return Conflict(new { title = $"Kurumun kullanıcı sınırına ({m}) ulaşıldı." });
            request.CustomerId = Tenant.ToString();   // always inside the caller's institution
            return Ok(await mediator.Send(request));
        }

        [HttpGet("[action]")]
        [AuthorizeDefinition(ActionType = ActionType.Reading, Definition = "Get All Users", Menu = "Users")]
        public async Task<IActionResult> GetAllUsers([FromQuery] int page = 0, [FromQuery] int size = 50)
        {
            size = Math.Clamp(size, 1, 200);
            var q = db.Users.AsNoTracking().Where(u => u.CustomerId == Tenant && !u.RowIsDeleted);
            var users = await q.OrderByDescending(u => u.RowCreatedDate).Skip(Math.Max(0, page) * size).Take(size).ToListAsync();
            var ids = users.Select(u => u.Id).ToList();
            var roles = await db.UserRoles.Where(ur => ids.Contains(ur.UserId))
                .Join(db.Roles.Where(r => r.CustomerId == Tenant), ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name }).ToListAsync();
            return Ok(new
            {
                users = users.Select(u => new
                {
                    id = u.Id.ToString(), u.Email, u.PhoneNumber, u.NameSurname, u.UserName,
                    userRole = string.Join(", ", roles.Where(r => r.UserId == u.Id).Select(r => r.Name)),
                    customerId = u.CustomerId.ToString()
                }),
                totalUsersCount = await q.CountAsync()
            });
        }

        [HttpGet("[action]/{userId}")]
        [AuthorizeDefinition(ActionType = ActionType.Reading, Definition = "Get Roles To User", Menu = "Users")]
        public async Task<IActionResult> GetRolesToUser(Guid userId)
        {
            if (!await db.Users.AnyAsync(u => u.Id == userId && u.CustomerId == Tenant)) return NotFound();
            var names = await db.UserRoles.Where(ur => ur.UserId == userId)
                .Join(db.Roles.Where(r => r.CustomerId == Tenant), ur => ur.RoleId, r => r.Id, (ur, r) => r.Name).ToListAsync();
            return Ok(new { userRoles = names });
        }

        // Sets the user's roles (replaces the previous set). The system role can only be handed out by the system admin.
        [HttpPost("[action]")]
        [AuthorizeDefinition(ActionType = ActionType.Writing, Definition = "Assign Role To User", Menu = "Users")]
        public async Task<IActionResult> AssignRoleToUser([FromBody] AssignRequest r)
        {
            if (!Guid.TryParse(r.UserId, out var userId)) return BadRequest(new { title = "invalid user id" });
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.CustomerId == Tenant && !u.RowIsDeleted);
            if (user is null) return NotFound();

            var wanted = r.Roles.Distinct().ToList();
            var tenantRoles = await db.Roles.Where(r2 => r2.CustomerId == Tenant && !r2.RowIsDeleted).ToListAsync();
            var picked = tenantRoles.Where(x => wanted.Contains(x.Name!)).ToList();
            if (picked.Count != wanted.Count) return BadRequest(new { title = "Bilinmeyen rol" });

            var isSystemAdmin = await system.IsSystemAdminAsync(User);
            var current = await db.UserRoles.Where(ur => ur.UserId == userId).ToListAsync();
            var currentNames = tenantRoles.Where(x => current.Any(c => c.RoleId == x.Id)).Select(x => x.Name!).ToHashSet();
            var touchesSystemRole = wanted.Contains(system.Role) != currentNames.Contains(system.Role);
            if (touchesSystemRole && !isSystemAdmin) return Forbid();
            // Nobody removes their own admin rights by accident (would lock the institution out).
            if (userId == CallerId && (currentNames.Contains(TenantRoles.Admin) && !wanted.Contains(TenantRoles.Admin) || currentNames.Contains(system.Role) && !wanted.Contains(system.Role)))
                return Conflict(new { title = "Kendi yönetici rolünüzü kaldıramazsınız." });

            db.UserRoles.RemoveRange(current.Where(c => tenantRoles.Any(x => x.Id == c.RoleId)));
            db.UserRoles.AddRange(picked.Select(x => new IdentityUserRole<Guid> { UserId = userId, RoleId = x.Id }));
            await db.SaveChangesAsync();
            return Ok(new { });
        }

        [HttpDelete("[action]/{id:guid}")]
        [AuthorizeDefinition(ActionType = ActionType.Deleting, Definition = "Delete User", Menu = "Users")]
        public async Task<IActionResult> DeleteUser(Guid id)
        {
            if (id == CallerId) return Conflict(new { title = "Kendi hesabınızı silemezsiniz." });
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id && u.CustomerId == Tenant && !u.RowIsDeleted);
            if (user is null) return NotFound();
            var isSystemUser = await db.UserRoles.AnyAsync(ur => ur.UserId == id && db.Roles.Any(r => r.Id == ur.RoleId && r.CustomerId == Tenant && r.Name == system.Role));
            if (isSystemUser && !await system.IsSystemAdminAsync(User)) return Forbid();
            user.RowIsDeleted = true; user.RowIsActive = false; user.RefreshToken = null; user.RowUpdatedDate = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Ok(new { });
        }
    }

    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class RolesController(AuthDbContext db, SystemAccess system) : TenantControllerBase
    {
        public class RoleRequest { [Required, RegularExpression(@"^[\p{L}0-9 _.-]{2,50}$")] public string Name { get; set; } = string.Empty; }
        public class PermissionRequest { public string[] Codes { get; set; } = []; }

        private bool Protected(AppRole r) => r.Name == system.Role || TenantRoles.Reserved.Contains(r.Name!);

        [HttpGet("[action]")]
        [AuthorizeDefinition(ActionType = ActionType.Reading, Definition = "Get Roles", Menu = "Roles")]
        public async Task<IActionResult> GetRoles()
        {
            var isSystemAdmin = await system.IsSystemAdminAsync(User);
            var roles = await db.Roles.AsNoTracking().Where(r => r.CustomerId == Tenant && !r.RowIsDeleted && (isSystemAdmin || r.Name != system.Role))
                .OrderBy(r => r.Name).Select(r => new { r.Id, r.Name }).ToListAsync();
            return Ok(new { datas = roles, totalCount = roles.Count });
        }

        [HttpPost("[action]")]
        [AuthorizeDefinition(ActionType = ActionType.Writing, Definition = "Create Role", Menu = "Roles")]
        public async Task<IActionResult> CreateRole([FromBody] RoleRequest r)
        {
            var name = r.Name.Trim();
            if (name == system.Role || TenantRoles.Reserved.Contains(name, StringComparer.OrdinalIgnoreCase) || name.Equals(system.Role, StringComparison.OrdinalIgnoreCase))
                return Conflict(new { title = "Bu rol adı ayrılmıştır." });
            if (await db.Roles.AnyAsync(x => x.CustomerId == Tenant && x.NormalizedName == name.ToUpperInvariant()))
                return Conflict(new { title = "Bu rol zaten var." });
            var role = Tenants.NewRole(Tenant, name);
            db.Roles.Add(role);   // starts with no permissions
            await db.SaveChangesAsync();
            return Ok(new { id = role.Id });
        }

        [HttpDelete("[action]/{id:guid}")]
        [AuthorizeDefinition(ActionType = ActionType.Deleting, Definition = "Delete Role", Menu = "Roles")]
        public async Task<IActionResult> DeleteRole(Guid id)
        {
            var role = await db.Roles.FirstOrDefaultAsync(x => x.Id == id && x.CustomerId == Tenant);
            if (role is null) return NotFound();
            if (Protected(role)) return Conflict(new { title = "Varsayılan roller silinemez." });
            if (await db.UserRoles.AnyAsync(ur => ur.RoleId == id)) return Conflict(new { title = "Role atanmış kullanıcılar var." });
            db.Roles.Remove(role);
            await db.SaveChangesAsync();
            return Ok(new { });
        }

        // What an institution admin can hand out, grouped for the screen.
        [HttpGet("permission-catalog")]
        [AuthorizeDefinition(ActionType = ActionType.Reading, Definition = "Get Role Permissions", Menu = "Roles")]
        public IActionResult Catalog() => Ok(TenantRoles.Catalog.Select(p => new { p.Code, p.Group, p.Label }));

        [HttpGet("{id:guid}/permissions")]
        [AuthorizeDefinition(ActionType = ActionType.Reading, Definition = "Get Role Permissions", Menu = "Roles")]
        public async Task<IActionResult> Permissions(Guid id)
        {
            var role = await db.Roles.AsNoTracking().Include(r => r.Endpoints).FirstOrDefaultAsync(x => x.Id == id && x.CustomerId == Tenant);
            if (role is null) return NotFound();
            var catalog = TenantRoles.Catalog.Select(p => p.Code).ToHashSet();
            return Ok(new { role = role.Name, editable = !Protected(role) || role.Name == TenantRoles.Doctor,
                codes = role.Endpoints!.Select(e => e.Code).Where(catalog.Contains).ToList() });
        }

        // Grants exactly the given study permissions to the role. KurumAdmin and the system role are fixed; Doktor and custom roles are editable.
        [HttpPut("{id:guid}/permissions")]
        [AuthorizeDefinition(ActionType = ActionType.Updating, Definition = "Update Role", Menu = "Roles")]
        public async Task<IActionResult> SetPermissions(Guid id, [FromBody] PermissionRequest r)
        {
            var role = await db.Roles.Include(x => x.Endpoints).FirstOrDefaultAsync(x => x.Id == id && x.CustomerId == Tenant);
            if (role is null) return NotFound();
            if (Protected(role) && role.Name != TenantRoles.Doctor) return Conflict(new { title = "Bu rolün yetkileri sabittir." });
            var allowed = TenantRoles.Catalog.Select(p => p.Code).ToHashSet();
            if (r.Codes.Any(c => !allowed.Contains(c))) return BadRequest(new { title = "Verilemeyen yetki" });

            var endpoints = await db.Endpoints.Where(e => allowed.Contains(e.Code)).ToListAsync();
            foreach (var e in role.Endpoints!.Where(e => allowed.Contains(e.Code) && !r.Codes.Contains(e.Code)).ToList()) role.Endpoints!.Remove(e);
            foreach (var e in endpoints.Where(e => r.Codes.Contains(e.Code) && !role.Endpoints!.Contains(e))) role.Endpoints!.Add(e);
            await db.SaveChangesAsync();
            return Ok(new { });
        }
    }

    // System admin: institutions (tenants) and their first admin.
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class InstitutionsController(AuthDbContext db, NeuroVoxDbContext nv, IMediator mediator, SystemAccess system) : TenantControllerBase
    {
        public class CreateRequest
        {
            [Required, StringLength(150, MinimumLength = 2)] public string Name { get; set; } = string.Empty;
            public string? City { get; set; }
            [EmailAddress] public string? Email { get; set; }
            public string? PhoneNumber { get; set; }
            [Range(1, 10000)] public int? MaxUsers { get; set; }
            public DateTime? SubscriptionEndDate { get; set; }
            [Required, StringLength(100, MinimumLength = 2)] public string AdminNameSurname { get; set; } = string.Empty;
            [Required, StringLength(100, MinimumLength = 3)] public string AdminUsername { get; set; } = string.Empty;
            [EmailAddress] public string? AdminEmail { get; set; }
            [Required, StringLength(100, MinimumLength = 8)] public string AdminPassword { get; set; } = string.Empty;
        }

        public class UpdateRequest
        {
            [Required, StringLength(150, MinimumLength = 2)] public string Name { get; set; } = string.Empty;
            public string? City { get; set; }
            [EmailAddress] public string? Email { get; set; }
            public string? PhoneNumber { get; set; }
            [Range(1, 10000)] public int? MaxUsers { get; set; }
            public DateTime? SubscriptionEndDate { get; set; }
            public bool IsActive { get; set; } = true;
        }

        [HttpGet]
        [AuthorizeDefinition(ActionType = ActionType.Reading, Definition = "Get All Customers", Menu = "Customers")]
        public async Task<IActionResult> GetAll()
        {
            if (!await system.IsSystemAdminAsync(User)) return Forbid();
            var customers = await db.Customers.AsNoTracking().Where(c => !c.RowIsDeleted).OrderBy(c => c.Name).ToListAsync();
            var users = await db.Users.Where(u => !u.RowIsDeleted).GroupBy(u => u.CustomerId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N);
            var participants = await nv.Participants.IgnoreQueryFilters().Where(p => !p.RowIsDeleted).GroupBy(p => p.CustomerId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N);
            var recordings = await nv.SpeechRecordings.IgnoreQueryFilters().Where(p => !p.RowIsDeleted).GroupBy(p => p.CustomerId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N);
            return Ok(customers.Select(c => new
            {
                c.Id, c.Name, c.City, c.Email, c.PhoneNumber, c.MaxUsers, c.SubscriptionEndDate, isActive = c.RowIsActive, isSystem = c.IsSystemCompany, createdAt = c.RowCreatedDate,
                userCount = users.GetValueOrDefault(c.Id), participantCount = participants.GetValueOrDefault(c.Id), recordingCount = recordings.GetValueOrDefault(c.Id)
            }));
        }

        [HttpPost]
        [AuthorizeDefinition(ActionType = ActionType.Writing, Definition = "Create Customer", Menu = "Customers")]
        public async Task<IActionResult> Create([FromBody] CreateRequest r)
        {
            if (!await system.IsSystemAdminAsync(User)) return Forbid();
            var id = Guid.NewGuid();
            db.Customers.Add(new Customer
            {
                Id = id, Name = r.Name.Trim(), City = r.City, Email = r.Email, PhoneNumber = r.PhoneNumber, MaxUsers = r.MaxUsers,
                SubscriptionStartDate = DateTime.UtcNow, SubscriptionEndDate = r.SubscriptionEndDate
            });
            await db.SaveChangesAsync();
            try
            {
                await Tenants.EnsureRolesAsync(db, id);
                var created = await mediator.Send(new CreateUserCommandRequest
                {
                    NameSurname = r.AdminNameSurname.Trim(), Username = r.AdminUsername.Trim(), Email = r.AdminEmail, Password = r.AdminPassword,
                    PasswordConfirm = r.AdminPassword, CustomerId = id.ToString()
                });
                var adminRole = await db.Roles.FirstAsync(x => x.CustomerId == id && x.Name == TenantRoles.Admin);
                db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = created.UserId, RoleId = adminRole.Id });
                await db.SaveChangesAsync();
                return Ok(new { id });
            }
            catch
            {
                // No half-made institution: remove what was created, then let the error reach the client.
                db.ChangeTracker.Clear();
                var roles = await db.Roles.Where(x => x.CustomerId == id).ToListAsync();
                var stray = await db.Users.Where(u => u.CustomerId == id).ToListAsync();
                db.UserRoles.RemoveRange(db.UserRoles.Where(ur => stray.Select(u => u.Id).Contains(ur.UserId)));
                db.Users.RemoveRange(stray); db.Roles.RemoveRange(roles);
                db.Customers.Remove(await db.Customers.FirstAsync(c => c.Id == id));
                await db.SaveChangesAsync();
                throw;
            }
        }

        [HttpPut("{id:guid}")]
        [AuthorizeDefinition(ActionType = ActionType.Updating, Definition = "Update Customer", Menu = "Customers")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateRequest r)
        {
            if (!await system.IsSystemAdminAsync(User)) return Forbid();
            var c = await db.Customers.FirstOrDefaultAsync(x => x.Id == id && !x.RowIsDeleted);
            if (c is null) return NotFound();
            if (c.IsSystemCompany && !r.IsActive) return Conflict(new { title = "Sistem kurumu pasifleştirilemez." });
            c.Name = r.Name.Trim(); c.City = r.City; c.Email = r.Email; c.PhoneNumber = r.PhoneNumber;
            c.MaxUsers = r.MaxUsers; c.SubscriptionEndDate = r.SubscriptionEndDate; c.RowIsActive = r.IsActive; c.RowUpdatedDate = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Ok(new { });
        }
    }

    // Login needs to know which institution the user belongs to (the customerid header). Names only -- nothing else is public.
    [Route("api/Customers")]
    [ApiController]
    [AllowAnonymous]
    public class PublicCustomersController(AuthDbContext db) : ControllerBase
    {
        [HttpGet("public")]
        public async Task<IActionResult> List() =>
            Ok(await db.Customers.AsNoTracking().Where(c => !c.RowIsDeleted && c.RowIsActive).OrderBy(c => c.Name).Select(c => new { c.Id, c.Name }).ToListAsync());
    }
}
