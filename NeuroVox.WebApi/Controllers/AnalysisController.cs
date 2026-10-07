using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BaseAuth.Application.CustomAttributes;
using BaseAuth.Domain.Enums;
using NeuroVox.Application.Repositories.AceAssessments;
using NeuroVox.Application.Repositories.FeatureMeasurements;
using NeuroVox.Application.Repositories.Participants;
using NeuroVox.Application.Repositories.StudyVisits;
using NeuroVox.Application.Services.Analytics;
using NeuroVox.Domain.Entities;

namespace NeuroVox.WebApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class AnalysisController : ControllerBase
    {
        private readonly IFeatureMeasurementReadRepository _measurements;
        private readonly IAceAssessmentReadRepository _aces;
        private readonly IParticipantReadRepository _participants;
        private readonly IStudyVisitReadRepository _visits;

        public AnalysisController(IFeatureMeasurementReadRepository measurements, IAceAssessmentReadRepository aces, IParticipantReadRepository participants, IStudyVisitReadRepository visits)
        {
            _measurements = measurements;
            _aces = aces;
            _participants = participants;
            _visits = visits;
        }

        public class IrrRequest
        {
            public string VariableType { get; set; } = "categorical"; // categorical | ordinal | continuous
            public int RaterCount { get; set; } = 2;
            public List<SubjectRatings> Subjects { get; set; } = new();
        }

        public class SubjectRatings
        {
            public string SubjectId { get; set; } = string.Empty;
            public List<string>? CategoryRatings { get; set; }
            public List<double>? NumericRatings { get; set; }
        }

        [HttpPost("irr")]
        [AuthorizeDefinition(Menu = "NeuroVox", Definition = "Post Analysis", ActionType = ActionType.Writing)]
        
        public IActionResult InterRaterReliability([FromBody] IrrRequest req)
        {
            return req.VariableType.ToLowerInvariant() switch
            {
                "categorical" when req.RaterCount == 2 => Ok(new { statistic = "cohens_kappa", value = KappaCategorical(req, cohens: true) }),
                "categorical" => Ok(new { statistic = "fleiss_kappa", value = Fleiss(req) }),
                "ordinal" => Ok(new { statistic = "weighted_kappa", value = Weighted(req) }),
                "continuous" => Ok(new { statistic = "icc_21", value = Icc(req) }),
                _ => BadRequest(new { title = "Unknown variable type" })
            };
        }

        private double KappaCategorical(IrrRequest req, bool cohens)
        {
            var a = req.Subjects.Select(s => ParseCat(s.CategoryRatings![0])).ToList();
            var b = req.Subjects.Select(s => ParseCat(s.CategoryRatings![1])).ToList();
            return cohens ? Statistics.CohensKappa(a, b) : 0;
        }

        private int ParseCat(string v) => int.TryParse(v, out var i) ? i : v.GetHashCode();

        private double Fleiss(IrrRequest req)
        {
            var labels = req.Subjects.SelectMany(s => s.CategoryRatings!).Distinct().ToList();
            var counts = new int[req.Subjects.Count, labels.Count];
            for (int s = 0; s < req.Subjects.Count; s++)
                foreach (var r in req.Subjects[s].CategoryRatings!)
                    counts[s, labels.IndexOf(r)]++;
            return Statistics.FleissKappa(counts, req.Subjects.Count, req.RaterCount);
        }

        private double Weighted(IrrRequest req)
        {
            var a = req.Subjects.Select(s => ParseCat(s.CategoryRatings![0])).ToList();
            var b = req.Subjects.Select(s => ParseCat(s.CategoryRatings![1])).ToList();
            return Statistics.WeightedKappa(a, b);
        }

        private double Icc(IrrRequest req)
        {
            int n = req.Subjects.Count, k = req.RaterCount;
            var x = new double[n, k];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < k; j++)
                    x[i, j] = req.Subjects[i].NumericRatings![j];
            return Statistics.IccTwoWayRandomSingle(x);
        }

        // Association between one speech feature and one ACE-III subdomain.
        [HttpGet("association")]
        [AuthorizeDefinition(Menu = "NeuroVox", Definition = "Get Analysis", ActionType = ActionType.Reading)]
        
        public IActionResult Association([FromQuery] string speechFeature, [FromQuery] string cognitiveScore = "total")
        {
            var featureByVisit = _measurements.GetWhere(m => m.FeatureName == speechFeature && m.NumericValue != null && !m.RowIsDeleted)
                .GroupBy(m => m.VisitId).ToDictionary(g => g.Key, g => g.Average(m => m.NumericValue!.Value));
            var aces = _aces.GetWhere(a => !a.RowIsDeleted).ToList();

            double? GetScore(AceAssessment ace) => cognitiveScore.ToLowerInvariant() switch
            {
                "attention" => ace.AttentionScore,
                "memory" => ace.MemoryScore,
                "fluency" => ace.FluencyScore,
                "language" => ace.LanguageScore,
                "visuospatial" => ace.VisuospatialScore,
                _ => ace.TotalScore,
            };

            var x = new List<double>(); var y = new List<double>();
            foreach (var ace in aces)
            {
                var sv = GetScore(ace);
                if (sv is null) continue;
                if (!featureByVisit.TryGetValue(ace.VisitId, out var fv)) continue;
                x.Add(fv); y.Add(sv.Value);
            }

            if (x.Count < 2) return Ok(new { n = x.Count, pearson = (double?)null, spearman = (double?)null });
            return Ok(new { n = x.Count, pearson = Statistics.Pearson(x, y), spearman = Statistics.Spearman(x, y) });
        }

        // Longitudinal change for one feature of one participant.
        [HttpGet("longitudinal/{participantId:guid}")]
        [AuthorizeDefinition(Menu = "NeuroVox", Definition = "Get Analysis", ActionType = ActionType.Reading)]
        
        public IActionResult Longitudinal(Guid participantId, [FromQuery] string featureName)
        {
            var participant = _participants.GetWhere(p => p.Id == participantId && !p.RowIsDeleted).FirstOrDefault();
            if (participant is null) return NotFound();

            var visits = _visits.GetWhere(v => v.ParticipantId == participantId && !v.RowIsDeleted).ToList();
            var records = _measurements.GetWhere(m => m.ParticipantId == participantId && m.FeatureName == featureName && m.NumericValue != null)
                .ToList();

            var points = records
                .Select(r => new
                {
                    r.VisitId,
                    Value = r.NumericValue!.Value,
                    Date = visits.FirstOrDefault(v => v.Id == r.VisitId)?.ActualDate
                })
                .Where(p => p.Date != null)
                .OrderBy(p => p.Date)
                .ToList();

            if (points.Count < 2)
                return Ok(new { participantId, featureName, n = points.Count, absoluteChange = (double?)null, relativeChange = (double?)null, annualizedChange = (double?)null, slope = (double?)null });

            var first = points.First();
            var last = points.Last();
            double months = (last.Date!.Value.ToDateTime(TimeOnly.MinValue) - first.Date!.Value.ToDateTime(TimeOnly.MinValue)).TotalDays / 30.44;
            double abs = last.Value - first.Value;
            double? rel = Math.Abs(first.Value) > 1e-12 ? abs / Math.Abs(first.Value) : null;
            double? annual = months > 0 ? abs / (months / 12.0) : null;

            var xs = points.Select(p => (p.Date!.Value.ToDateTime(TimeOnly.MinValue) - first.Date!.Value.ToDateTime(TimeOnly.MinValue)).TotalDays / 30.44).ToList();
            var ys = points.Select(p => p.Value).ToList();

            return Ok(new
            {
                participantId,
                featureName,
                n = points.Count,
                absoluteChange = abs,
                relativeChange = rel,
                annualizedChange = annual,
                slopePerMonth = Statistics.SlopeMonths(xs, ys),
                points = points.Select(p => new { p.VisitId, p.Date, p.Value })
            });
        }
    }
}



