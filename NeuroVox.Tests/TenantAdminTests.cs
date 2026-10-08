using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BaseAuth.Domain.Entities;
using BaseAuth.Domain.Entities.Identity;
using BaseAuth.Persistence.Contexts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NeuroVox.Persistence.Contexts;
using NeuroVox.WebApi.Services;

namespace NeuroVox.Tests
{
    // Multi-tenant administration on the real permission checker (role ids, per institution) with in-memory databases.
    public class TenantAdminTests : IClassFixture<TenantAdminTests.TFactory>
    {
        private readonly TFactory _f;
        public TenantAdminTests(TFactory f) => _f = f;

        public class TFactory : WebApplicationFactory<Program>
        {
            private readonly string _db = Guid.NewGuid().ToString(), _auth = Guid.NewGuid().ToString();
            public Guid SystemTenant = Guid.NewGuid(), TenantA = Guid.NewGuid(), TenantB = Guid.NewGuid();
            public Guid SysAdmin = Guid.NewGuid(), AdminA = Guid.NewGuid(), DoctorA = Guid.NewGuid(), AdminB = Guid.NewGuid(), UserB = Guid.NewGuid();

            public TFactory()
            {
                Environment.SetEnvironmentVariable("Auth__Token__SecurityKey", "integration-test-key-integration-test-key-123");
                Environment.SetEnvironmentVariable("Auth__Token__Issuer", "https://test");
                Environment.SetEnvironmentVariable("Auth__Token__Audience", "https://test");
                Environment.SetEnvironmentVariable("Auth__ConnectionString", "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1");
                Environment.SetEnvironmentVariable("NeuroVox__ConnectionString", "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x");
                Environment.SetEnvironmentVariable("NeuroVox__AutoMigrate", "false");
            }

            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("NeuroVox:RequireEligibility", "false");
                builder.UseSetting("NeuroVox:RequireEthicsApproval", "false");
                builder.ConfigureServices(s =>
                {
                    s.RemoveAll<DbContextOptions<NeuroVoxDbContext>>();
                    s.AddDbContext<NeuroVoxDbContext>(o => o.UseInMemoryDatabase(_db));
                    s.RemoveAll<DbContextOptions<AuthDbContext>>();
                    s.AddDbContext<AuthDbContext>(o => o.UseInMemoryDatabase(_auth));
                });
            }

