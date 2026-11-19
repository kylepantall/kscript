using System;
using System.Diagnostics;
using System.IO;

namespace KScript.Helpers
{
    /// <summary>
    /// Cross-platform helpers for console and process interaction.
    /// Console APIs such as Clear/ReadKey/WindowWidth throw when input or output is redirected
    /// (pipes, CI, tests) or on terminals that do not support them, so they are wrapped here.
    /// </summary>
    internal static class Platform
    {
        /// <summary>Clears the console when attached to a terminal; does nothing otherwise.</summary>
        public static void ClearConsole()
        {
            if (Console.IsOutputRedirected)
            {
                return;
            }

            try { Console.Clear(); }
            catch (IOException) { }
            catch (PlatformNotSupportedException) { }
        }

        /// <summary>Waits for a key press, or consumes one character when input is redirected.</summary>
        public static void WaitForKey()
        {
            if (Console.IsInputRedirected)
            {
                Console.In.Read();
                return;
            }

            try { Console.ReadKey(true); }
            catch (InvalidOperationException) { Console.In.Read(); }
        }

        /// <summary>The console width, or a sensible default when it cannot be determined.</summary>
        public static int ConsoleWidth(int fallback = 80)
        {
            if (Console.IsOutputRedirected)
            {
                return fallback;
            }

            try
            {
                int width = Console.WindowWidth;
                return width > 0 ? width : fallback;
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        /// <summary>
        /// Opens a file, directory, URL or program with the platform's default handler
        /// (Explorer/ShellExecute on Windows, xdg-open on Linux, open on macOS).
        /// </summary>
        public static Process Open(string target, string arguments = null)
        {
            ProcessStartInfo info = new ProcessStartInfo(target)
            {
                // .NET Core defaults UseShellExecute to false, which cannot open URLs, documents or directories.
                UseShellExecute = true
            };

            if (!string.IsNullOrWhiteSpace(arguments))
            {
                info.Arguments = arguments;
            }

            return Process.Start(info);
        }

        /// <summary>
        /// Resolves a path relative to the current directory, falling back to the script's directory.
        /// </summary>
        public static string ResolvePath(string path, string scriptDirectory)
        {
            if (string.IsNullOrEmpty(path) || Path.IsPathRooted(path) || File.Exists(path) || Directory.Exists(path))
            {
                return path;
            }

            if (!string.IsNullOrEmpty(scriptDirectory))
            {
                string candidate = Path.Combine(scriptDirectory, path);
                if (File.Exists(candidate) || Directory.Exists(candidate))
                {
                    return candidate;
                }
            }

            return path;
        }

        public static bool IsHttpUrl(string value) =>
            Uri.TryCreate(value, UriKind.Absolute, out Uri uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}
