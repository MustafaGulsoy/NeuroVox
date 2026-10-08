using System.Reflection;
using System.Security.Claims;
using BaseAuth.Application.CustomAttributes;
using BaseAuth.Domain.Entities;
using BaseAuth.Domain.Entities.Identity;
using BaseAuth.Persistence.Contexts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;

namespace NeuroVox.WebApi.Services
{
    /// <summary>One grantable permission as shown in the role-permission screen.</summary>
    public record PermissionInfo(string Code, string Group, string Label, bool DoctorDefault);

    public static class TenantRoles
    {
        public const string Admin = "KurumAdmin";
        public const string Doctor = "Doktor";
        public static readonly string[] Reserved = [Admin, Doctor];

        // Study endpoints an institution admin may hand to roles in their own institution. Kaggle, institutions and the
        // permission-management endpoints are deliberately absent: they stay with the system admin.
        public static readonly PermissionInfo[] Catalog =
        [
            new("GET.Reading.GetParticipants", "Katılımcılar", "Katılımcıları görüntüle", true),
            new("POST.Writing.PostParticipants", "Katılımcılar", "Katılımcı ekle / uygunluk-onam güncelle", true),
            new("DELETE.Deleting.DeleteParticipants", "Katılımcılar", "Katılımcı sil", false),
            new("GET.Reading.GetSpeechRecordings", "Ses kayıtları", "Kayıtları görüntüle / dinle", true),
            new("POST.Writing.PostSpeechRecordings", "Ses kayıtları", "Kayıt yükle", true),
            new("DELETE.Deleting.DeleteSpeechRecordings", "Ses kayıtları", "Kayıt sil", false),
            new("POST.Writing.PostAnalysis", "Analiz", "Kayıt analizi başlat", true),
            new("GET.Reading.GetAnalysis", "Analiz", "Analiz sonuçları ve istatistikler", true),
            new("POST.Writing.PostPredictions", "Analiz", "Model tahmini", true),
            new("GET.Reading.GetTherapistAnnotations", "Anotasyon", "Anotasyonları görüntüle", true),
            new("POST.Writing.PostTherapistAnnotations", "Anotasyon", "Anotasyon yaz (kör değerlendirme)", true),
            new("GET.Reading.GetAceAssessments", "ACE-III", "ACE-III sonuçlarını görüntüle", true),
            new("POST.Writing.PostAceAssessments", "ACE-III", "ACE-III girişi", true),
            new("GET.Reading.GetResearchProtocols", "Araştırma", "Protokolleri görüntüle", true),
            new("POST.Writing.PostResearchProtocols", "Araştırma", "Protokol oluştur / etik bilgisi", false),
            new("GET.Reading.GetStimuli", "Araştırma", "Uyaranları görüntüle", true),
            new("POST.Writing.PostStimuli", "Araştırma", "Uyaran ekle", false),
            new("GET.Reading.GetFeatureDefinitions", "Araştırma", "Özellik tanımlarını görüntüle", true),
            new("GET.Reading.GetExport", "Veri", "Eğitim verisini dışa aktar", false),
            new("GET.Reading.GetTraining", "Model", "Model eğitimi sonuçları", false),
            new("POST.Writing.PostTraining", "Model", "Model eğitimi başlat", false),
        ];

        // Only the institution admin role holds these (user and role management inside the institution).
        public static readonly string[] AdminOnly =
        [
            "GET.Reading.GetAllUsers", "GET.Reading.GetRolesToUser", "POST.Writing.CreateUser", "POST.Writing.AssignRoleToUser", "DELETE.Deleting.DeleteUser",
            "GET.Reading.GetRoles", "POST.Writing.CreateRole", "PUT.Updating.UpdateRole", "DELETE.Deleting.DeleteRole", "GET.Reading.GetRolePermissions"
        ];
    }

    public interface IPermissionChecker
    {
        Task<bool> HasAsync(ClaimsPrincipal user, string code);
    }

