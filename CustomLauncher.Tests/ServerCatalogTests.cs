using System.Text.Json;
using System.Text.Json.Serialization;
using CustomLauncher.Core;
using CustomLauncher.Shared.Models;

namespace CustomLauncher.Tests;

public sealed class ServerCatalogTests : IDisposable
{
    private static readonly JsonSerializerOptions ManifestJson = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _gameDirectory = Path.Combine(Path.GetTempPath(), "customlauncher-catalog-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void DeserializesServersWithDefaultPortAndOptionalFields()
    {
        const string json = """
            {
              "servers": [
                {
                  "id": "main",
                  "name": "Main",
                  "address": "main.example.com",
                  "minecraftVersion": "1.20.1",
                  "modLoader": { "type": "fabric", "version": "0.15.7" },
                  "discord": { "details": "Playing Main" },
                  "statusUrl": "https://status.example.com/main"
                },
                {
                  "id": "second",
                  "name": "Second",
                  "address": "second.example.com",
                  "port": 25566,
                  "minecraftVersion": "1.21.1",
                  "modLoader": { "type": "neoforge", "version": "21.1.0" }
                }
              ]
            }
            """;

        var manifest = Read(json);

        Assert.Equal(2, manifest.Servers.Count);
        var main = manifest.Servers[0];
        Assert.Equal("main", main.Id);
        Assert.Equal(25565, main.Port);
        Assert.Equal("fabric", main.ModLoader.Type);
        Assert.Equal("Playing Main", main.Discord?.Details);
        Assert.Equal("https://status.example.com/main", main.StatusUrl);
        Assert.Equal(25566, manifest.Servers[1].Port);
    }

    [Fact]
    public void LegacySingleServerManifestMovesModulesIntoOneServer()
    {
        var manifest = Read(LegacyJson(new string('a', 64)));

        var result = LauncherServers.Normalize(manifest);

        var server = Assert.Single(result.Servers);
        Assert.Equal(ServerCatalog.LegacyServerId, server.Id);
        Assert.Equal(LauncherConfig.ServerName, server.Name);
        Assert.Equal(LauncherConfig.McVersion, server.MinecraftVersion);
        Assert.Equal("core", Assert.Single(server.Modules).Id);
        Assert.Equal("17", server.Java.MinVersion);
        Assert.Empty(result.Modules);
        // The input manifest is not changed by normalization.
        Assert.Empty(manifest.Servers);
        Assert.Equal("core", Assert.Single(manifest.Modules).Id);
    }

    [Fact]
    public void NullServersListIsTreatedAsLegacyFormat()
    {
        var manifest = Read("""{ "servers": null, "serverId": "main", "modules": [], "java": { "minVersion": "17" } }""");

        var result = LauncherServers.Normalize(manifest);

        var server = Assert.Single(result.Servers);
        Assert.Equal(ServerCatalog.LegacyServerId, server.Id);
        Assert.Equal("17", server.Java.MinVersion);
    }

    [Fact]
    public void RejectsDuplicateServerIds()
    {
        var distribution = Catalog(ValidServer("main"), ValidServer("main"));

        var exception = Rejected(distribution);

        Assert.Contains("Duplicate server id", exception.Message);
    }

    [Fact]
    public void RejectsEmptyServerId()
    {
        Rejected(Catalog(ValidServer("")));
    }

    [Theory]
    [InlineData("Main")]
    [InlineData("my server")]
    public void RejectsUppercaseOrSpacedServerIds(string id)
    {
        Rejected(Catalog(ValidServer(id)));
    }

    [Fact]
    public void RejectsUnknownLoaderTypeAndNamesIt()
    {
        var server = ValidServer();
        server.ModLoader = new ModLoaderSpec { Type = "xyz", Version = "1.0.0" };

        var exception = Rejected(Catalog(server));

        Assert.Contains("unsupported mod loader type 'xyz'", exception.Message);
    }

    [Fact]
    public void RejectsNonVanillaLoaderWithoutVersion()
    {
        var server = ValidServer();
        server.ModLoader = new ModLoaderSpec { Type = "forge", Version = "" };

        Rejected(Catalog(server));
    }

    [Theory]
    [InlineData("neoforge")]
    [InlineData("quilt")]
    public void AcceptsNeoForgeAndQuiltLoaders(string type)
    {
        var server = ValidServer();
        server.ModLoader = new ModLoaderSpec { Type = type, Version = "1.0.0" };

        Assert.Null(Record.Exception(() => ServerCatalog.Validate(Catalog(server))));
    }

    [Fact]
    public void RejectsPortZero()
    {
        var server = ValidServer();
        server.Port = 0;

        Rejected(Catalog(server));
    }

    [Fact]
    public void RejectsHttpStatusUrl()
    {
        var server = ValidServer();
        server.StatusUrl = "http://status.example.com/main";

        Rejected(Catalog(server));
    }

    [Fact]
    public void RejectsTopLevelModulesCombinedWithServers()
    {
        var distribution = Catalog(ValidServer());
        distribution.Modules = [Module("core")];

        Rejected(distribution);
    }

    [Fact]
    public void RejectsNullServerEntryWithItsIndex()
    {
        var exception = Rejected(Catalog(ValidServer(), null!));

        Assert.Equal("Server entry 1 is null.", exception.Message);
    }

    [Fact]
    public void RejectsServerWithNullModulesAndNamesIt()
    {
        var server = ValidServer();
        server.Modules = null!;

        var exception = Rejected(Catalog(server));

        Assert.Contains("'main'", exception.Message);
    }

    [Fact]
    public void RejectsServerWithNullJavaAndNamesIt()
    {
        var server = ValidServer();
        server.Java = null!;

        var exception = Rejected(Catalog(server));

        Assert.Contains("'main'", exception.Message);
    }

    [Fact]
    public void SelectReturnsRequestedServerOrFirstWhenUnknown()
    {
        var distribution = Catalog(ValidServer("main"), ValidServer("second"));

        Assert.Equal("second", ServerCatalog.Select(distribution, "second").Id);
        Assert.Equal("main", ServerCatalog.Select(distribution, "deleted").Id);
        Assert.Equal("main", ServerCatalog.Select(distribution, null).Id);
    }

    [Fact]
    public void ForServerUsesServerModulesAndJavaWithTopLevelShader()
    {
        var second = ValidServer("second");
        second.Modules = [Module("extra")];
        second.Java = new JavaRequirement { MinVersion = "21" };
        var distribution = new ServerDistribution
        {
            DefaultShaderPackId = "shader-pack",
            Servers = [ValidServer("main"), second],
        };

        var result = ServerCatalog.ForServer(distribution, second);

        Assert.Equal("second", result.ServerId);
        Assert.Equal(["extra"], result.Modules.Select(module => module.Id));
        Assert.Equal("21", result.Java.MinVersion);
        Assert.Equal("shader-pack", result.DefaultShaderPackId);
        Assert.Empty(result.Servers);
    }

    [Fact]
    public void JoinAddressOmitsDefaultPortOnly()
    {
        var server = ValidServer();
        Assert.Equal("play.example.com", ServerCatalog.JoinAddress(server));

        server.Port = 25566;
        Assert.Equal("play.example.com:25566", ServerCatalog.JoinAddress(server));
    }

    [Fact]
    public async Task LegacyCacheLoadsAsSingleServerAndRoundTrips()
    {
        Directory.CreateDirectory(_gameDirectory);
        await File.WriteAllTextAsync(Path.Combine(_gameDirectory, ".launcher-distribution-cache.json"),
            LegacyJson(new string('a', 64)));
        using var httpClient = new HttpClient();
        var service = new ContentUpdateService(httpClient, _gameDirectory);

        var loaded = await service.TryLoadCacheAsync(CancellationToken.None);

        Assert.NotNull(loaded);
        var server = Assert.Single(loaded.Servers);
        Assert.Equal(ServerCatalog.LegacyServerId, server.Id);
        Assert.Single(server.Modules);

        await service.SaveCacheAsync(loaded, CancellationToken.None);
        var reloaded = await service.TryLoadCacheAsync(CancellationToken.None);

        Assert.NotNull(reloaded);
        var again = Assert.Single(reloaded.Servers);
        Assert.Equal(server.Id, again.Id);
        Assert.Single(again.Modules);
    }

    [Fact]
    public async Task CacheWithDuplicateServerIdsLoadsAsNull()
    {
        Directory.CreateDirectory(_gameDirectory);
        await File.WriteAllTextAsync(Path.Combine(_gameDirectory, ".launcher-distribution-cache.json"), """
            {
              "servers": [
                { "id": "main", "name": "Main", "address": "main.example.com", "minecraftVersion": "1.20.1", "modLoader": { "type": "fabric", "version": "0.15.7" } },
                { "id": "main", "name": "Again", "address": "main.example.com", "minecraftVersion": "1.20.1", "modLoader": { "type": "fabric", "version": "0.15.7" } }
              ]
            }
            """);
        using var httpClient = new HttpClient();
        var service = new ContentUpdateService(httpClient, _gameDirectory);

        Assert.Null(await service.TryLoadCacheAsync(CancellationToken.None));
    }

    public void Dispose()
    {
        if (Directory.Exists(_gameDirectory)) Directory.Delete(_gameDirectory, true);
    }

    private static ServerDistribution Read(string json) =>
        JsonSerializer.Deserialize<ServerDistribution>(json, ManifestJson)!;

    private static string LegacyJson(string hash) => $$"""
        {
          "serverId": "main",
          "java": { "minVersion": "17" },
          "modules": [
            { "id": "core", "path": "mods/core.jar", "url": "https://example.com/core.jar", "hash": "{{hash}}", "type": "RequiredMod", "packaging": "File" }
          ]
        }
        """;

    private static ServerEntry ValidServer(string id = "main") => new()
    {
        Id = id,
        Name = "Main",
        Address = "play.example.com",
        MinecraftVersion = "1.20.1",
        ModLoader = new ModLoaderSpec { Type = "fabric", Version = "0.15.7" },
    };

    private static ServerDistribution Catalog(params ServerEntry[] servers) => new() { Servers = [.. servers] };

    private static DistroModule Module(string id) => new()
    {
        Id = id,
        Path = $"mods/{id}.jar",
        Url = $"https://example.com/{id}.jar",
        Hash = new string('a', 64),
        Type = ModuleType.RequiredMod,
        Packaging = ModulePackaging.File,
    };

    private static InvalidDataException Rejected(ServerDistribution distribution) =>
        Assert.Throws<InvalidDataException>(() => ServerCatalog.Validate(distribution));
}
