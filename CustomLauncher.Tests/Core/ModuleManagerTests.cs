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
