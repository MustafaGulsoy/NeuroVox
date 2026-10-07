using NeuroVox.Application.Services.Analytics;
using Xunit;

namespace NeuroVox.Tests
{
    public class StatisticsTests
    {
        [Fact]
        public void CohensKappa_PerfectAgreement_IsOne()
        {
            var a = new List<int> { 0, 1, 1, 0, 2, 2 };
            var b = new List<int> { 0, 1, 1, 0, 2, 2 };
            Assert.Equal(1.0, Statistics.CohensKappa(a, b), 6);
        }

        [Fact]
        public void FleissKappa_AllRatersAgree_IsOne()
        {
            var counts = new int[,] { { 3, 0, 0 }, { 0, 3, 0 }, { 0, 0, 3 } };
            Assert.Equal(1.0, Statistics.FleissKappa(counts, 3, 3), 6);
        }

        [Fact]
        public void WeightedKappa_PerfectAgreement_IsOne()
        {
            var a = new List<int> { 0, 1, 2, 2, 1 };
            var b = new List<int> { 0, 1, 2, 2, 1 };
            Assert.Equal(1.0, Statistics.WeightedKappa(a, b), 6);
        }

        [Fact]
        public void Icc_IdenticalRaters_IsHigh()
        {
            var x = new double[,] { { 5, 5 }, { 8, 8 }, { 3, 3 }, { 7, 7 } };
            Assert.True(Statistics.IccTwoWayRandomSingle(x) > 0.99);
        }

        [Fact]
        public void Pearson_PerfectLinear_IsOne()
        {
            var x = new List<double> { 1, 2, 3, 4 };
            var y = new List<double> { 2, 4, 6, 8 };
            Assert.Equal(1.0, Statistics.Pearson(x, y), 6);
        }

        [Fact]
        public void Spearman_Monotonic_IsOne()
        {
            var x = new List<double> { 1, 2, 3, 4 };
            var y = new List<double> { 1, 4, 9, 16 };
            Assert.Equal(1.0, Statistics.Spearman(x, y), 6);
        }

        [Fact]
        public void SlopeMonths_LinearGrowth_MatchesExpected()
        {
            var months = new List<double> { 0, 6, 12 };
            var values = new List<double> { 0, 5, 10 };
            Assert.Equal(10.0 / 12.0, Statistics.SlopeMonths(months, values), 6);
        }
    }
}
