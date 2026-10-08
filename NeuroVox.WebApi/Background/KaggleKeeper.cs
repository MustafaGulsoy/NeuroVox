using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using NeuroVox.Domain.Entities;
using NeuroVox.Domain.Enums;
using NeuroVox.Persistence.Contexts;
using NeuroVox.WebApi.Services;

namespace NeuroVox.WebApi.Background
{
    // Keeps Kaggle capacity matched to the work: always one kernel up (NeuroVox:KaggleAlwaysOn), more when jobs pile up,
    // and spreads kernels over the accounts with the most GPU hours left. Accounts out of GPU quota run CPU-only kernels,
    // so work keeps moving; when nothing is online, jobs simply wait in the queue until a kernel registers.
    public class KaggleKeeper(IServiceScopeFactory scopes, AiHostPool pool, SecretProtector protector, IKaggleClient kaggle, IConfiguration config, ILogger<KaggleKeeper> log) : BackgroundService
    {
        private static readonly TimeSpan StartWindow = TimeSpan.FromMinutes(15), ErrorBackoff = TimeSpan.FromMinutes(30), Diagnose = TimeSpan.FromMinutes(5);
        private static readonly Regex QuotaText = new("quota|accelerator|gpu", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        protected override async Task ExecuteAsync(CancellationToken ct)
        {
            await Task.Delay(TimeSpan.FromSeconds(20), ct);
            while (!ct.IsCancellationRequested)
            {
                try { await TickAsync(ct); }
                catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "Kaggle keeper tick failed"); }
                await Task.Delay(TimeSpan.FromSeconds(60), ct);
            }
        }

        private static bool Starting(KaggleAccount a) =>
            a.LastError is null && a.LastHeartbeatUtc is null && a.LastConnectAttemptUtc is { } t && DateTime.UtcNow - t < StartWindow;

        public async Task TickAsync(CancellationToken ct)
        {
            if (!protector.Enabled) return;
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NeuroVoxDbContext>();
            var now = DateTime.UtcNow;
            var week = pool.WeekStart(now);
            var accounts = await db.KaggleAccounts.IgnoreQueryFilters().Where(a => a.RowIsActive).ToListAsync(ct);

            foreach (var a in accounts.Where(a => a.QuotaWeekStartUtc != week))
            {
                a.QuotaWeekStartUtc = week; a.GpuSecondsThisWeek = 0; a.GpuExhaustedUntilUtc = null;
            }
            foreach (var a in accounts.Where(a => Starting(a) && now - a.LastConnectAttemptUtc > Diagnose))
                await DiagnoseAsync(a, week, ct);
            await db.SaveChangesAsync(ct);

            var alwaysOn = config.GetValue("NeuroVox:KaggleAlwaysOn", true);
            var maxParallel = Math.Max(1, config.GetValue("NeuroVox:KaggleMaxParallel", 2));

            foreach (var tenant in accounts.GroupBy(a => a.CustomerId))
            {
                var pending = await db.SpeechRecordings.IgnoreQueryFilters().CountAsync(r => r.CustomerId == tenant.Key && !r.RowIsDeleted
                                  && (r.AnalysisStatus == AnalysisStatus.Queued || r.AnalysisStatus == AnalysisStatus.Running), ct)
                              + await db.TrainingRuns.IgnoreQueryFilters().CountAsync(r => r.CustomerId == tenant.Key && !r.RowIsDeleted
                                  && (r.Status == AnalysisStatus.Queued || r.Status == AnalysisStatus.Running), ct);
                var desired = Math.Min(tenant.Count(), pending == 0 ? (alwaysOn ? 1 : 0) : Math.Clamp((pending + 1) / 2, 1, maxParallel));
                if (tenant.Count(a => AiHostPool.IsOnline(a) || Starting(a)) >= desired) continue;

                var pick = tenant.Where(a => !AiHostPool.IsOnline(a) && !Starting(a)
                                             && (a.LastError is null || now - a.LastConnectAttemptUtc > ErrorBackoff))
                    .OrderByDescending(pool.RemainingGpuSeconds).ThenBy(_ => Random.Shared.Next()).FirstOrDefault();
                if (pick is null) continue;

                var gpu = pool.ShouldUseGpu(pick);
                var err = await pool.ConnectAsync(pick.Id, gpu, ct);
                log.LogInformation("Kaggle keeper: started {User} ({Mode}), pending={Pending}, error={Err}", pick.Username, gpu ? "gpu" : "cpu", pending, err);
            }
        }

        // A kernel that never registered: ask Kaggle why (quota, crash) so the next attempt can pick CPU mode or another account.
        private async Task DiagnoseAsync(KaggleAccount a, DateTime week, CancellationToken ct)
        {
            var st = await kaggle.GetStatusAsync(a.Username, protector.Unprotect(a.EncryptedApiKey, "kaggle:" + a.Id), AiHostPool.KernelSlug, ct);
            if (st is not { } s || s.Status is "queued" or "running" or "new_script" or "unknown") return;
            var msg = string.IsNullOrWhiteSpace(s.Failure) ? $"kernel {s.Status} (kayıt başlamadan bitti)" : s.Failure;
            if (msg.Contains("KAGGLE_INTERNET_OFF") || msg.Contains("name resolution"))
                msg = "Kernel internete erişemiyor: Kaggle hesabında telefon doğrulaması gerekli (kaggle.com/settings)";
            a.LastError = msg.Length > 300 ? msg[..300] : msg;
            if (a.RunningOnGpu && QuotaText.IsMatch(msg)) a.GpuExhaustedUntilUtc = week.AddDays(7);
        }
    }
}
