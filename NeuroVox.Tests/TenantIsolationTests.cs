using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NeuroVox.Application.Abstractions.Services;
using NeuroVox.Domain.Entities;
using NeuroVox.Persistence.Contexts;
using NeuroVox.WebApi.Middlewares;

namespace NeuroVox.Tests
{
    public class TenantIsolationTests
    {
        private class FakeTenant(Guid? id) : ITenantProvider { public Guid? CustomerId { get; } = id; }

        private static NeuroVoxDbContext Db(string name, Guid? tenant) =>
            new(new DbContextOptionsBuilder<NeuroVoxDbContext>().UseInMemoryDatabase(name).Options,
                new ConfigurationBuilder().Build(), new FakeTenant(tenant));

        [Fact]
        public void Query_OnlyReturnsCurrentTenantsRows()
        {
            var a = Guid.NewGuid(); var b = Guid.NewGuid(); var name = Guid.NewGuid().ToString();
            using (var seed = Db(name, null))
            {
                seed.Participants.Add(new Participant { Id = Guid.NewGuid(), CustomerId = a, ParticipantCode = "A1" });
                seed.Participants.Add(new Participant { Id = Guid.NewGuid(), CustomerId = b, ParticipantCode = "B1" });
                seed.SaveChanges();
            }
            Assert.Equal(["A1"], Db(name, a).Participants.Select(p => p.ParticipantCode).ToList());
            Assert.Equal(["B1"], Db(name, b).Participants.Select(p => p.ParticipantCode).ToList());
            Assert.Equal(2, Db(name, null).Participants.Count()); // no request context (migrations/worker)
        }

        private static (DefaultHttpContext ctx, bool nextCalled) Run(Guid tokenTenant, Guid? header)
        {
            var ctx = new DefaultHttpContext();
            ctx.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.UserData, tokenTenant.ToString())], "jwt"));
            if (header is not null) ctx.Items["customerid"] = header.Value;
            var called = false;
            new TenantBindingMiddleware(_ => { called = true; return Task.CompletedTask; }).InvokeAsync(ctx).GetAwaiter().GetResult();
            return (ctx, called);
        }

        [Fact]
        public void HeaderDifferentFromToken_IsForbidden()
        {
            var (ctx, called) = Run(Guid.NewGuid(), Guid.NewGuid());
            Assert.Equal(403, ctx.Response.StatusCode);
            Assert.False(called);
        }

        [Fact]
        public void HeaderMatchingToken_PassesAndTokenTenantIsUsed()
        {
            var t = Guid.NewGuid();
            var (ctx, called) = Run(t, t);
            Assert.True(called);
            Assert.Equal(t, ctx.Items["customerid"]);
        }
    }
}