            public async Task SeedAsync()
            {
                using var scope = Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
                var menu = new Menu { Id = Guid.NewGuid(), Name = "NeuroVox" };
                db.Menus.Add(menu);
                var codes = TenantRoles.Catalog.Select(p => p.Code).Concat(TenantRoles.AdminOnly).Concat(
                    ["POST.Writing.CreateCustomer", "GET.Reading.GetAllCustomers", "PUT.Updating.UpdateCustomer", "GET.Reading.GetKaggleAccounts"]).ToList();
                var endpoints = codes.Select(c => new BaseAuth.Domain.Entities.Endpoint { Id = Guid.NewGuid(), Code = c, Definition = c, ActionType = c.Split('.')[1], HttpType = c.Split('.')[0], MenuId = menu.Id }).ToList();
                db.Endpoints.AddRange(endpoints);
                db.Customers.AddRange(
                    new Customer { Id = SystemTenant, Name = "Sistem", IsSystemCompany = true },
                    new Customer { Id = TenantA, Name = "Hastane A" },
                    new Customer { Id = TenantB, Name = "Hastane B" });
                await db.SaveChangesAsync();

                // The system role holds everything, but only inside the system institution.
                var sys = Tenants.NewRole(SystemTenant, "NeuroVoxAdmin"); sys.Endpoints = endpoints;
                db.Roles.Add(sys);
                await db.SaveChangesAsync();
                foreach (var t in new[] { SystemTenant, TenantA, TenantB }) await Tenants.EnsureRolesAsync(db, t);

                async Task User(Guid id, Guid tenant, string name, string role)
                {
                    db.Users.Add(new AppUser { Id = id, UserName = name, NormalizedUserName = name.ToUpperInvariant(), CustomerId = tenant, NameSurname = name });
                    var r = await db.Roles.FirstAsync(x => x.CustomerId == tenant && x.Name == role);
                    db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = id, RoleId = r.Id });
                }
                await User(SysAdmin, SystemTenant, "sysadmin", "NeuroVoxAdmin");
                await User(AdminA, TenantA, "admin-a", TenantRoles.Admin);
                await User(DoctorA, TenantA, "doctor-a", TenantRoles.Doctor);
                await User(AdminB, TenantB, "admin-b", TenantRoles.Admin);
                await User(UserB, TenantB, "doctor-b", TenantRoles.Doctor);
                await db.SaveChangesAsync();
            }
        }

        private async Task<HttpClient> Client(Guid user, Guid tenant, string? role = null)
        {
            if (!_seeded) { await _f.SeedAsync(); _seeded = true; }
            var c = _f.CreateClient();
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiFlowTests.Token(tenant, user, role));
            c.DefaultRequestHeaders.Add("customerid", tenant.ToString());
            return c;
        }
        private static bool _seeded;

        [Fact]
        public async Task Users_And_Roles_Are_Scoped_To_The_Callers_Institution()
        {
            var a = await Client(_f.AdminA, _f.TenantA, TenantRoles.Admin);
            var users = await a.GetFromJsonAsync<JsonElement>("/api/Users/GetAllUsers");
            var names = users.GetProperty("users").EnumerateArray().Select(u => u.GetProperty("userName").GetString()).ToList();
            Assert.Equal(new[] { "admin-a", "doctor-a" }, names.OrderBy(x => x));

            // A user of another institution cannot be touched; the system role name is reserved.
            var cross = await a.PostAsJsonAsync("/api/Users/AssignRoleToUser", new { userId = _f.UserB, roles = new[] { TenantRoles.Doctor } });
            Assert.Equal(HttpStatusCode.NotFound, cross.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await a.DeleteAsync($"/api/Users/DeleteUser/{_f.UserB}")).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await a.PostAsJsonAsync("/api/Roles/CreateRole", new { name = "NeuroVoxAdmin" })).StatusCode);

            // Even a self-made role literally named like the system role (inserted directly) grants nothing outside the system institution.
            using (var scope = _f.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
                var fake = Tenants.NewRole(_f.TenantB, "NeuroVoxAdmin");
                fake.Endpoints = await db.Endpoints.ToListAsync();   // even with every permission, the system-admin guard still says no
                db.Roles.Add(fake);
                db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = _f.UserB, RoleId = fake.Id });
                await db.SaveChangesAsync();
            }
            var b = await Client(_f.UserB, _f.TenantB, "NeuroVoxAdmin");
            Assert.Equal(HttpStatusCode.Forbidden, (await b.GetAsync("/api/Institutions")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await b.GetAsync("/api/KaggleAccounts")).StatusCode);
        }

        [Fact]
        public async Task Admin_Controls_Doctor_Permissions()
        {
            var admin = await Client(_f.AdminA, _f.TenantA, TenantRoles.Admin);
            var doctor = await Client(_f.DoctorA, _f.TenantA, TenantRoles.Doctor);
            Assert.Equal(HttpStatusCode.OK, (await doctor.GetAsync("/api/Participants")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await doctor.GetAsync("/api/Users/GetAllUsers")).StatusCode);   // admin-only

            var roles = (await admin.GetFromJsonAsync<JsonElement>("/api/Roles/GetRoles")).GetProperty("datas").EnumerateArray().ToList();
            Assert.DoesNotContain(roles, r => r.GetProperty("name").GetString() == "NeuroVoxAdmin");
            var docRole = roles.First(r => r.GetProperty("name").GetString() == TenantRoles.Doctor).GetProperty("id").GetGuid();

            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/Roles/{docRole}/permissions", new { codes = new[] { "GET.Reading.GetKaggleAccounts" } })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/Roles/{docRole}/permissions", new { codes = new[] { "POST.Writing.PostParticipants" } })).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await doctor.GetAsync("/api/Participants")).StatusCode);   // revoked
            Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/Roles/{docRole}/permissions", new { codes = new[] { "GET.Reading.GetParticipants" } })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await doctor.GetAsync("/api/Participants")).StatusCode);

            // The admin role itself is fixed; another institution's role cannot be edited.
            var adminRole = roles.First(r => r.GetProperty("name").GetString() == TenantRoles.Admin).GetProperty("id").GetGuid();
            Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync($"/api/Roles/{adminRole}/permissions", new { codes = Array.Empty<string>() })).StatusCode);
            var b = await Client(_f.AdminB, _f.TenantB, TenantRoles.Admin);
            Assert.Equal(HttpStatusCode.NotFound, (await b.PutAsJsonAsync($"/api/Roles/{docRole}/permissions", new { codes = Array.Empty<string>() })).StatusCode);
        }

        [Fact]
        public async Task SystemAdmin_Creates_Institution_With_Its_Admin_And_Deactivates_It()
        {
            var sys = await Client(_f.SysAdmin, _f.SystemTenant, "NeuroVoxAdmin");
            var other = await Client(_f.AdminA, _f.TenantA, TenantRoles.Admin);
            Assert.Equal(HttpStatusCode.Unauthorized, (await other.GetAsync("/api/Institutions")).StatusCode);   // lacks the permission

            var res = await sys.PostAsJsonAsync("/api/Institutions", new
            {
                name = "Üniversite C", city = "Ankara", maxUsers = 5,
                adminNameSurname = "Ayşe Yönetici", adminUsername = "ayse.c", adminPassword = "Abc12345!x"
            });
            Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
            var id = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

            var list = await sys.GetFromJsonAsync<JsonElement>("/api/Institutions");
            var row = list.EnumerateArray().First(c => c.GetProperty("id").GetGuid() == id);
            Assert.Equal(1, row.GetProperty("userCount").GetInt32());

            using (var scope = _f.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
                var admin = await db.Users.FirstAsync(u => u.CustomerId == id);
                var roleNames = await db.UserRoles.Where(ur => ur.UserId == admin.Id).Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name).ToListAsync();
                Assert.Equal(new[] { TenantRoles.Admin }, roleNames);
                Assert.Equal(2, await db.Roles.CountAsync(r => r.CustomerId == id));   // KurumAdmin + Doktor
            }

            var upd = await sys.PutAsJsonAsync($"/api/Institutions/{id}", new { name = "Üniversite C", isActive = false });
            Assert.Equal(HttpStatusCode.OK, upd.StatusCode);
            var pub = await sys.GetFromJsonAsync<JsonElement>("/api/Customers/public");
            Assert.DoesNotContain(pub.EnumerateArray(), c => c.GetProperty("id").GetGuid() == id);
            Assert.Equal(HttpStatusCode.Conflict, (await sys.PutAsJsonAsync($"/api/Institutions/{_f.SystemTenant}", new { name = "Sistem", isActive = false })).StatusCode);
        }
    }
}
