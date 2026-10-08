using BaseAuth.Application.CustomAttributes;
using BaseAuth.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NeuroVox.Application.Services.Analytics;
using NeuroVox.Domain.Enums;
using NeuroVox.Persistence.Contexts;
using NeuroVox.WebApi.Services;

namespace NeuroVox.WebApi.Controllers
{
    // Study-level analyses from the research plan: group comparison with normality-driven test choice, rater agreement,
    // and a per-visit model prediction. All are research outputs, not diagnoses.
    [Route("api/Analysis")]
    [ApiController]
    [Authorize]
    public class StudyAnalyticsController(NeuroVoxDbContext db, ISpeechAnalysisClient ai, ModelStorage models) : ControllerBase
    {
        private const int MinPerGroup = 3;

        // Participant-level comparison (a participant's visits are averaged first, so repeated visits do not inflate n).
        // by=outcome: conversion to AD vs stable MCI. by=diagnosis: mild Alzheimer's vs MCI at entry.
        [HttpGet("group-comparison")]
        [AuthorizeDefinition(Menu = "NeuroVox", Definition = "Get Analysis", ActionType = ActionType.Reading)]
        public async Task<IActionResult> GroupComparison([FromQuery] string by = "outcome")
        {
            if (HttpContext.Items["customerid"] is not Guid customerId) return BadRequest(new { title = "customerid missing" });
            if (by is not ("outcome" or "diagnosis")) return BadRequest(new { title = "by must be outcome or diagnosis" });

            var visits = await TrainingSetBuilder.LoadVisitsAsync(db, customerId);
            var people = visits.GroupBy(v => v.ParticipantId).Select(g => new
            {
                Label = g.First().OutcomeLabel,
                Dx = g.First().Diagnosis,
                F = g.SelectMany(v => v.Features).GroupBy(kv => kv.Key).ToDictionary(x => x.Key, x => x.Average(kv => kv.Value))
            }).ToList();

            var (aName, bName) = by == "outcome" ? ("AD'ye dönüşüm", "Stabil MCI") : ("Hafif Alzheimer", "MCI");
            var a = people.Where(p => by == "outcome" ? p.Label == 1 : p.Dx == BaselineDiagnosis.MildAlzheimer).ToList();
            var b = people.Where(p => by == "outcome" ? p.Label == 0 : p.Dx == BaselineDiagnosis.MCI).ToList();
            var groups = new { a = new { name = aName, n = a.Count }, b = new { name = bName, n = b.Count } };
            if (a.Count < MinPerGroup || b.Count < MinPerGroup)
                return Ok(new { by, groups, results = Array.Empty<object>(), note = $"Her grupta en az {MinPerGroup} katılımcı gerekir." });

            var features = a.Concat(b).SelectMany(p => p.F.Keys).Distinct().ToDictionary(
                f => f,
                f => new Dictionary<string, List<double>>
                {
                    ["a"] = a.Where(p => p.F.ContainsKey(f)).Select(p => p.F[f]).ToList(),
                    ["b"] = b.Where(p => p.F.ContainsKey(f)).Select(p => p.F[f]).ToList()
                });
            var results = await ai.CompareAsync(features);
            return results is null
                ? StatusCode(503, new { title = "İstatistik servisi (AI) çalışmıyor" })
                : Ok(new { by, groups, results = results.Value.GetProperty("results") });
        }

