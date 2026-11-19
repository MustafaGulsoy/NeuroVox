using NeuroVox.WebApi.Services;
using Xunit;

namespace NeuroVox.Tests
{
    public class DoctorSegmentsTests
    {
        [Fact]
        public void Normalize_sorts_clamps_merges_and_drops_garbage()
        {
            var r = DoctorSegments.Normalize([new(8, 12), new(1, 3), new(2, 5), new(-4, 0.01), new(double.NaN, 3), new(6, 6.01)], duration: 10);
            Assert.Equal([new SegmentDto(1, 5), new SegmentDto(8, 10)], r);
        }

        [Fact]
        public void Parse_roundtrips_and_tolerates_garbage()
        {
            var s = new[] { new SegmentDto(1.5, 4) };
            Assert.Equal(s, DoctorSegments.Parse(DoctorSegments.ToJson(s)));
            Assert.Empty(DoctorSegments.Parse("not json"));
            Assert.Empty(DoctorSegments.Parse(null));
        }
    }
}
