using NeuroVox.WebApi.Background;
using Xunit;

namespace NeuroVox.Tests
{
    public class AnalysisQueueTests
    {
        [Fact]
        public async Task Sending_the_same_job_again_while_it_waits_is_absorbed()
        {
            var q = new AnalysisQueue();
            var id = Guid.NewGuid();
            await q.EnqueueAsync(id); await q.EnqueueAsync(id); await q.EnqueueAsync(id);
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
            var seen = new List<Guid>();
            try { await foreach (var j in q.ReadAllAsync(cts.Token)) seen.Add(j.Id); } catch (OperationCanceledException) { }
            Assert.Single(seen);

            await q.EnqueueAsync(id);   // once taken off the queue it can be queued again
            using var cts2 = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
            try { await foreach (var j in q.ReadAllAsync(cts2.Token)) seen.Add(j.Id); } catch (OperationCanceledException) { }
            Assert.Equal(2, seen.Count);
        }
    }
}