    // Replaces BaseAuth's name-based lookup: roles are matched by id inside the caller's own institution, so a role that
    // merely shares a name with another institution's role (or with the system role) grants nothing.
    public class PermissionChecker(AuthDbContext db) : IPermissionChecker
    {
        public async Task<bool> HasAsync(ClaimsPrincipal user, string code)
        {
            if (!Guid.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId)
                || !Guid.TryParse(user.FindFirst(ClaimTypes.UserData)?.Value, out var tenant)) return false;
            var u = await db.Users.AsNoTracking().Where(x => x.Id == userId && x.CustomerId == tenant && !x.RowIsDeleted && x.RowIsActive)
                .Select(x => x.Id).FirstOrDefaultAsync();
            if (u == Guid.Empty) return false;
            if (!await Tenants.IsOpenAsync(db, tenant)) return false;
            var roleIds = db.UserRoles.Where(ur => ur.UserId == userId)
                .Join(db.Roles.Where(r => r.CustomerId == tenant && !r.RowIsDeleted), ur => ur.RoleId, r => r.Id, (ur, r) => r.Id);
            return await db.Endpoints.AsNoTracking().AnyAsync(e => e.Code == code && e.Roles!.Any(r => roleIds.Contains(r.Id)));
        }
    }

    public class TenantRolePermissionFilter(IPermissionChecker checker) : IAsyncActionFilter
    {
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            if (context.ActionDescriptor is not ControllerActionDescriptor d) { await next(); return; }
            var attr = d.MethodInfo.GetCustomAttribute<AuthorizeDefinitionAttribute>();
            var user = context.HttpContext.User;
            if (string.IsNullOrEmpty(user.Identity?.Name))
            {
                if (attr is null) await next(); else context.Result = new UnauthorizedResult();
                return;
            }
            if (attr is null)
            {
                var open = d.MethodInfo.GetCustomAttribute<Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute>() is not null
                    || d.ControllerTypeInfo.GetCustomAttribute<Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute>() is not null;
                if (open) await next(); else context.Result = new UnauthorizedResult();
                return;
            }
            var method = d.MethodInfo.GetCustomAttribute<HttpMethodAttribute>()?.HttpMethods.First() ?? HttpMethods.Get;
            var code = $"{method}.{attr.ActionType}.{attr.Definition.Replace(" ", "")}";
            if (!await checker.HasAsync(user, code)) context.Result = new UnauthorizedResult();
            else await next();
        }
    }

    public static class Tenants
    {
        /// <summary>An institution can sign in while it is active and (if set) inside its subscription period.</summary>
        public static async Task<bool> IsOpenAsync(AuthDbContext db, Guid tenant)
        {
            var c = await db.Customers.AsNoTracking().Where(x => x.Id == tenant && !x.RowIsDeleted)
                .Select(x => new { x.RowIsActive, x.SubscriptionEndDate }).FirstOrDefaultAsync();
            return c is not null && c.RowIsActive && (c.SubscriptionEndDate is null || c.SubscriptionEndDate > DateTime.UtcNow);
        }

        /// <summary>Creates a role row directly: RoleManager looks roles up by name across all institutions and would reject a repeated name.</summary>
        public static AppRole NewRole(Guid tenant, string name) => new()
        {
            Id = Guid.NewGuid(), Name = name, NormalizedName = name.ToUpperInvariant(), CustomerId = tenant,
            ConcurrencyStamp = Guid.NewGuid().ToString()
        };

        /// <summary>Idempotent: makes sure the institution has its KurumAdmin and Doktor roles. KurumAdmin always carries the full admin set; Doktor's grants are written once, then belong to the admin.</summary>
        public static async Task EnsureRolesAsync(AuthDbContext db, Guid tenant)
        {
            var endpoints = await db.Endpoints.ToListAsync();
            var roles = await db.Roles.Include(r => r.Endpoints).Where(r => r.CustomerId == tenant && !r.RowIsDeleted).ToListAsync();

            var admin = roles.FirstOrDefault(r => r.Name == TenantRoles.Admin);
            if (admin is null) { admin = NewRole(tenant, TenantRoles.Admin); admin.Endpoints = []; db.Roles.Add(admin); }
            var adminCodes = TenantRoles.Catalog.Select(p => p.Code).Concat(TenantRoles.AdminOnly).ToHashSet();
            foreach (var e in endpoints.Where(e => adminCodes.Contains(e.Code) && !admin.Endpoints!.Contains(e))) admin.Endpoints!.Add(e);

            if (roles.All(r => r.Name != TenantRoles.Doctor))
            {
                var doc = NewRole(tenant, TenantRoles.Doctor);
                doc.Endpoints = endpoints.Where(e => TenantRoles.Catalog.Any(p => p.DoctorDefault && p.Code == e.Code)).ToList();
                db.Roles.Add(doc);
            }
            await db.SaveChangesAsync();
        }

        /// <summary>The institution that hosts the system admin: marked IsSystemCompany so the flag, not a role name, decides.</summary>
        public static async Task MarkSystemTenantAsync(AuthDbContext db, string systemRole)
        {
            if (await db.Customers.AnyAsync(c => c.IsSystemCompany && !c.RowIsDeleted)) return;
            var tenant = await db.Roles.Where(r => r.Name == systemRole && r.CustomerId != null).Select(r => r.CustomerId).FirstOrDefaultAsync();
            if (tenant is null) return;
            var c = await db.Customers.FirstOrDefaultAsync(x => x.Id == tenant);
            if (c is null) return;
            c.IsSystemCompany = true;
            await db.SaveChangesAsync();
        }
    }

    // The system admin is the system role inside the system institution -- a same-named role elsewhere does not count.
    public class SystemAccess(IServiceScopeFactory scopes, IConfiguration config)
    {
        private Guid? _tenant;
        private DateTime _loaded;

        public string Role => config["NeuroVox:SystemAdminRole"] ?? "NeuroVoxAdmin";

        public async Task<Guid?> TenantIdAsync()
        {
            if (_tenant is not null && DateTime.UtcNow - _loaded < TimeSpan.FromMinutes(5)) return _tenant;
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            _tenant = await db.Customers.AsNoTracking().Where(c => c.IsSystemCompany && !c.RowIsDeleted).Select(c => (Guid?)c.Id).FirstOrDefaultAsync();
            _loaded = DateTime.UtcNow;
            return _tenant;
        }

        public virtual async Task<bool> IsSystemAdminAsync(ClaimsPrincipal user) =>
            user.IsInRole(Role) && Guid.TryParse(user.FindFirst(ClaimTypes.UserData)?.Value, out var t) && t == await TenantIdAsync();
    }

    // Startup: flag the system institution and give every institution its default roles.
    public class TenantSeeder(IServiceScopeFactory scopes, SystemAccess system, IConfiguration config, ILogger<TenantSeeder> log) : IHostedService
    {
        public async Task StartAsync(CancellationToken ct)
        {
            if (!config.GetValue("NeuroVox:AutoMigrate", true)) return;
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
                await Tenants.MarkSystemTenantAsync(db, system.Role);
                foreach (var id in await db.Customers.Where(c => !c.RowIsDeleted).Select(c => c.Id).ToListAsync(ct)) await Tenants.EnsureRolesAsync(db, id);
            }
            catch (Exception ex) { log.LogError(ex, "Tenant seeding failed"); }
        }

        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
