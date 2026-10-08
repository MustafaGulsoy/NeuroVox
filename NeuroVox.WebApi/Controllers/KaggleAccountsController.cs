using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using BaseAuth.Application.CustomAttributes;
using BaseAuth.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NeuroVox.Domain.Entities;
using NeuroVox.Domain.Enums;
using NeuroVox.Persistence.Contexts;
using NeuroVox.WebApi.Services;

namespace NeuroVox.WebApi.Controllers
{
    // System-admin only: besides the BaseAuth permission, the caller must hold the NeuroVox:SystemAdminRole role.
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class KaggleAccountsController(NeuroVoxDbContext db, SecretProtector protector, IKaggleClient kaggle, AiHostPool pool, IConfiguration config) : ControllerBase
    {
        public class AddRequest
        {
            [Required, StringLength(100, MinimumLength = 2), RegularExpression(@"^[A-Za-z0-9_.-]+$")] public string Username { get; set; } = string.Empty;
            [Required, StringLength(512, MinimumLength = 10)] public string ApiKey { get; set; } = string.Empty;
        }

        private bool IsSystemAdmin => User.IsInRole(config["NeuroVox:SystemAdminRole"] ?? "NeuroVoxAdmin");

        private object View(KaggleAccount a) => new
        {
            a.Id, a.Username, a.RowCreatedDate, a.LastHeartbeatUtc, a.LastConnectAttemptUtc, a.LastError, a.KernelStartedUtc,
            Mode = AiHostPool.IsOnline(a) || a.LastConnectAttemptUtc != null ? (a.RunningOnGpu ? "gpu" : "cpu") : null,
            GpuHoursUsed = Math.Round(a.GpuSecondsThisWeek / 3600, 1),
            GpuHoursLimit = Math.Round(pool.WeeklyGpuSeconds / 3600, 1),
            GpuExhausted = a.GpuExhaustedUntilUtc > DateTime.UtcNow,
            Status = AiHostPool.IsOnline(a) ? "online"
                : a.LastError is not null ? "error"
                : a.LastConnectAttemptUtc is { } t && DateTime.UtcNow - t < TimeSpan.FromMinutes(15) ? "starting"
                : "offline"
        };

        [HttpGet]
        [AuthorizeDefinition(Menu = "NeuroVox", Definition = "Get KaggleAccounts", ActionType = ActionType.Reading)]
        public async Task<IActionResult> GetAll()
        {
            if (!IsSystemAdmin) return Forbid();
            var list = await db.KaggleAccounts.AsNoTracking().OrderBy(a => a.Username).ToListAsync();
            var queued = await db.SpeechRecordings.CountAsync(r => !r.RowIsDeleted && (r.AnalysisStatus == AnalysisStatus.Queued || r.AnalysisStatus == AnalysisStatus.Running));
            var training = await db.TrainingRuns.CountAsync(r => !r.RowIsDeleted && (r.Status == AnalysisStatus.Queued || r.Status == AnalysisStatus.Running));
            return Ok(new
            {
                secretsEnabled = protector.Enabled,
                alwaysOn = config.GetValue("NeuroVox:KaggleAlwaysOn", true),
                queue = new { recordings = queued, training },
                accounts = list.Select(View)
            });
        }

        // Console output of the account's last kernel session -- the first place to look when a kernel never comes online.
        [HttpGet("{id:guid}/log")]
        [AuthorizeDefinition(Menu = "NeuroVox", Definition = "Get KaggleAccounts", ActionType = ActionType.Reading)]
        public async Task<IActionResult> Log(Guid id, CancellationToken ct)
        {
            if (!IsSystemAdmin) return Forbid();
            var a = await db.KaggleAccounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (a is null) return NotFound();
            var log = await kaggle.GetLogAsync(a.Username, protector.Unprotect(a.EncryptedApiKey, "kaggle:" + a.Id), AiHostPool.KernelSlug, 4000, ct);
            return Ok(new { log });
        }

        [HttpPost]
        [AuthorizeDefinition(Menu = "NeuroVox", Definition = "Post KaggleAccounts", ActionType = ActionType.Writing)]
        public async Task<IActionResult> Add([FromBody] AddRequest r, CancellationToken ct)
        {
            if (!IsSystemAdmin) return Forbid();
            if (!protector.Enabled) return StatusCode(503, new { title = "NeuroVox:SecretKey is not configured; secrets cannot be stored" });
            if (HttpContext.Items["customerid"] is not Guid customerId) return BadRequest(new { title = "customerid missing" });
            var username = r.Username.Trim();
            if (await db.KaggleAccounts.AnyAsync(a => a.Username == username, ct)) return Conflict(new { title = "account already added" });

            if (!await kaggle.ValidateAsync(username, r.ApiKey.Trim(), ct))
                return BadRequest(new { title = "Kaggle rejected this username/token" });

            var id = Guid.NewGuid();
            db.KaggleAccounts.Add(new KaggleAccount
            {
                Id = id, CustomerId = customerId, Username = username,
                EncryptedApiKey = protector.Protect(r.ApiKey.Trim(), "kaggle:" + id),
                CreatedBy = User.Identity?.Name
            });
            await db.SaveChangesAsync(ct);
            return Ok(new { id });
        }

        // Pushes and starts the kernel right now (otherwise it is started on demand when a recording needs analysis).
        [HttpPost("{id:guid}/connect")]
        [AuthorizeDefinition(Menu = "NeuroVox", Definition = "Connect KaggleAccounts", ActionType = ActionType.Updating)]
        public async Task<IActionResult> Connect(Guid id, CancellationToken ct)
        {
            if (!IsSystemAdmin) return Forbid();
            if (!await db.KaggleAccounts.AnyAsync(a => a.Id == id, ct)) return NotFound();
            var acc = await db.KaggleAccounts.FirstAsync(a => a.Id == id, ct);
            var err = await pool.ConnectAsync(id, pool.ShouldUseGpu(acc), ct);
            return err is null ? Accepted() : StatusCode(502, new { title = err });
        }

        [HttpDelete("{id:guid}")]
        [AuthorizeDefinition(Menu = "NeuroVox", Definition = "Delete KaggleAccounts", ActionType = ActionType.Deleting)]
        public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        {
            if (!IsSystemAdmin) return Forbid();
            // Hard delete: the encrypted secrets must not linger. A running kernel's next heartbeat is rejected and it shuts down.
            var a = await db.KaggleAccounts.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (a is null) return NotFound();
            db.KaggleAccounts.Remove(a);
            await db.SaveChangesAsync(ct);
            return NoContent();
        }
    }

