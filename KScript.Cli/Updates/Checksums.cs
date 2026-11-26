using System;
using System.IO;
using System.Security.Cryptography;

namespace KScript.Updates
{
    /// <summary>A download whose SHA-256 isn't the one its release lists.</summary>
    internal sealed class ChecksumException : Exception
    {
        public ChecksumException(string message) : base(message) { }
    }

    /// <summary>
    /// A release lists the SHA-256 of each of its files in SHA256SUMS (the output of sha256sum, written by
    /// .github/workflows/release.yml). Everything kscript downloads from a release is checked against it before it is
    /// unpacked: kscript update, the runtimes kscript compile fetches, and install.sh / install.ps1 do the same.
    /// </summary>
    internal static class Checksums
    {
        public const string FileName = "SHA256SUMS";

        /// <summary>The first release that has to have a SHA256SUMS file. Earlier ones were published without.</summary>
        private static readonly Version FirstListed = new Version(0, 26, 0);

        /// <summary>Whether a release must list its checksums: one that doesn't is refused, not installed unchecked.</summary>
        public static bool Required(string version) => SelfUpdater.TryParseVersion(version, out Version parsed) && parsed >= FirstListed;

        /// <summary>The SHA-256 a SHA256SUMS file lists for a file name, in lower case, or null when it doesn't list it.</summary>
        public static string Listed(string sums, string name)
        {
            foreach (string raw in (sums ?? "").Split('\n'))
            {
                string line = raw.Trim();
                int gap = line.IndexOfAny(new[] { ' ', '\t' });
                if (gap <= 0) continue;
                // sha256sum writes "<hash>  <name>", or "<hash> *<name>" for a file read as binary.
                string listed = line.Substring(gap).Trim().TrimStart('*');
                if (listed.StartsWith("./", StringComparison.Ordinal)) listed = listed.Substring(2);
                if (listed == name) return line.Substring(0, gap).ToLowerInvariant();
            }
            return null;
        }

        public static string Of(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

        /// <summary>Throws unless <paramref name="data"/> is the file SHA256SUMS lists under <paramref name="name"/>.</summary>
        public static void Verify(byte[] data, string name, string sums)
        {
            string expected = Listed(sums, name);
            if (expected == null) throw new ChecksumException($"{FileName} doesn't list {name}, so the download can't be checked. Nothing was installed.");
            string actual = Of(data);
            if (actual != expected)
                throw new ChecksumException($"The checksum of {name} is wrong: {FileName} lists {expected} but the download is {actual}. It may be damaged or not the released file. Nothing was installed.");
        }

        /// <summary>What to say when a release has no SHA256SUMS file.</summary>
        public static string Missing(string release) => $"Release {release} has no {FileName} file, so the download can't be checked. Nothing was installed.";

        public static string Unchecked(string release) => $"Release {release} was published before releases listed checksums, so the download wasn't checked.";
    }
}
