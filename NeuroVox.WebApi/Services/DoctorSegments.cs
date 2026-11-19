using System.Text.Json;

namespace NeuroVox.WebApi.Services
{
    // Doctor (interviewer) speech ranges stored on SpeechRecording.DoctorSegmentsJson.
    public static class DoctorSegments
    {
        private static readonly JsonSerializerOptions Opt = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };

        public static string ToJson(IEnumerable<SegmentDto> segments) => JsonSerializer.Serialize(segments, Opt);

        /// <summary>Null/blank/garbage -> empty list (nothing to cut).</summary>
        public static List<SegmentDto> Parse(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return [];
            try { return JsonSerializer.Deserialize<List<SegmentDto>>(json, Opt) ?? []; }
            catch (JsonException) { return []; }
        }

        /// <summary>Sorted, clamped to [0, duration] (duration &lt;= 0 = unknown), overlapping/touching ranges merged, empties dropped.</summary>
        public static List<SegmentDto> Normalize(IEnumerable<SegmentDto> input, double duration)
        {
            var max = duration > 0 ? duration : double.MaxValue;
            var list = input.Where(s => double.IsFinite(s.Start) && double.IsFinite(s.End))
                .Select(s => new SegmentDto(Math.Max(0, s.Start), Math.Min(max, s.End)))
                .Where(s => s.End - s.Start >= 0.05).OrderBy(s => s.Start).ToList();
            var merged = new List<SegmentDto>();
            foreach (var s in list)
            {
                if (merged.Count > 0 && s.Start <= merged[^1].End) merged[^1] = merged[^1] with { End = Math.Max(merged[^1].End, s.End) };
                else merged.Add(s);
            }
            return merged;
        }
    }
}
