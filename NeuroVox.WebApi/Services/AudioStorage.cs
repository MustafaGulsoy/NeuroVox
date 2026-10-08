namespace NeuroVox.WebApi.Services
{
    // Audio lives under one root; the DB stores only the file name (never a client-supplied path).
    public class AudioStorage(IConfiguration config, IWebHostEnvironment env)
    {
        public string Root { get; } = Path.GetFullPath(
            config["NeuroVox:AudioStoragePath"] ?? Path.Combine(env.ContentRootPath, "data", "audio"));

        public string Resolve(string fileName)
        {
            var full = Path.GetFullPath(Path.Combine(Root, Path.GetFileName(fileName)));
            return full.StartsWith(Root, StringComparison.Ordinal) ? full : throw new InvalidOperationException("Invalid audio path");
        }
    }
}
