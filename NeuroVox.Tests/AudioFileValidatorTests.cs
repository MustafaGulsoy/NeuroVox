using System.Text;
using NeuroVox.Application.Services.Audio;

namespace NeuroVox.Tests
{
    public class AudioFileValidatorTests
    {
        private static byte[] Bytes(string head) => Encoding.ASCII.GetBytes(head.PadRight(16, '\0'));

        [Fact]
        public void RealWav_IsAccepted() =>
            Assert.True(AudioFileValidator.IsValid("a.WAV", Bytes("RIFF\0\0\0\0WAVE")));

        [Fact]
        public void ExecutableRenamedToWav_IsRejected() =>
            Assert.False(AudioFileValidator.IsValid("evil.wav", Bytes("MZ\u0090\0\u0003")));

        [Fact]
        public void UnknownExtension_IsRejected() =>
            Assert.False(AudioFileValidator.IsValid("a.exe", Bytes("RIFF\0\0\0\0WAVE")));

        [Fact]
        public void TruncatedHeader_IsRejected() =>
            Assert.False(AudioFileValidator.IsValid("a.wav", new byte[] { 0x52, 0x49 }));
    }
}