        // Inter-rater agreement per annotation category ("is the category present in the recording?") for recordings with
        // two or more raters, plus agreement between the AI candidate counts and the mean human counts.
        [HttpGet("rater-agreement")]
        [AuthorizeDefinition(Menu = "NeuroVox", Definition = "Get Analysis", ActionType = ActionType.Reading)]
        public async Task<IActionResult> RaterAgreement()
        {
            var ann = await db.TherapistAnnotations.AsNoTracking().Where(a => !a.RowIsDeleted && a.SubmittedAt != null)
                .Select(a => new { a.RecordingId, a.AnnotatorId, a.Category }).ToListAsync();
            var byRec = ann.GroupBy(a => a.RecordingId).ToDictionary(g => g.Key, g => g.ToList());
            var multi = byRec.Where(kv => kv.Value.Select(x => x.AnnotatorId).Distinct().Count() >= 2).ToList();

            var categories = new List<object>();
            foreach (var cat in Enum.GetValues<AnnotationCategory>())
            {
                var xs = new List<int>(); var ys = new List<int>();
                foreach (var (_, list) in multi)
                {
                    var raters = list.Select(x => x.AnnotatorId).Distinct().OrderBy(i => i).Take(2).ToList();
                    xs.Add(list.Any(x => x.AnnotatorId == raters[0] && x.Category == cat) ? 1 : 0);
                    ys.Add(list.Any(x => x.AnnotatorId == raters[1] && x.Category == cat) ? 1 : 0);
                }
                if (xs.Count == 0 || xs.Concat(ys).All(v => v == 0)) continue;   // nobody ever used this category
                var agree = xs.Zip(ys).Count(p => p.First == p.Second) / (double)xs.Count;
                var varying = xs.Concat(ys).Distinct().Count() > 1;
                categories.Add(new { category = cat.ToString(), n = xs.Count, percentAgreement = Math.Round(agree, 3),
                    kappa = varying ? Math.Round(Statistics.CohensKappa(xs, ys), 3) : (double?)null });
            }

            // AI candidate vs human count (Spearman over recordings).
            var map = new (AnnotationCategory Cat, string Feature)[]
            {
                (AnnotationCategory.Repetition, "repetition_candidates"), (AnnotationCategory.VagueExpression, "vague_expression_candidates"),
                (AnnotationCategory.Anomia, "anomia_candidate_pauses"), (AnnotationCategory.Circumlocution, "circumlocution_candidates"),
                (AnnotationCategory.MorphosyntacticIssue, "verbless_sentence_candidates")
            };
            var aiVals = await db.FeatureMeasurements.AsNoTracking()
                .Where(m => !m.RowIsDeleted && m.Layer == MeasurementLayer.AutomaticMeasurement && m.NumericValue != null && map.Select(x => x.Feature).Contains(m.FeatureName))
                .Select(m => new { m.RecordingId, m.FeatureName, V = m.NumericValue!.Value }).ToListAsync();
            var aiVsHuman = new List<object>();
            foreach (var (cat, feature) in map)
            {
                var ai1 = new List<double>(); var human = new List<double>();
                foreach (var m in aiVals.Where(v => v.FeatureName == feature))
                {
                    if (!byRec.TryGetValue(m.RecordingId, out var list)) continue;   // never annotated
                    var raters = Math.Max(1, list.Select(x => x.AnnotatorId).Distinct().Count());
                    ai1.Add(m.V); human.Add(list.Count(x => x.Category == cat) / (double)raters);
                }
                aiVsHuman.Add(new { category = cat.ToString(), feature, n = ai1.Count,
                    spearman = ai1.Count >= 5 && ai1.Distinct().Count() > 1 && human.Distinct().Count() > 1 ? Math.Round(Statistics.Spearman(ai1, human), 3) : (double?)null });
            }
            return Ok(new { annotatedRecordings = byRec.Count, recordingsWithTwoRaters = multi.Count, categories, aiVsHuman,
                note = "İlk iki değerlendirici karşılaştırılır; kappa için kategoride değişkenlik gerekir." });
        }

        // Model estimate for one visit (needs a completed training run). Research use only -- not a diagnosis.
        [HttpGet("~/api/Predictions/visit/{visitId:guid}")]
        [AuthorizeDefinition(Menu = "NeuroVox", Definition = "Get Analysis", ActionType = ActionType.Reading)]
        public async Task<IActionResult> PredictVisit(Guid visitId)
        {
            if (HttpContext.Items["customerid"] is not Guid customerId) return BadRequest(new { title = "customerid missing" });
            var visit = (await TrainingSetBuilder.LoadVisitsAsync(db, customerId, visitId)).FirstOrDefault();
            if (visit is null) return NotFound(new { title = "Bu ziyaret için analiz edilmiş ölçüm yok" });
            if (!models.TryReadFeatureOrder(out var order)) return StatusCode(503, new { title = "Henüz eğitilmiş model yok" });

            var used = order.Where(visit.Features.ContainsKey).ToList();
            var result = await ai.PredictAsync(visit.Features.Where(kv => order.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value));
            if (result is null) return StatusCode(503, new { title = "Model servisi yanıt vermedi veya yeterli özellik yok" });
            return Ok(new
            {
                visitId,
                modelEstimatedRisk = result.Value.TryGetProperty("model_estimated_risk", out var r) && r.ValueKind == System.Text.Json.JsonValueKind.Number ? r.GetDouble() : (double?)null,
                predictedLabel = result.Value.TryGetProperty("predicted_label", out var l) ? l.GetInt32() : (int?)null,
                featuresUsed = used.Count, featuresExpected = order.Count,
                missing = order.Except(used).ToList(),
                disclaimer = "Araştırma amaçlı model tahminidir; tanı değildir ve klinik karar yerine geçmez."
            });
        }
    }
}
