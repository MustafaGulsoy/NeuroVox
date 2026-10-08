namespace NeuroVox.Application.Services.Audio
{
    // Validates uploads by extension AND magic bytes (a renamed .exe must not pass).
    public static class AudioFileValidator
    {
        public static readonly string[] AllowedExtensions = { ".wav", ".mp3", ".m4a", ".ogg", ".flac", ".webm" };

        public static bool IsValid(string fileName, ReadOnlySpan<byte> header)
        {
            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            if (Array.IndexOf(AllowedExtensions, ext) < 0 || header.Length < 12) return false;

            return ext switch
            {
                ".wav" => header[..4].SequenceEqual("RIFF"u8) && header.Slice(8, 4).SequenceEqual("WAVE"u8),
                ".mp3" => header[..3].SequenceEqual("ID3"u8) || (header[0] == 0xFF && (header[1] & 0xE0) == 0xE0),
                ".m4a" => header.Slice(4, 4).SequenceEqual("ftyp"u8),
                ".ogg" => header[..4].SequenceEqual("OggS"u8),
                ".flac" => header[..4].SequenceEqual("fLaC"u8),
                ".webm" => header[0] == 0x1A && header[1] == 0x45 && header[2] == 0xDF && header[3] == 0xA3,
                _ => false
            };
        }
    }
}
