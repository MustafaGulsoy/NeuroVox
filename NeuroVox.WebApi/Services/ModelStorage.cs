using System.IO.Compression;

namespace NeuroVox.WebApi.Services
{
    // Trained artifacts live under one root: <root>/<customer>/<run>/ plus <root>/current/, which the local AI container
    // mounts read-only for /predict.
    // ponytail: `current` is global (latest completed run of any tenant); make it per-tenant when a second tenant trains.
    public class ModelStorage(IConfiguration config, IWebHostEnvironment env)
    {
        public string Root { get; } = Path.GetFullPath(config["NeuroVox:ModelStoragePath"] ?? Path.Combine(env.ContentRootPath, "data", "models"));
        private static readonly System.Text.RegularExpressions.Regex Allowed = new(@"^[A-Za-z0-9_.-]{1,80}$");

        /// <summary>Feature columns of the current model, in training order.</summary>
        public bool TryReadFeatureOrder(out List<string> order)
        {
            order = [];
            var path = Path.Combine(Root, "current", "feature_order.json");
            if (!File.Exists(path)) return false;
            order = System.Text.Json.JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path)) ?? [];
            return order.Count > 0;
        }

        /// <summary>Current model file and feature order, for hosts that do not mount the model volume.</summary>
        public (byte[] Model, string FeatureOrder)? ReadCurrent(string name)
        {
            if (!Allowed.IsMatch(name)) return null;
            var m = Path.Combine(Root, "current", name + ".joblib");
            var f = Path.Combine(Root, "current", "feature_order.json");
            return File.Exists(m) && File.Exists(f) ? (File.ReadAllBytes(m), File.ReadAllText(f)) : null;
        }

        /// <summary>Extracts a trusted-format zip (flat files only, no paths) and publishes it as `current`. Returns the run-relative path and report.json text.</summary>
        public (string RelativePath, string Report) Save(Guid customerId, Guid runId, byte[] zip)
        {
            var rel = Path.Combine(customerId.ToString("N"), runId.ToString("N"));
            var dir = Path.Combine(Root, rel);
            Directory.CreateDirectory(dir);
            using (var za = new ZipArchive(new MemoryStream(zip)))
            {
                long total = 0;
                foreach (var e in za.Entries.Where(e => e.Length > 0))
                {
                    var name = Path.GetFileName(e.FullName);   // never trust paths from the archive (zip-slip)
                    if (!Allowed.IsMatch(name) || (total += e.Length) > 200_000_000) throw new InvalidDataException("unexpected archive content");
                    e.ExtractToFile(Path.Combine(dir, name), true);
                }
            }
            var reportPath = Path.Combine(dir, "report.json");
            if (!File.Exists(reportPath)) throw new InvalidDataException("archive has no report.json");

            var cur = Path.Combine(Root, "current");
            Directory.CreateDirectory(cur);
            foreach (var f in Directory.GetFiles(dir)) File.Copy(f, Path.Combine(cur, Path.GetFileName(f)), true);
            return (rel, File.ReadAllText(reportPath));
        }
    }
}
