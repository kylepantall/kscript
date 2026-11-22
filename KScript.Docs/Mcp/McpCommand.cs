using System.Text;

namespace KScript.Docs.Mcp
{
    /// <summary>
    /// <c>kscript mcp</c>: serves the KScript docs to coding assistants over the Model Context Protocol, on stdio
    /// (the default, for clients that start the server themselves) or with <c>--http</c> on localhost.
    /// </summary>
    public static class McpCommand
    {
        public const int DefaultPort = 4181;

        public const string Usage = "Usage: kscript mcp [--syntax kscript|xml] [--dir folder] [--http [--port 4181]]";

        /// <summary>Runs the server. <paramref name="input"/> and <paramref name="output"/> carry the protocol on stdio; <paramref name="log"/> is stderr.</summary>
        public static int Run(string[] args, Stream input, Stream output, TextWriter log, CancellationToken stop = default)
        {
            bool http = false;
            int port = DefaultPort;
            string format = null;
            string folder = null;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--help":
                    case "-h":
                        log.WriteLine(Usage);
                        log.WriteLine("Serves the KScript docs to AI coding assistants (MCP), offline. Clients start it as: kscript mcp");
                        log.WriteLine("  --syntax   Show examples as .kscript or XML (default: the project's make.syntax, else .kscript).");
                        log.WriteLine("  --dir      The project folder: its kscript.json sets the syntax and the check/fmt rules (default: this folder).");
                        log.WriteLine("  --http     Serve on http://localhost:4181/mcp instead of stdin and stdout.");
                        log.WriteLine("Setup for Claude Code, Cursor, VS Code and other clients: the \"AI assistants (MCP)\" page of kscript docs.");
                        return 0;
                    case "--http":
                        http = true;
                        break;
                    case "--port":
                    case "-p":
                        if (i + 1 >= args.Length || !int.TryParse(args[++i], out port) || port < 1 || port > 65535)
                        {
                            log.WriteLine("--port needs a number, e.g. --port 4181");
                            return ExitCodes.Usage;
                        }
                        break;
                    case CliFlags.Syntax:
                        format = i + 1 < args.Length ? DocsCommand.NormalizeFormat(args[++i]) : null;
                        if (format == null)
                        {
                            log.WriteLine("--syntax needs kscript or xml, e.g. --syntax xml");
                            return ExitCodes.Usage;
                        }
                        break;
                    case "--dir":
                        folder = i + 1 < args.Length ? args[++i] : null;
                        if (folder == null || !Directory.Exists(folder))
                        {
                            log.WriteLine(folder == null ? "--dir needs a folder, e.g. --dir my-app" : $"There is no folder '{folder}'.");
                            return ExitCodes.Usage;
                        }
                        break;
                    default:
                        log.WriteLine(CliFlags.Retired("mcp", args[i]) ?? $"Unknown option '{args[i]}'. {Usage}");
                        return ExitCodes.Usage;
                }
            }

            folder = Path.GetFullPath(folder ?? KScript.Runtime.WorkingFolder.Current);
            var server = new McpServer(format: format ?? DocsCommand.ProjectFormat(folder), workingDirectory: folder);
            if (http)
            {
                if (!DocsCommand.IsFree(port))
                {
                    log.WriteLine($"Port {port} is in use. Pick another with --port.");
                    return 1;
                }
                server.RunHttpAsync(port, stop, url => log.WriteLine($"KScript docs MCP server: {url}\nPress Ctrl+C to stop.")).GetAwaiter().GetResult();
                return 0;
            }

            // stdout carries the protocol, so nothing else may write to it.
            var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            using var reader = new StreamReader(input, encoding);
            using var writer = new StreamWriter(output, encoding) { NewLine = "\n" };
            try
            {
                server.RunStdioAsync(reader, writer, stop).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                // Stopped with Ctrl+C.
            }
            return 0;
        }
    }
}
