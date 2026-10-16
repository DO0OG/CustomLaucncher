using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using CustomLauncher.Core;
using CustomLauncher.Shared.Models;

namespace CustomLauncher.Tests.Core;

public sealed class ModuleManagerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "launcher-module-tests-" + Guid.NewGuid().ToString("N"));

    public ModuleManagerTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task Update_VerifiesHashAndDoesNotRedownloadUnchangedModule()
    {
        var content = "module data"u8.ToArray();
        var handler = new StaticHandler(content);
        var manager = new ModuleManager(new HttpClient(handler), _directory);
        var distro = Distribution(Module("mod", "mods/mod.jar", content));

        var first = await manager.UpdateAsync(distro);
        var second = await manager.UpdateAsync(distro);

        Assert.Equal(ModuleUpdateStatus.Updated, first.Status);
        Assert.Equal(ModuleUpdateStatus.UpToDate, second.Status);
        Assert.Equal(1, handler.RequestCount);
        Assert.Equal(content, await File.ReadAllBytesAsync(Path.Combine(_directory, "mods", "mod.jar")));
    }

    [Fact]
    public async Task Update_HashMismatchNeverAppliesAndPreservesDropIn()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "mods"));
        await File.WriteAllTextAsync(Path.Combine(_directory, "mods", "user.jar"), "keep");
        var manager = new ModuleManager(new HttpClient(new StaticHandler("wrong"u8.ToArray())), _directory);
        var module = Module("mod", "mods/mod.jar", "right"u8.ToArray());

        var result = await manager.UpdateAsync(Distribution(module));

        Assert.Equal(ModuleUpdateStatus.Failed, result.Status);
        Assert.False(File.Exists(Path.Combine(_directory, "mods", "mod.jar")));
        Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(_directory, "mods", "user.jar")));
    }

    [Fact]
    public async Task Update_RejectsArchiveTraversal()
    {
        byte[] archiveBytes;
        using (var memory = new MemoryStream())
        {
            using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true))
            {
                var entry = archive.CreateEntry("../../evil.txt");
                await using var writer = new StreamWriter(entry.Open());
                await writer.WriteAsync("evil");
            }
            archiveBytes = memory.ToArray();
        }
        var module = Module("bundle", "mods", archiveBytes);
        module.Packaging = ModulePackaging.Archive;
        var manager = new ModuleManager(new HttpClient(new StaticHandler(archiveBytes)), _directory);

        var result = await manager.UpdateAsync(Distribution(module));

        Assert.Equal(ModuleUpdateStatus.Failed, result.Status);
        Assert.False(File.Exists(Path.Combine(_directory, "evil.txt")));
    }

    [Fact]
    public async Task Update_WhenModuleBecomesUnmanaged_PreservesItsFile()
    {
        var content = "server then user owned"u8.ToArray();
        var manager = new ModuleManager(new HttpClient(new StaticHandler(content)), _directory);
        var module = Module("mod", "mods/mod.jar", content);
        Assert.Equal(ModuleUpdateStatus.Updated, (await manager.UpdateAsync(Distribution(module))).Status);

        module.IsServerManaged = false;
        var result = await manager.UpdateAsync(Distribution(module));

        Assert.Equal(ModuleUpdateStatus.Updated, result.Status);
        Assert.Equal(content, await File.ReadAllBytesAsync(Path.Combine(_directory, "mods", "mod.jar")));
    }

    [Fact]
    public async Task Update_VerifyContentsRepairsOnlyTamperedFileModule()
    {
        var alpha = "alpha"u8.ToArray();
        var beta = "beta"u8.ToArray();
        var handler = new MapHandler { ["https://example.test/a"] = alpha, ["https://example.test/b"] = beta };
        var manager = new ModuleManager(new HttpClient(handler), _directory);
        var distro = Distribution(Module("a", "mods/a.jar", alpha), Module("b", "mods/b.jar", beta));
        await manager.UpdateAsync(distro);
        Assert.Equal(2, handler.RequestCount);
        await File.WriteAllTextAsync(Path.Combine(_directory, "mods", "b.jar"), "tampered");

        var result = await manager.UpdateAsync(distro, verifyContents: true);

        Assert.Equal(ModuleUpdateStatus.Updated, result.Status);
        Assert.Equal(["b"], result.UpdatedModuleIds);
        Assert.Equal(2, result.VerifiedCount);
        Assert.Equal(3, handler.RequestCount);
        Assert.Equal(beta, await File.ReadAllBytesAsync(Path.Combine(_directory, "mods", "b.jar")));
        Assert.Equal(alpha, await File.ReadAllBytesAsync(Path.Combine(_directory, "mods", "a.jar")));
    }

    [Fact]
    public async Task Update_ArchiveRecordsHashesAndOnlyVerifyContentsDetectsTampering()
    {
        var archiveBytes = ZipBytes(("one.txt", "first"), ("sub/two.txt", "second"));
        var handler = new MapHandler { ["https://example.test/bundle"] = archiveBytes };
        var manager = new ModuleManager(new HttpClient(handler), _directory);
        var module = ArchiveModule("bundle", "pack", archiveBytes);
        var distro = Distribution(module);
        Assert.Equal(ModuleUpdateStatus.Updated, (await manager.UpdateAsync(distro)).Status);

        var record = (await new ManagedModuleStateStore(_directory).LoadAsync()).Modules["bundle"];
        Assert.Equal(module.Hash, record.ArchiveHash);
        Assert.Equal(["pack/one.txt", "pack/sub/two.txt"], record.FileHashes!.Keys.Order());
        Assert.Equal(Convert.ToHexString(SHA256.HashData("first"u8)).ToLowerInvariant(), record.FileHashes["pack/one.txt"]);

        var tampered = Path.Combine(_directory, "pack", "one.txt");
        await File.WriteAllTextAsync(tampered, "tampered");
        var plain = await manager.UpdateAsync(distro);
        Assert.Equal(ModuleUpdateStatus.UpToDate, plain.Status);
        Assert.Equal(1, handler.RequestCount);

        var repaired = await manager.UpdateAsync(distro, verifyContents: true);
        Assert.Equal(ModuleUpdateStatus.Updated, repaired.Status);
        Assert.Equal(["bundle"], repaired.UpdatedModuleIds);
        Assert.Equal(2, handler.RequestCount);
        Assert.Equal("first", await File.ReadAllTextAsync(tampered));
    }

    [Fact]
    public async Task Update_ArchiveRecordWithoutArchiveHashIsDownloadedAgain()
    {
        var archiveBytes = ZipBytes(("one.txt", "first"));
        var handler = new MapHandler { ["https://example.test/bundle"] = archiveBytes };
        var manager = new ModuleManager(new HttpClient(handler), _directory);
        var distro = Distribution(ArchiveModule("bundle", "pack", archiveBytes));
        await manager.UpdateAsync(distro);
        var store = new ManagedModuleStateStore(_directory);
        var state = await store.LoadAsync();
        state.Modules["bundle"].ArchiveHash = null;
        state.Modules["bundle"].FileHashes = null;
        await store.SaveAsync(state);

        var result = await manager.UpdateAsync(distro);

        Assert.Equal(ModuleUpdateStatus.Updated, result.Status);
        Assert.Equal(2, handler.RequestCount);
        Assert.NotNull((await store.LoadAsync()).Modules["bundle"].ArchiveHash);
    }

    [Fact]
    public async Task Update_VerifyContentsDownloadFailureLeavesFilesAndStateUntouched()
    {
        var alpha = "alpha"u8.ToArray();
        var beta = "beta"u8.ToArray();
        var handler = new MapHandler { ["https://example.test/a"] = alpha, ["https://example.test/b"] = beta };
        var manager = new ModuleManager(new HttpClient(handler), _directory);
        var distro = Distribution(Module("a", "mods/a.jar", alpha), Module("b", "mods/b.jar", beta));
        await manager.UpdateAsync(distro);
        var tampered = Path.Combine(_directory, "mods", "b.jar");
        await File.WriteAllTextAsync(tampered, "tampered");
        var statePath = Path.Combine(_directory, ".launcher-managed-modules.json");
        var stateBefore = await File.ReadAllTextAsync(statePath);
        handler.FailWithStatus = HttpStatusCode.InternalServerError;

        var result = await manager.UpdateAsync(distro, verifyContents: true);

        Assert.Equal(ModuleUpdateStatus.Failed, result.Status);
        Assert.Equal("tampered", await File.ReadAllTextAsync(tampered));
        Assert.Equal(alpha, await File.ReadAllBytesAsync(Path.Combine(_directory, "mods", "a.jar")));
        Assert.Equal(stateBefore, await File.ReadAllTextAsync(statePath));
    }

    private static byte[] ZipBytes(params (string Name, string Content)[] files)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            foreach (var (name, content) in files)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                writer.Write(content);
            }
        }
        return memory.ToArray();
    }

    private static DistroModule ArchiveModule(string id, string path, byte[] content)
    {
        var module = Module(id, path, content);
        module.Packaging = ModulePackaging.Archive;
        return module;
    }

    private static ServerDistribution Distribution(params DistroModule[] modules) => new()
    {
        ServerId = "test",
        Modules = modules.ToList(),
    };

    private static DistroModule Module(string id, string path, byte[] content) => new()
    {
        Id = id,
        Path = path,
        Url = "https://example.test/" + id,
        Hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(),
        IsServerManaged = true,
    };

    public void Dispose() => Directory.Delete(_directory, true);

    private sealed class MapHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, byte[]> _responses = new(StringComparer.Ordinal);

        public int RequestCount { get; private set; }
        public HttpStatusCode? FailWithStatus { get; set; }

        public byte[] this[string url]
        {
            set => _responses[url] = value;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            if (FailWithStatus is { } status)
                return Task.FromResult(new HttpResponseMessage(status));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_responses[request.RequestUri!.ToString()]),
            });
        }
    }

    private sealed class StaticHandler(byte[] content) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) });
        }
    }
}
