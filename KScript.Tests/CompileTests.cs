using System.Buffers.Binary;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

using KScript.Compile;

namespace KScript.Tests;

/// <summary>
/// kscript compile: package selection, the bundle it writes, the Windows and Mac launchers it edits, icons, and
/// where runtimes come from. The runtimes here are small stand-ins with the same structure as a release kscript.
/// </summary>
public sealed class CompileTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("kscript-compile").FullName;
    private readonly string cwd = Directory.GetCurrentDirectory();

    public void Dispose()
    {
        Directory.SetCurrentDirectory(cwd);
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }

    private string Write(string name, string text) => WriteBytes(name, Encoding.UTF8.GetBytes(text));

    private string WriteBytes(string name, byte[] data)
    {
        string path = Path.Combine(dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, data);
        return path;
    }

    #region Stand-in runtimes

    private static byte[] Marker() => new byte[8].Concat(SingleFileBundle.Signature).ToArray();

    private static byte[] ElfHost()
    {
        var host = new byte[256];
        new byte[] { 0x7F, (byte)'E', (byte)'L', (byte)'F' }.CopyTo(host, 0);
        Marker().CopyTo(host, 64);
        return host;
    }

    /// <summary>A two-section PE32+ launcher: .text with the bundle marker, and .rsrc with a version and a named resource.</summary>
    internal static byte[] PeHost(bool resources = true, int sections = 2, uint headersSize = 0x400)
    {
        var image = new byte[0x800];
        image[0] = (byte)'M'; image[1] = (byte)'Z';
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x3C), 0x40);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(0x40), 0x00004550);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(0x44), 0x8664);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(0x46), (ushort)sections);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(0x54), 0xF0);
        int opt = 0x58;
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(opt), 0x20B);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(opt + 32), 0x1000);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(opt + 36), 0x200);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(opt + 56), 0x3000);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(opt + 60), headersSize);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(opt + 68), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(opt + 108), 16);
        void Section(int index, string name, uint va, uint raw)
        {
            int s = opt + 0xF0 + index * 40;
            Encoding.ASCII.GetBytes(name).CopyTo(image, s);
            BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(s + 8), 0x200);
            BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(s + 12), va);
            BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(s + 16), 0x200);
            BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(s + 20), raw);
        }
        Section(0, ".text", 0x1000, 0x400);
        Marker().CopyTo(image, 0x480);
        if (sections > 1) Section(1, ".rsrc", 0x2000, 0x600);
        if (resources)
        {
            byte[] rsrc = WindowsExecutable.Serialize(new List<WindowsExecutable.Resource>
            {
                new(16, 1, 0x0409, new byte[] { 1, 2, 3 }),
                new(new WindowsExecutable.ResourceKey(0, "CONFIG"), new WindowsExecutable.ResourceKey(0, "MAIN"), 0, new byte[] { 9 }),
                new(24, 1, new WindowsExecutable.ResourceKey(0, "LANG"), new byte[] { 7, 7 }),
            }, 0x2000);
            rsrc.CopyTo(image, 0x600);
            BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(opt + 112 + 16), 0x2000);
            BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(opt + 112 + 20), (uint)rsrc.Length);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(opt + 112 + 32), 0x700); // a certificate table to drop
        return image;
    }

    /// <summary>A Mach-O launcher with __TEXT and __LINKEDIT, and a code signature command unless asked not to.</summary>
    internal static byte[] MachOHost(bool signature = true, bool room = true)
    {
        var file = new byte[0x1100];
        BinaryPrimitives.WriteUInt32LittleEndian(file, 0xFEEDFACF);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(4), 0x0100000C);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(12), 2);
        int at = 32, commands = 0;
        void Segment(string name, ulong fileoff, ulong filesize, int sections, uint sectionOffset)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(at), 0x19);
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(at + 4), (uint)(72 + sections * 80));
            Encoding.ASCII.GetBytes(name).CopyTo(file, at + 8);
            BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(at + 40), fileoff);
            BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(at + 48), filesize);
            BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(at + 64), sections);
            for (int s = 0; s < sections; s++) BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(at + 72 + s * 80 + 48), sectionOffset);
            at += 72 + sections * 80;
            commands++;
        }
        Segment("__PAGEZERO", 0, 0, 0, 0);
        Segment("__TEXT", 0, 0x1000, 2, room ? 0x300u : (uint)(32 + 72 + 72 + 160 + 72 + 8));
        Segment("__LINKEDIT", 0x1000, 0x100, 0, 0);
        if (signature)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(at), 0x1D);
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(at + 4), 16);
            at += 16;
            commands++;
        }
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(16), commands);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(20), at - 32);
        Marker().CopyTo(file, 0x400);
        return file;
    }

    private const string Deps = """
        {
          "runtimeTarget": { "name": ".NETCoreApp,Version=v8.0/linux-x64" },
          "targets": {
            ".NETCoreApp,Version=v8.0": {},
            ".NETCoreApp,Version=v8.0/linux-x64": {
              "KScript/0.4.0": { "dependencies": { "KScript.Cli": "0.4.0", "KScript.UI": "0.4.0", "KScript.Http": "0.4.0" }, "runtime": { "KScript.dll": {} } },
              "runtimepack.Microsoft.NETCore.App.Runtime.linux-x64/8.0.0": { "runtime": { "System.Private.CoreLib.dll": {} }, "native": { "libcoreclr.so": {} } },
              "runtimepack.Microsoft.AspNetCore.App.Runtime.linux-x64/8.0.0": { "runtime": { "Microsoft.AspNetCore.dll": {} } },
              "System.Text.Json/10.0.0": { "dependencies": { "System.IO.Pipelines": "10.0.0" }, "runtime": { "lib/net8.0/System.Text.Json.dll": {} } },
              "System.IO.Pipelines/10.0.0": { "runtime": { "lib/net8.0/System.IO.Pipelines.dll": {} } },
              "System.Memory.Data/1.0.0": {},
              "Avalonia/11.0.0": { "runtime": { "lib/net8.0/Avalonia.dll": {} } },
              "SkiaSharp.NativeAssets.Linux/2.0.0": { "native": { "runtimes/linux-x64/native/libSkiaSharp.so": {} } },
              "KScript.Core/0.4.0": { "runtime": { "KScript.Core.dll": {} } },
              "KScript.Math/0.4.0": { "dependencies": { "KScript.Core": "0.4.0" }, "runtime": { "KScript.Math.dll": {} } },
              "KScript.UI/0.4.0": { "dependencies": { "Avalonia": "11.0.0", "SkiaSharp.NativeAssets.Linux": "2.0.0", "KScript.Core": "0.4.0" }, "runtime": { "KScript.UI.dll": {} } },
              "KScript.Http/0.4.0": { "runtime": { "KScript.Http.dll": {} } },
              "KScript.Cli/0.4.0": { "dependencies": { "KScript.Core": "0.4.0" }, "runtime": { "KScript.Cli.dll": {} }, "resources": { "de/KScript.Cli.resources.dll": { "locale": "de" }, "Other.resources.dll": {} } }
            }
          },
          "libraries": {
            "KScript/0.4.0": { "type": "project" },
            "runtimepack.Microsoft.NETCore.App.Runtime.linux-x64/8.0.0": { "type": "runtimepack" },
            "runtimepack.Microsoft.AspNetCore.App.Runtime.linux-x64/8.0.0": { "type": "runtimepack" },
            "System.Text.Json/10.0.0": { "type": "package" },
            "System.IO.Pipelines/10.0.0": { "type": "package" },
            "System.Memory.Data/1.0.0": { "type": "package" },
            "Avalonia/11.0.0": { "type": "package" },
            "SkiaSharp.NativeAssets.Linux/2.0.0": { "type": "package" },
            "KScript.Core/0.4.0": { "type": "project" },
            "KScript.Math/0.4.0": { "type": "project" },
            "KScript.UI/0.4.0": { "type": "project" },
            "KScript.Http/0.4.0": { "type": "project" },
            "KScript.Cli/0.4.0": { "type": "project" },
            "Unknown/1.0.0": {}
          }
        }
        """;

    private static byte[] Deflate(byte[] data)
    {
        var output = new MemoryStream();
        using (var d = new DeflateStream(output, CompressionLevel.Fastest, leaveOpen: true)) d.Write(data);
        return output.ToArray();
    }

    /// <summary>A stand-in release kscript: host, assemblies, a native library, deps.json and runtimeconfig.json.</summary>
    internal static SingleFileBundle Runtime(byte[] host, bool supportsPrograms = true, string config = """{ "runtimeOptions": { "tfm": "net8.0", "configProperties": { "A": true } } }""")
    {
        var bundle = new SingleFileBundle { Host = host, BundleId = "test" };
        byte[] entry = Encoding.Unicode.GetBytes(supportsPrograms ? "...KScript.CompiledApp..." : "...");
        bundle.Entries.Add(new BundleEntry { RelativePath = "KScript.dll", Type = BundleFileType.Assembly, Size = entry.Length, CompressedSize = Deflate(entry).Length, Data = Deflate(entry) });
        foreach (string name in new[] { "System.Private.CoreLib.dll", "Microsoft.AspNetCore.dll", "System.Text.Json.dll", "System.IO.Pipelines.dll", "Avalonia.dll",
                     "KScript.Core.dll", "KScript.Math.dll", "KScript.UI.dll", "KScript.Http.dll", "KScript.Cli.dll", "de/KScript.Cli.resources.dll", "Other.resources.dll" })
            bundle.Entries.Add(BundleEntry.FromBytes(name, BundleFileType.Assembly, Encoding.UTF8.GetBytes(name)));
        bundle.Entries.Add(BundleEntry.FromBytes("libSkiaSharp.so", BundleFileType.NativeBinary, new byte[] { 1 }));
        bundle.Entries.Add(BundleEntry.FromBytes("kscript-app/kscript-app.json", BundleFileType.Unknown, new byte[] { 2 }));
        bundle.Entries.Add(BundleEntry.FromBytes("KScript.deps.json", BundleFileType.DepsJson, Encoding.UTF8.GetBytes(Deps)));
        if (config != null) bundle.Entries.Add(BundleEntry.FromBytes("KScript.runtimeconfig.json", BundleFileType.RuntimeConfigJson, Encoding.UTF8.GetBytes(config)));
        return bundle;
    }

    private string RuntimeFile(string name, byte[] host, bool supportsPrograms = true) => WriteBytes(name, Runtime(host, supportsPrograms).ToBytes());

    private static ProgramPlan Plan(ProgramKind kind, params string[] packages)
    {
        var plan = new ProgramPlan { Name = "Demo", FileName = "Demo", Id = "app.demo", Kind = kind, Main = "main.ks.xml" };
        plan.Packages.Add("KScript.Core");
        foreach (string p in packages) plan.Packages.Add(p);
        plan.Files["main.ks.xml"] = Encoding.UTF8.GetBytes("<kscript />");
        return plan;
    }

    private static List<string> Paths(SingleFileBundle b) => b.Entries.Select(e => e.RelativePath).ToList();

    #endregion

    #region The bundle

    [Fact]
    public void Bundles_round_trip_and_keep_compressed_files_compressed()
    {
        SingleFileBundle original = Runtime(ElfHost());
        byte[] bytes = original.ToBytes();
        SingleFileBundle read = SingleFileBundle.Read(bytes, "kscript");
        Assert.Equal(Paths(original), Paths(read));
        Assert.Equal("test", read.BundleId);
        Assert.Equal(6u, read.MajorVersion);
        Assert.Equal(ElfHost(), read.Host.Take(256).ToArray());
        BundleEntry entry = read.Find("kscript.dll")!;
        Assert.NotEqual(0, entry.CompressedSize);
        Assert.Equal("...KScript.CompiledApp...", Encoding.Unicode.GetString(entry.Content()));
        Assert.Equal(BundleFileType.NativeBinary, read.FindType(BundleFileType.NativeBinary)!.Type);
        Assert.Null(read.Find("nope"));

        // Assemblies start on 4 KB boundaries.
        long first = BitConverter.ToInt64(bytes, SingleFileBundle.MarkerOffset(bytes));
        Assert.True(first > 0);

        // Without deps.json or runtimeconfig.json in the bundle, their offsets are 0.
        var bare = new SingleFileBundle { Host = ElfHost() };
        bare.Entries.Add(BundleEntry.FromBytes("a.txt", BundleFileType.Unknown, new byte[] { 1 }));
        SingleFileBundle back = SingleFileBundle.Read(bare.ToBytes(), "bare");
        Assert.Equal(bare.ComputeId(), back.BundleId);
    }

    [Fact]
    public void Files_that_are_not_bundles_are_refused()
    {
        Assert.Contains("isn't a single-file kscript", Assert.Throws<CompileException>(() => SingleFileBundle.Read(new byte[100], "x")).Message);
        byte[] bad = ElfHost();
        BitConverter.GetBytes((long)bad.Length + 10).CopyTo(bad, 64);
        Assert.Throws<CompileException>(() => SingleFileBundle.Read(bad, "x"));
        Assert.Throws<CompileException>(() => SingleFileBundle.Read(Marker().Skip(4).ToArray(), "x"));

        SingleFileBundle future = Runtime(ElfHost());
        future.MajorVersion = 7;
        Assert.Contains("bundle format 7", Assert.Throws<CompileException>(() => SingleFileBundle.Read(future.ToBytes(), "x")).Message);

        Assert.Contains("no bundle marker", Assert.Throws<CompileException>(() => new SingleFileBundle { Host = new byte[10] }.ToBytes()).Message);
        Assert.Equal(-1, SingleFileBundle.IndexOf(new byte[] { 1, 2, 3 }, new byte[] { 2, 4 }, 0));
    }

    #endregion

    #region Packaging

    [Fact]
    public void Terminal_programs_keep_only_the_framework_core_and_their_packages()
    {
        SingleFileBundle program = ProgramPackager.Package(Runtime(ElfHost()), Plan(ProgramKind.Script, "KScript.Math"), out List<string> dropped);
        List<string> paths = Paths(program);
        foreach (string kept in new[] { "KScript.dll", "System.Private.CoreLib.dll", "System.Text.Json.dll", "System.IO.Pipelines.dll", "KScript.Core.dll", "KScript.Math.dll" })
            Assert.Contains(kept, paths);
        foreach (string gone in new[] { "KScript.UI.dll", "Avalonia.dll", "libSkiaSharp.so", "KScript.Http.dll", "KScript.Cli.dll", "de/KScript.Cli.resources.dll", "Microsoft.AspNetCore.dll", "Other.resources.dll" })
        {
            Assert.DoesNotContain(gone, paths);
            Assert.Contains(gone, dropped);
        }
        Assert.Contains("kscript-app/app/main.ks.xml", paths);
        Assert.Equal(1, paths.Count(p => p == "kscript-app/kscript-app.json"));

        JsonObject deps = JsonNode.Parse(program.FindType(BundleFileType.DepsJson)!.Content())!.AsObject();
        JsonObject target = deps["targets"]![".NETCoreApp,Version=v8.0/linux-x64"]!.AsObject();
        Assert.False(target.ContainsKey("KScript.UI/0.4.0"));
        Assert.Empty(target["KScript/0.4.0"]!["dependencies"]!.AsObject());
        Assert.False(deps["libraries"]!.AsObject().ContainsKey("KScript.Cli/0.4.0"));

        JsonNode config = JsonNode.Parse(program.FindType(BundleFileType.RuntimeConfigJson)!.Content())!;
        Assert.Equal("kscript-app/kscript-app.json", config["runtimeOptions"]!["configProperties"]![ProgramPackager.ConfigProperty]!.GetValue<string>());
        Assert.True(config["runtimeOptions"]!["configProperties"]!["A"]!.GetValue<bool>());

        JsonNode manifest = JsonNode.Parse(program.Find("kscript-app/kscript-app.json")!.Content())!;
        Assert.Equal("script", manifest["kind"]!.GetValue<string>());
        Assert.Equal("app/main.ks.xml", manifest["main"]!.GetValue<string>());
        Assert.NotEqual("test", program.BundleId);
    }

    [Fact]
    public void Desktop_apps_keep_the_ui_libraries_and_sites_keep_aspnet()
    {
        List<string> window = Paths(ProgramPackager.Package(Runtime(ElfHost()), Plan(ProgramKind.Window, "KScript.UI"), out _));
        Assert.Contains("Avalonia.dll", window);
        Assert.Contains("libSkiaSharp.so", window);
        Assert.DoesNotContain("Microsoft.AspNetCore.dll", window);

        List<string> site = Paths(ProgramPackager.Package(Runtime(ElfHost()), Plan(ProgramKind.Site, "KScript.Http"), out _));
        Assert.Contains("Microsoft.AspNetCore.dll", site);
        Assert.Contains("KScript.Http.dll", site);
    }

    [Fact]
    public void Runtimes_that_cannot_run_programs_are_refused()
    {
        Assert.Contains("0.4.0 or later", Assert.Throws<CompileException>(() => ProgramPackager.Package(Runtime(ElfHost(), supportsPrograms: false), Plan(ProgramKind.Script), out _)).Message);
        Assert.Contains("runtimeconfig", Assert.Throws<CompileException>(() => ProgramPackager.Package(Runtime(ElfHost(), config: null), Plan(ProgramKind.Script), out _)).Message);

        SingleFileBundle noDeps = Runtime(ElfHost());
        noDeps.Entries.RemoveAll(e => e.Type == BundleFileType.DepsJson);
        Assert.Contains("deps.json", Assert.Throws<CompileException>(() => ProgramPackager.Package(noDeps, Plan(ProgramKind.Script), out _)).Message);

        SingleFileBundle noEntry = Runtime(ElfHost());
        noEntry.Entries.RemoveAll(e => e.RelativePath == "KScript.dll");
        Assert.Contains("KScript.dll", Assert.Throws<CompileException>(() => ProgramPackager.Package(noEntry, Plan(ProgramKind.Script), out _)).Message);

        // A runtimeconfig.json without options gets them.
        SingleFileBundle plain = ProgramPackager.Package(Runtime(ElfHost(), config: "{}"), Plan(ProgramKind.Script), out _);
        Assert.Contains(ProgramPackager.ConfigProperty, Encoding.UTF8.GetString(plain.FindType(BundleFileType.RuntimeConfigJson)!.Content()));
    }

    #endregion

    #region Windows and Mac launchers

    [Fact]
    public void Windows_programs_get_the_icon_name_version_and_subsystem()
    {
        var icon = new List<IconImage> { Icons.FromPng(Png(256)), Icons.FromPng(Png(32)) };
        byte[] exe = WindowsExecutable.Customize(PeHost(), "Life Lab", "1.2.3-beta", icon, gui: true);
        var pe = new WindowsExecutable.PeFile(exe);
        List<WindowsExecutable.Resource> resources = pe.ReadResources();
        Assert.Equal(2, resources.Count(r => r.Type.Id == 3 && r.Type.Name == null));
        Assert.Single(resources, r => r.Type.Id == 14);
        Assert.Single(resources, r => r.Type.Name == "CONFIG" && r.Name.Name == "MAIN");
        Assert.Single(resources, r => r.Type.Id == 24 && r.Language.Name == "LANG");
        byte[] version = resources.Single(r => r.Type.Id == 16).Data;
        string text = Encoding.Unicode.GetString(version);
        Assert.Contains("FileDescription", text);
        Assert.Contains("Life Lab", text);
        Assert.Contains("1.2.3-beta", text);
        int pe32 = BinaryPrimitives.ReadInt32LittleEndian(exe.AsSpan(0x3C));
        Assert.Equal(2, BinaryPrimitives.ReadUInt16LittleEndian(exe.AsSpan(pe32 + 24 + 68)));
        Assert.Equal(3, BinaryPrimitives.ReadUInt16LittleEndian(exe.AsSpan(pe32 + 6)));
        Assert.Equal(0ul, BinaryPrimitives.ReadUInt64LittleEndian(exe.AsSpan(pe32 + 24 + 112 + 32)));
        Assert.True(SingleFileBundle.MarkerOffset(exe) > 0);

        // Terminal programs keep the console; a launcher without resources gets them.
        byte[] console = WindowsExecutable.Customize(PeHost(resources: false), "Tool", null, null, gui: false);
        Assert.Equal(3, BinaryPrimitives.ReadUInt16LittleEndian(console.AsSpan(pe32 + 24 + 68)));
        Assert.Single(new WindowsExecutable.PeFile(console).ReadResources());
        Assert.Contains("1.0.0", Encoding.Unicode.GetString(new WindowsExecutable.PeFile(console).ReadResources()[0].Data));
    }

    [Fact]
    public void Broken_windows_launchers_are_reported()
    {
        Assert.Throws<CompileException>(() => new WindowsExecutable.PeFile(new byte[10]));
        byte[] notPe = PeHost();
        notPe[0x40] = 0;
        Assert.Throws<CompileException>(() => new WindowsExecutable.PeFile(notPe));
        byte[] pe32 = PeHost();
        BinaryPrimitives.WriteUInt16LittleEndian(pe32.AsSpan(0x58), 0x10B);
        Assert.Contains("64-bit", Assert.Throws<CompileException>(() => new WindowsExecutable.PeFile(pe32)).Message);
        Assert.Contains("no room", Assert.Throws<CompileException>(() => WindowsExecutable.Customize(PeHost(headersSize: 0x1A0), "x", "1", null, false)).Message);
        Assert.Contains("outside", Assert.Throws<CompileException>(() => WindowsExecutable.Customize(PeHost(sections: 1), "x", "1", null, false)).Message);
        byte[] noRoomBeforeFirstSection = PeHost();
        BinaryPrimitives.WriteUInt32LittleEndian(noRoomBeforeFirstSection.AsSpan(0x58 + 0xF0 + 20), 0);
        Assert.NotNull(WindowsExecutable.Customize(noRoomBeforeFirstSection, "x", "1", null, false));
    }

    [Fact]
    public void Mac_programs_are_signed_ad_hoc()
    {
        byte[] program = MacExecutable.Sign(MachOHost(), "app.demo");
        AssertSigned(program, "app.demo");
        byte[] added = MacExecutable.Sign(MachOHost(signature: false), "x");
        AssertSigned(added, "x");
        Assert.Contains("no room", Assert.Throws<CompileException>(() => MacExecutable.Sign(MachOHost(signature: false, room: false), "x")).Message);
        byte[] busy = MachOHost(signature: false);
        busy[32 + BinaryPrimitives.ReadInt32LittleEndian(busy.AsSpan(20)) + 3] = 1;
        Assert.Throws<CompileException>(() => MacExecutable.Sign(busy, "x"));
        Assert.Contains("isn't a 64-bit Mac", Assert.Throws<CompileException>(() => MacExecutable.Sign(ElfHost(), "x")).Message);
        byte[] noLinkedit = MachOHost();
        Encoding.ASCII.GetBytes("__OTHER___").CopyTo(noLinkedit, 32 + 72 + 72 + 160 + 8);
        Assert.Contains("__LINKEDIT", Assert.Throws<CompileException>(() => MacExecutable.Sign(noLinkedit, "x")).Message);
    }

    private static void AssertSigned(byte[] file, string id)
    {
        int commands = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(16));
        int at = 32, dataoff = -1;
        ulong linkeditOff = 0, linkeditSize = 0;
        for (int i = 0; i < commands; i++)
        {
            uint cmd = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(at));
            if (cmd == 0x1D) dataoff = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(at + 8));
            if (cmd == 0x19 && Encoding.ASCII.GetString(file, at + 8, 10) == "__LINKEDIT")
            {
                linkeditOff = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(at + 40));
                linkeditSize = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(at + 48));
            }
            at += BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(at + 4));
        }
        Assert.Equal((ulong)file.Length, linkeditOff + linkeditSize);
        Assert.Equal(0xFADE0CC0u, BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(dataoff)));
        int cd = dataoff + 20;
        Assert.Equal(0xFADE0C02u, BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(cd)));
        Assert.Equal(id, Encoding.UTF8.GetString(file, cd + 88, id.Length));
        int hashes = cd + (int)BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(cd + 16));
        Assert.Equal(SHA256.HashData(file.AsSpan(0, 4096)), file.AsSpan(hashes, 32).ToArray());
        int pages = (int)BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(cd + 28));
        int last = (pages - 1) * 4096;
        Assert.Equal(SHA256.HashData(file.AsSpan(last, dataoff - last)), file.AsSpan(hashes + (pages - 1) * 32, 32).ToArray());
    }

    #endregion

    #region Icons

    internal static byte[] Png(int size)
    {
        var png = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(png, 0);
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(16), size);
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(20), size);
        return png;
    }

    private static byte[] Ico(params byte[][] images)
    {
        var s = new MemoryStream();
        var w = new BinaryWriter(s);
        w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)(images.Length + 1));
        int offset = 6 + 16 * (images.Length + 1);
        foreach (byte[] image in images)
        {
            w.Write((byte)16); w.Write((byte)16); w.Write((byte)0); w.Write((byte)0); w.Write((ushort)1); w.Write((ushort)24);
            w.Write(image.Length); w.Write(offset);
            offset += image.Length;
        }
        w.Write(new byte[8]); w.Write(0); w.Write(99999); // an entry pointing outside the file
        foreach (byte[] image in images) w.Write(image);
        return s.ToArray();
    }

    [Fact]
    public void Icons_are_read_from_png_ico_and_icns_and_written_for_each_platform()
    {
        string png = WriteBytes("icon.png", Png(1024));
        Assert.Equal(1024, Icons.Load(png).Single().Width);

        string ico = WriteBytes("icon.ico", Ico(Png(256), new byte[] { 40, 0, 0, 0 }));
        List<IconImage> fromIco = Icons.Load(ico);
        Assert.Equal(2, fromIco.Count);
        Assert.False(fromIco[1].Png);
        Assert.Equal(24, fromIco[1].BitCount);
        Assert.Equal(256, Icons.Largest(fromIco).Width);
        string truncated = WriteBytes("short.ico", Ico().Take(10).ToArray());
        Assert.Throws<CompileException>(() => Icons.Load(truncated));

        byte[] icns = Icons.ToIcns(new[] { 1024, 512, 256, 128, 64, 32, 16, 16 }.Select(n => Icons.FromPng(Png(n))).Append(new IconImage(48, 48, new byte[4], false)));
        string icnsFile = WriteBytes("icon.icns", icns.Concat(new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 }).ToArray());
        Assert.Equal(7, Icons.Load(icnsFile).Count);
        Assert.Equal(Png(1024), Icons.ToPng(Icons.Load(icnsFile)));
        string noPng = WriteBytes("plain.icns", Encoding.ASCII.GetBytes("icns").Concat(new byte[] { 0, 0, 0, 16, (byte)'i', (byte)'s', (byte)'3', (byte)'2', 0, 0, 0, 8 }).ToArray());
        Assert.Throws<CompileException>(() => Icons.Load(noPng));
        Assert.Contains("needs a PNG", Assert.Throws<CompileException>(() => Icons.ToIcns(new[] { new IconImage(16, 16, new byte[1], false) })).Message);
        Assert.Null(Icons.ToPng(new[] { new IconImage(16, 16, new byte[1], false) }));

        string gif = Write("icon.gif", "GIF89a.....");
        Assert.Contains("isn't a PNG", Assert.Throws<CompileException>(() => Icons.Load(gif)).Message);

        byte[] entry = Icons.DirectoryEntry(Icons.FromPng(Png(300)));
        Assert.Equal(0, entry[0]);
        Assert.Equal(32, Icons.DirectoryEntry(Icons.FromPng(Png(32)))[0]);
    }

    #endregion

    #region The command

    private sealed class FakeReleases : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; }
        public List<string> Urls { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Urls.Add(request.RequestUri!.ToString());
            return Task.FromResult(Respond(request));
        }
    }

    private static byte[] TarGz(string name, byte[] data)
    {
        var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        using (var tar = new TarWriter(gzip))
        {
            var entry = new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = new MemoryStream(data) };
            tar.WriteEntry(entry);
        }
        return output.ToArray();
    }

    private static byte[] Zip(string name, byte[] data)
    {
        var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        using (Stream s = zip.CreateEntry(name).Open()) s.Write(data);
        return output.ToArray();
    }

    private CompileEnvironment Env(FakeReleases releases = null, string current = null) => new()
    {
        Version = "0.4.0",
        Repository = "example/kscript",
        HostRuntime = "linux-x64",
        CurrentBinary = current,
        RuntimeCache = Path.Combine(dir, "cache"),
        Handler = releases ?? new FakeReleases { Respond = _ => new HttpResponseMessage(HttpStatusCode.NotFound) },
        Codesign = null,
        DevelopmentBuild = false,
    };

    private (int Code, string Out, string Err) Compile(CompileEnvironment env, params string[] args)
    {
        var o = new StringWriter();
        var e = new StringWriter();
        int code = CompileCommand.Run(args, o, e, env);
        return (code, o.ToString(), e.ToString());
    }

    [Fact]
    public void Terminal_programs_are_built_for_this_computer_from_the_running_kscript()
    {
        string runtime = RuntimeFile("kscript", ElfHost());
        string script = Write("tool/hello.ks.xml", "<kscript>\n  <import package=\"KScript.Math\" />\n  <import path=\"lib/util.ks.xml\" />\n  <echo>@sqrt(4)</echo>\n</kscript>\n");
        Write("tool/lib/util.ks.xml", "<kscript />");
        Write("tool/hello.test.ks.xml", "<kscript />");
        Write("tool/.env", "SECRET=1");
        Write("tool/.env.local", "SECRET=2");
        Write("tool/node_modules/x.js", "");
        Write("tool/Old.app/x", "");

        var (code, output, error) = Compile(Env(current: runtime), script);
        Assert.True(code == 0, error);
        Assert.Contains("hello (terminal program) with KScript.Core, KScript.Math", output);
        Assert.Contains("Left out .env, .env.local", output);
        string exe = Path.Combine(dir, "tool", "dist", "linux-x64", "hello");
        Assert.Contains(exe, output);
        SingleFileBundle program = SingleFileBundle.Read(exe);
        List<string> paths = Paths(program);
        Assert.Contains("kscript-app/app/hello.ks.xml", paths);
        Assert.Contains("kscript-app/app/lib/util.ks.xml", paths);
        Assert.DoesNotContain("kscript-app/app/hello.test.ks.xml", paths);
        Assert.DoesNotContain("kscript-app/app/.env", paths);
        Assert.DoesNotContain(paths, p => p.Contains("node_modules") || p.Contains("Old.app"));
        Assert.DoesNotContain("KScript.UI.dll", paths);
        if (!OperatingSystem.IsWindows()) Assert.True(File.GetUnixFileMode(exe).HasFlag(UnixFileMode.UserExecute));

        // Building again replaces the program, and an --out folder inside the script's folder isn't carried along.
        Assert.Equal(0, Compile(Env(current: runtime), script, "--name", "My Tool", "--out", Path.Combine(dir, "tool", "build")).Code);
        Assert.Equal(0, Compile(Env(current: runtime), script, "--name", "My Tool", "--out", Path.Combine(dir, "tool", "build")).Code);
        string again = Path.Combine(dir, "tool", "build", "linux-x64", "My-Tool");
        Assert.DoesNotContain(Paths(SingleFileBundle.Read(again)), p => p.Contains("build/"));
    }

    [Fact]
    public void Desktop_apps_become_mac_apps_windows_exes_and_linux_programs()
    {
        WriteBytes("app/icon.png", Png(512));
        Write("app/kscript.json", """{ "name": "demo", "app": { "main": "desktop.ks.xml", "name": "Demo App", "id": "com.example.demo", "version": "2.1.0", "icon": "icon.png" } }""");
        Write("app/desktop.ks.xml", "<KScript.Window><Text>Hi</Text></KScript.Window>");
        string mac = RuntimeFile("rt/mac", MachOHost());
        string win = RuntimeFile("rt/win.exe", PeHost());
        string linux = RuntimeFile("rt/linux", ElfHost());
        string sealedApp = null;
        CompileEnvironment env = Env();
        env.Codesign = app => { sealedApp = app; return "no identity"; };

        var (code, output, error) = Compile(env, Path.Combine(dir, "app"), "--target", "mac-arm64", "--runtime", mac);
        Assert.True(code == 0, error);
        Assert.Contains("Demo App (desktop app) with KScript.Core, KScript.UI", output);
        string bundle = Path.Combine(dir, "app", "dist", "mac-arm64", "Demo App.app");
        Assert.Equal(bundle, sealedApp);
        Assert.Contains("codesign couldn't seal Demo App.app", output);
        string plist = File.ReadAllText(Path.Combine(bundle, "Contents", "Info.plist"));
        Assert.Contains("<key>CFBundleIdentifier</key><string>com.example.demo</string>", plist);
        Assert.Contains("<key>CFBundleExecutable</key><string>Demo-App</string>", plist);
        Assert.Contains("<key>CFBundleIconFile</key><string>AppIcon.icns</string>", plist);
        Assert.True(File.Exists(Path.Combine(bundle, "Contents", "Resources", "AppIcon.icns")));
        byte[] macProgram = File.ReadAllBytes(Path.Combine(bundle, "Contents", "MacOS", "Demo-App"));
        AssertSigned(macProgram, "com.example.demo");

        // Again: the earlier app is replaced; a folder that isn't an app is left alone.
        env.Codesign = _ => null;
        Assert.Equal(0, Compile(env, Path.Combine(dir, "app"), "--target", "mac", "--runtime", mac).Code);
        Directory.CreateDirectory(Path.Combine(dir, "other", "mac-arm64", "Demo App.app"));
        (code, _, error) = Compile(env, Path.Combine(dir, "app"), "--target", "mac", "--runtime", mac, "--out", Path.Combine(dir, "other"));
        Assert.Equal(1, code);
        Assert.Contains("isn't a Mac app", error);

        (code, _, error) = Compile(env, Path.Combine(dir, "app"), "--target", "windows", "--runtime", win);
        Assert.True(code == 0, error);
        byte[] exe = File.ReadAllBytes(Path.Combine(dir, "app", "dist", "win-x64", "Demo App.exe"));
        var resources = new WindowsExecutable.PeFile(exe).ReadResources();
        Assert.Contains(resources, r => r.Type.Id == 14);
        Assert.NotNull(SingleFileBundle.Read(exe, "exe").Find("kscript-app/app/desktop.ks.xml"));

        (code, _, error) = Compile(env, Path.Combine(dir, "app"), "-t", "linux-x64", "--runtime", linux);
        Assert.True(code == 0, error);
        string folder = Path.Combine(dir, "app", "dist", "linux-x64");
        Assert.Contains("Exec=Demo-App\nIcon=Demo-App", File.ReadAllText(Path.Combine(folder, "Demo-App.desktop")));
        Assert.Equal(Png(512), File.ReadAllBytes(Path.Combine(folder, "Demo-App.png")));

        // Without an icon, Linux gets no picture and Macs no icon file.
        Write("app/kscript.json", """{ "app": { "main": "desktop.ks.xml" } }""");
        Assert.Equal(0, Compile(env, Path.Combine(dir, "app"), "-t", "linux-x64", "--runtime", linux).Code);
        Assert.DoesNotContain("Icon=", File.ReadAllText(Path.Combine(folder, "Desktop.desktop")));
        Assert.Equal(0, Compile(env, Path.Combine(dir, "app"), "-t", "mac-x64", "--runtime", mac).Code);
        Assert.DoesNotContain("CFBundleIconFile", File.ReadAllText(Path.Combine(dir, "app", "dist", "mac-x64", "Desktop.app", "Contents", "Info.plist")));
    }

    [Fact]
    public void Sites_compile_to_programs_that_serve_them()
    {
        string runtime = RuntimeFile("kscript", ElfHost());
        Write("site/index.html", "<import package=\"KScript.Math\" />\n<h1>Hi</h1>");
        Write("site/about.md", "<import package=\"Nope\" />");
        Write("site/data.json", "{ \"x\": \"<import package=\\\"KScript.Terminal\\\" />\" }");
        var (code, output, error) = Compile(Env(current: runtime), Path.Combine(dir, "site"));
        Assert.True(code == 0, error);
        Assert.Contains("site (web site) with KScript.Core, KScript.Http, KScript.IO, KScript.Markdown, KScript.Math, KScript.Nope, KScript.Style, KScript.Web", output);
        SingleFileBundle program = SingleFileBundle.Read(Path.Combine(dir, "site", "dist", "linux-x64", "site"));
        Assert.Contains("\"main\": \"app/.\"", Encoding.UTF8.GetString(program.Find("kscript-app/kscript-app.json")!.Content()));

        // In a project, the site root and name come from kscript.json.
        Write("project/kscript.json", """{ "name": "team", "serve": { "root": "public" }, "packages": ["Math"] }""");
        Write("project/public/index.html", "<h1>Team</h1>");
        (code, output, error) = Compile(Env(current: runtime), Path.Combine(dir, "project"));
        Assert.True(code == 0, error);
        Assert.Contains("team (web site)", output);
        Assert.True(File.Exists(Path.Combine(dir, "project", "dist", "linux-x64", "team")));

        // A site root outside the project folder still works: only the site goes in.
        Write("outside/kscript.json", """{ "name": "far", "serve": { "root": "../far-site" } }""");
        Write("far-site/routes.ks.xml", "<KScript.Routes />");
        (code, output, error) = Compile(Env(current: runtime), Path.Combine(dir, "outside"));
        Assert.True(code == 0, error);
        Assert.True(File.Exists(Path.Combine(dir, "far-site", "dist", "linux-x64", "far")));
    }

    [Fact]
    public void Plans_say_what_is_wrong()
    {
        string runtime = RuntimeFile("kscript", ElfHost());
        Write("empty/notes.txt", "");
        Directory.SetCurrentDirectory(Path.Combine(dir, "empty"));
        Assert.Contains("Which script?", Compile(Env(current: runtime)).Err);
        Assert.Contains("no script or folder", Compile(Env(current: runtime), "missing.ks.xml").Err);

        Write("p1/kscript.json", """{ "app": { "main": "gone.ks.xml" } }""");
        Assert.Contains("app.main: there is no script", Compile(Env(current: runtime), Path.Combine(dir, "p1")).Err);

        Write("p2/kscript.json", """{ "app": { "main": "a.ks.xml", "icon": "gone.png" }, "nonsense": 1 }""");
        Write("p2/a.ks.xml", "<kscript />");
        var p2 = Compile(Env(current: runtime), Path.Combine(dir, "p2"));
        Assert.Contains("app.icon: there is no file", p2.Err);

        Write("p3/kscript.json", "{ nope");
        Assert.Equal(2, Compile(Env(current: runtime), Path.Combine(dir, "p3")).Code);

        string broken = Write("p4/broken.ks.xml", "<kscript><echo>");
        Assert.Contains("The script has an error", Compile(Env(current: runtime), broken).Err);

        Write("shared/lib.ks.xml", "<kscript />");
        string importer = Write("p5/main.ks.xml", "<kscript>\n  <import path=\"../shared/lib.ks.xml\" />\n</kscript>\n");
        Assert.Contains("which is outside", Compile(Env(current: runtime), importer).Err);

        // A script outside its project's folder takes its own folder.
        Write("p6/kscript.json", """{ "name": "p6", "app": { "main": "../p6-script/run.ks.xml" } }""");
        Write("p6-script/run.ks.xml", "<kscript />");
        Assert.Equal(0, Compile(Env(current: runtime), Path.Combine(dir, "p6")).Code);
        Assert.True(File.Exists(Path.Combine(dir, "p6-script", "dist", "linux-x64", "run")));

        Assert.Contains("can't be used as a program name", Compile(Env(current: runtime), Path.Combine(dir, "p6"), "--name", "///").Err);

        // A wrong command line is exit code 2; a build that could not finish is 1.
        Assert.Equal(ExitCodes.Usage, Compile(Env(current: runtime), "missing.kscript").Code);
        Assert.Equal(ExitCodes.Usage, Compile(Env(current: runtime), "--target", "amiga", importer).Code);
        Assert.Equal(ExitCodes.Usage, Compile(Env(current: runtime), Path.Combine(dir, "p6"), "--name", "///").Code);
        Assert.Equal(ExitCodes.Usage, Compile(Env(current: runtime), Path.Combine(dir, "p6"), "--to", "js").Code);
        Assert.Equal(ExitCodes.Failed, Compile(Env(current: runtime), importer).Code);
        string which = Compile(Env(current: runtime), Path.Combine(dir, "empty")).Err;
        Assert.Contains("\"main\": \"app.kscript\"", which);

        // A project whose routes are in routes/ (as kscript make project --template api writes them) is a site.
        Write("api/kscript.json", """{ "name": "api" }""");
        Write("api/routes/api.kscript", "import KScript.Http\n\nRoutes {\n  Get(path: /health) {\n    return { ok: true }\n  }\n}\n");
        Assert.True(CompileCommand.LooksLikeSite(Path.Combine(dir, "api"), KScript.Projects.ProjectConfig.FindAndLoad(Path.Combine(dir, "api"))));
        Assert.False(CompileCommand.LooksLikeSite(Path.Combine(dir, "empty"), null));
        var api = Compile(Env(current: runtime), Path.Combine(dir, "api"));
        Assert.True(api.Code == 0, api.Err);
        Assert.Contains("web site", api.Out);
        Assert.Equal("Name", CompileCommand.SafeName(" Na:me "));
    }

    [Fact]
    public void Options_and_targets_are_checked()
    {
        Assert.Contains("Usage: kscript compile", Compile(Env(), "--help").Out);
        Assert.Contains("Unknown option '--nope'", Compile(Env(), "--nope").Err);
        Assert.Contains("one script or project at a time", Compile(Env(), "a", "b").Err);
        Assert.Contains("--out needs a value", Compile(Env(), "--out").Err);
        Assert.Contains("--to ts isn't available yet", Compile(Env(), "--to", "ts").Err);
        Assert.Contains("exactly one --target", Compile(Env(), "--target", "all", "--runtime", "x").Err);
        Assert.Contains("There's no target 'amiga'", Compile(Env(), "--target", "amiga").Err);

        Assert.Equal(new[] { "linux-x64" }, CompileCommand.ParseTargets(null, "linux-x64").Select(t => t.Name));
        Assert.Equal(5, CompileCommand.ParseTargets("all,linux", "linux-x64").Count);
        Assert.Equal(new[] { "mac-arm64", "mac-x64", "win-x64" }, CompileCommand.ParseTargets("osx-arm64 macos-x64,windows-x64", null).Select(t => t.Name));
        Assert.Contains("no kscript release for this computer", Assert.Throws<CompileException>(() => CompileCommand.ParseTargets("", "freebsd-x64")).Message);
        Assert.Equal(new[] { "linux-x64", "linux-arm64" }, CompileCommand.ParseTargets("linux-x64", null).Concat(CompileCommand.ParseTargets("linux-arm64", null)).Select(t => t.Name));
    }

    [Fact]
    public void A_native_kscript_downloads_the_runtime_for_its_own_platform_too()
    {
        // Native AOT has no .NET bundle inside, so the running kscript isn't used to build programs.
        Assert.Null(CompileEnvironment.CurrentBundle(managed: false));
        Assert.Equal(KScript.Updates.SelfUpdater.InstalledBinaryPath(KScript.Updates.SelfUpdater.AssemblyLocation(), Environment.ProcessPath), CompileEnvironment.CurrentBundle(managed: true));
        Assert.Equal("kscript-runtime-linux-x64.tar.gz", KScript.Updates.SelfUpdater.RuntimeAssetName("linux-x64"));
        Assert.Equal("kscript-runtime-win-x64.zip", KScript.Updates.SelfUpdater.RuntimeAssetName("win-x64"));
    }

    [Fact]
    public void A_downloaded_runtime_is_checked_against_the_releases_checksums()
    {
        string script = Write("tool/hello.ks.xml", "<kscript />");
        byte[] archive = TarGz("kscript", Runtime(ElfHost()).ToBytes());
        string sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(archive)).ToLowerInvariant();
        string sums = $"{sha}  kscript-runtime-linux-arm64.tar.gz\n";
        HttpStatusCode sumsStatus = HttpStatusCode.OK;
        var releases = new FakeReleases
        {
            Respond = r => r.RequestUri!.AbsolutePath.EndsWith("/SHA256SUMS")
                ? new HttpResponseMessage(sumsStatus) { Content = new StringContent(sums) }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) },
        };
        CompileEnvironment Release()
        {
            CompileEnvironment env = Env(releases);
            env.Version = "0.26.0";
            env.Repository = KScript.Updates.SelfUpdater.DefaultRepository;
            return env;
        }
        string cached = Path.Combine(dir, "cache", "0.26.0", "linux-arm64", "kscript");

        // The wrong file: refused, and nothing is kept.
        sums = new string('0', 64) + "  kscript-runtime-linux-arm64.tar.gz\n";
        var wrong = Compile(Release(), script, "--target", "linux-arm64");
        Assert.NotEqual(0, wrong.Code);
        Assert.Contains($"The checksum of kscript-runtime-linux-arm64.tar.gz is wrong: SHA256SUMS lists {new string('0', 64)} but the download is {sha}.", wrong.Err);
        Assert.False(File.Exists(cached));

        // A list without the file, a release without a list, and a list that can't be fetched are refused too.
        sums = $"{sha}  kscript-runtime-win-x64.zip\n";
        Assert.Contains("SHA256SUMS doesn't list kscript-runtime-linux-arm64.tar.gz", Compile(Release(), script, "--target", "linux-arm64").Err);
        sumsStatus = HttpStatusCode.NotFound;
        Assert.Contains("Release v0.26.0 has no SHA256SUMS file, so the download can't be checked. Nothing was installed.", Compile(Release(), script, "--target", "linux-arm64").Err);
        sumsStatus = HttpStatusCode.BadGateway;
        Assert.Contains("Couldn't download the checksums of kscript 0.26.0 (502)", Compile(Release(), script, "--target", "linux-arm64").Err);
        Assert.False(File.Exists(cached));

        // The listed file is used. sha256sum marks a file read as binary with a star.
        sumsStatus = HttpStatusCode.OK;
        sums = $"{new string('1', 64)}  other.zip\n{sha.ToUpperInvariant()} *kscript-runtime-linux-arm64.tar.gz\n";
        var (code, output, error) = Compile(Release(), script, "--target", "linux-arm64");
        Assert.True(code == 0, error);
        Assert.True(File.Exists(cached));
        Assert.DoesNotContain("KSCRIPT_REPO", output);
        Assert.DoesNotContain("wasn't checked", output);
    }

    [Fact]
    public void Runtimes_for_other_computers_are_downloaded_once_and_cached()
    {
        string script = Write("tool/hello.ks.xml", "<kscript />");
        byte[] linux = Runtime(ElfHost()).ToBytes();
        byte[] win = Runtime(PeHost()).ToBytes();
        var releases = new FakeReleases
        {
            Respond = r => r.RequestUri!.AbsolutePath.EndsWith("/SHA256SUMS") ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : r.RequestUri!.AbsolutePath.EndsWith(".zip")
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Zip("kscript.exe", win)) }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(TarGz("kscript", linux)) },
        };
        var (code, output, error) = Compile(Env(releases), script, "--target", "linux-arm64,win-x64");
        Assert.True(code == 0, error);
        Assert.Contains("Downloading kscript 0.4.0 for linux-arm64", output);
        Assert.Equal(new[]
        {
            "https://github.com/example/kscript/releases/download/v0.4.0/kscript-runtime-linux-arm64.tar.gz",
            "https://github.com/example/kscript/releases/download/v0.4.0/SHA256SUMS",
            "https://github.com/example/kscript/releases/download/v0.4.0/kscript-runtime-win-x64.zip",
            "https://github.com/example/kscript/releases/download/v0.4.0/SHA256SUMS",
        }, releases.Urls);
        // 0.4.0 was published before releases listed checksums, and a release from another repository is named.
        Assert.Contains("Release v0.4.0 was published before releases listed checksums, so the download wasn't checked.", output);
        Assert.Contains("Using releases from example/kscript (KSCRIPT_REPO).", output);
        Assert.True(File.Exists(Path.Combine(dir, "cache", "0.4.0", "win-x64", "kscript.exe")));
        Assert.True(File.Exists(Path.Combine(dir, "tool", "dist", "win-x64", "hello.exe")));

        // The second time comes from the cache.
        Assert.Equal(0, Compile(Env(releases), script, "--target", "linux-arm64").Code);
        Assert.Equal(4, releases.Urls.Count);

        // A missing release, a broken archive, and no network.
        Assert.Contains("Couldn't download", Compile(Env(), script, "--target", "mac-x64").Err);
        releases.Respond = r => r.RequestUri!.AbsolutePath.EndsWith("/SHA256SUMS") ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(TarGz("readme.txt", new byte[1])) };
        Assert.Contains("has no kscript binary inside", Compile(Env(releases), script, "--target", "mac-x64").Err);
        releases.Respond = _ => throw new HttpRequestException("offline");
        string offline = Compile(Env(releases), script, "--target", "mac-arm64").Err;
        Assert.Contains("Couldn't download the kscript runtime.", offline);
        Assert.Contains(": offline", offline);

        // Development builds have no release to download from.
        CompileEnvironment dev = Env();
        dev.Version = "0.0.0-dev";
        Assert.Contains("development build", Compile(dev, script, "--target", "mac-arm64").Err);
        dev.Version = "nonsense";
        Assert.Contains("development build", Compile(dev, script, "--target", "mac-arm64").Err);
        dev.Version = "0.0.0";
        Assert.Contains("development build", Compile(dev, script, "--target", "mac-arm64").Err);
        dev.Version = "0.4.0-dev.1";
        Assert.Contains("development build", Compile(dev, script, "--target", "mac-arm64").Err);
        // A kscript built from the repository carries a release's version number, but is not that release.
        dev.Version = "0.4.0";
        dev.DevelopmentBuild = true;
        Assert.Contains("development build", Compile(dev, script, "--target", "mac-arm64").Err);
    }

    [Fact]
    public void Runtimes_can_be_given_as_a_binary_or_a_release_archive()
    {
        string script = Write("tool/hello.ks.xml", "<kscript />");
        string archive = WriteBytes("rt/kscript-linux-x64.tar.gz", TarGz("kscript", Runtime(ElfHost()).ToBytes()));
        Assert.Equal(0, Compile(Env(), script, "--target", "linux-x64", "--runtime", archive).Code);
        string zip = WriteBytes("rt/kscript-win-x64.zip", Zip("kscript.exe", Runtime(PeHost()).ToBytes()));
        Assert.Equal(0, Compile(Env(), script, "--target", "win-x64", "--runtime", zip).Code);
        Assert.Contains("--runtime: there is no file", Compile(Env(), script, "--target", "win-x64", "--runtime", Path.Combine(dir, "nope")).Err);
        string empty = WriteBytes("rt/empty.zip", Zip("readme.txt", new byte[1]));
        Assert.Contains("empty.zip: ", Compile(Env(), script, "--target", "win-x64", "--runtime", empty).Err);
        string old = RuntimeFile("rt/old", ElfHost(), supportsPrograms: false);
        Assert.Contains("0.4.0 or later", Compile(Env(), script, "--target", "linux-x64", "--runtime", old).Err);
    }

    [Fact]
    public void Codesign_reports_what_went_wrong()
    {
        Assert.NotNull(CompileEnvironment.RunCodesign(dir, "kscript-no-such-codesign"));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Null(CompileEnvironment.RunCodesign(dir, "true"));
            Assert.NotNull(CompileEnvironment.RunCodesign(dir, "false"));
        }
        Assert.NotNull(CompileEnvironment.DefaultCache());
        if (!OperatingSystem.IsMacOS()) Assert.NotNull(CompileEnvironment.RunCodesign(dir));
        var env = new CompileEnvironment();
        Assert.Equal(OperatingSystem.IsMacOS(), env.Codesign != null);
    }

    #endregion
}
