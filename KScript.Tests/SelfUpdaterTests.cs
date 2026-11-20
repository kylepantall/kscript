using System;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using KScript.Updates;
using Xunit;

namespace KScript.Tests
{
    public class SelfUpdaterTests
    {
        [Theory]
        [InlineData("v0.2.0", "0.1.0", true)]
        [InlineData("v0.1.0", "0.1.0", false)]
        [InlineData("v0.1.0", "0.2.0", false)]
        [InlineData("v1.0.0", "0.9.9", true)]
        [InlineData("v0.1.1", "0.1.0", true)]
        [InlineData("v0.1.0", "0.0.0-dev", true)]
        [InlineData("v0.10.0", "0.9.0", true)]
        [InlineData("v0.2", "0.1.5", true)]
        [InlineData("not-a-version", "0.1.0", false)]
        public void Compares_release_tags(string latest, string current, bool newer)
        {
            Assert.Equal(newer, SelfUpdater.IsNewer(latest, current));
        }

        [Fact]
        public void Maps_platforms_to_release_runtime_ids()
        {
            Assert.Equal("linux-x64", SelfUpdater.RuntimeId(OSPlatform.Linux, Architecture.X64));
            Assert.Equal("linux-arm64", SelfUpdater.RuntimeId(OSPlatform.Linux, Architecture.Arm64));
            Assert.Equal("osx-arm64", SelfUpdater.RuntimeId(OSPlatform.OSX, Architecture.Arm64));
            Assert.Equal("osx-x64", SelfUpdater.RuntimeId(OSPlatform.OSX, Architecture.X64));
            Assert.Equal("win-x64", SelfUpdater.RuntimeId(OSPlatform.Windows, Architecture.X64));
            Assert.Equal("win-x64", SelfUpdater.RuntimeId(OSPlatform.Windows, Architecture.Arm64));
            Assert.Null(SelfUpdater.RuntimeId(OSPlatform.Linux, Architecture.X86));
            Assert.Equal("kscript-win-x64.zip", SelfUpdater.AssetName("win-x64"));
            Assert.Equal("kscript-osx-arm64.tar.gz", SelfUpdater.AssetName("osx-arm64"));
        }

        [Fact]
        public void Extracts_the_binary_from_a_tarball()
        {
            string dir = Directory.CreateTempSubdirectory("kscript-update-test").FullName;
            try
            {
                string src = Path.Combine(dir, "kscript");
                File.WriteAllText(src, "binary");
                var archive = new MemoryStream();
                using (var gz = new GZipStream(archive, CompressionLevel.Fastest, leaveOpen: true))
                using (var tar = new TarWriter(gz))
                {
                    tar.WriteEntry(src, "kscript");
                }
                archive.Position = 0;

                string dest = Path.Combine(dir, "out");
                SelfUpdater.ExtractBinary(archive, "kscript-linux-x64.tar.gz", dest);
                Assert.Equal("binary", File.ReadAllText(dest));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void Extracts_the_exe_from_a_zip()
        {
            string dir = Directory.CreateTempSubdirectory("kscript-update-test").FullName;
            try
            {
                var archive = new MemoryStream();
                using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, leaveOpen: true))
                using (var w = new StreamWriter(zip.CreateEntry("kscript.exe").Open()))
                {
                    w.Write("exe");
                }
                archive.Position = 0;

                string dest = Path.Combine(dir, "out.exe");
                SelfUpdater.ExtractBinary(archive, "kscript-win-x64.zip", dest);
                Assert.Equal("exe", File.ReadAllText(dest));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void Replaces_the_installed_binary()
        {
            string dir = Directory.CreateTempSubdirectory("kscript-update-test").FullName;
            try
            {
                string target = Path.Combine(dir, "kscript");
                string staged = Path.Combine(dir, ".kscript-update");
                File.WriteAllText(target, "old");
                File.WriteAllText(staged, "new");

                SelfUpdater.ReplaceBinary(staged, target, windows: false);

                Assert.Equal("new", File.ReadAllText(target));
                Assert.False(File.Exists(staged));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