    // Called by the Kaggle kernel itself (no user token): it proves itself with the one-time token we embedded in its source.
    [Route("api/[controller]")]
    [ApiController]
    [AllowAnonymous]
    public class AiHostsController(NeuroVoxDbContext db) : ControllerBase
    {
        public class RegisterRequest { [Required] public string Url { get; set; } = string.Empty; public bool Gpu { get; set; } }

        // Only Cloudflare quick-tunnel hosts are accepted, so a leaked token cannot point audio at an arbitrary server.
        private static readonly Regex Tunnel = new(@"^https://[a-z0-9-]+\.trycloudflare\.com/?$", RegexOptions.Compiled);

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest r, CancellationToken ct)
        {
            var token = Request.Headers["X-Register-Token"].ToString();
            if (token.Length < 32 || !Tunnel.IsMatch(r.Url ?? "")) return BadRequest();
            var hash = AiHostPool.Hash(token);
            var a = await db.KaggleAccounts.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.RegisterTokenHash == hash && x.RowIsActive, ct);
            if (a is null) return StatusCode(StatusCodes.Status410Gone);   // revoked / deleted / superseded by a newer push
            var now = DateTime.UtcNow;
            // GPU quota bookkeeping: sum the time between heartbeats (capped, so a gap is not billed).
            if (a.LastHeartbeatUtc is { } prev && a.RunningOnGpu) a.GpuSecondsThisWeek += Math.Min((now - prev).TotalSeconds, 120);
            a.RunningOnGpu = r.Gpu;
            a.KernelStartedUtc ??= now;
            a.PublicUrl = r.Url.TrimEnd('/');
            a.LastHeartbeatUtc = now;
            a.LastError = null;
            await db.SaveChangesAsync(ct);
            return NoContent();
        }
    }
}
