using CustomLauncher.Core;

namespace CustomLauncher.Tests.Core;

public sealed class ServerListWriterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"cl-servers-{Guid.NewGuid():N}");
    private string Path_ => System.IO.Path.Combine(_root, "servers.dat");

    public ServerListWriterTests() => Directory.CreateDirectory(_root);

    private static NbtCompound Read(string path)
    {
        using var stream = File.OpenRead(path);
        return Nbt.Read(stream);
    }

    private static List<NbtCompound> Servers(NbtCompound root) =>
        ((NbtList)root["servers"]).Cast<NbtCompound>().ToList();

    [Fact]
    public void RegisteringIntoAFreshDirectoryCreatesTheList()
    {
        var status = new ServerListWriter().Register(_root, "DOG'S SERVER", "play.example.com");

        Assert.Equal(ServerListStatus.Added, status);
        var servers = Servers(Read(Path_));
        Assert.Single(servers);
        Assert.Equal("DOG'S SERVER", servers[0].GetString("name"));
        Assert.Equal("play.example.com", servers[0].GetString("ip"));
    }

    [Fact]
    public void RegisteringTwiceDoesNotDuplicateTheEntry()
    {
        var writer = new ServerListWriter();
        writer.Register(_root, "DOG'S SERVER", "play.example.com");
        var status = writer.Register(_root, "DOG'S SERVER", "play.example.com");

        Assert.Equal(ServerListStatus.AlreadyPresent, status);
        Assert.Single(Servers(Read(Path_)));
    }

    [Fact]
    public void ServersThePlayerAddedInGameSurviveTheNextLaunch()
    {
        // This is the case that justifies merging rather than overwriting: the file is rewritten by
        // Minecraft itself every time the player edits their list.
        var writer = new ServerListWriter();
        writer.Register(_root, "DOG'S SERVER", "play.example.com");

        var root = Read(Path_);
        ((NbtList)root["servers"]).Add(new NbtCompound
        {
            ["name"] = "친구 서버",
            ["ip"] = "friend.example.net",
            ["acceptTextures"] = (byte)1,
        });
        using (var stream = File.Create(Path_)) Nbt.Write(stream, root);

        writer.Register(_root, "DOG'S SERVER", "play.example.com");

        var servers = Servers(Read(Path_));
        Assert.Equal(2, servers.Count);
        var friend = servers.Single(server => server.GetString("ip") == "friend.example.net");
        Assert.Equal("친구 서버", friend.GetString("name"));
        Assert.Equal((byte)1, friend["acceptTextures"]);
    }

    [Fact]
    public void ARenamedServerIsUpdatedInPlaceWithoutLosingItsIcon()
    {
        var writer = new ServerListWriter();
        writer.Register(_root, "OLD NAME", "play.example.com");
        var root = Read(Path_);
        ((NbtCompound)((NbtList)root["servers"])[0])["icon"] = "iVBORw0KGgo=";
        ((NbtList)root["servers"]).Insert(0, new NbtCompound { ["name"] = "다른 서버", ["ip"] = "other.example.net" });
        using (var stream = File.Create(Path_)) Nbt.Write(stream, root);

        var status = writer.Register(_root, "NEW NAME", "play.example.com");

        Assert.Equal(ServerListStatus.Updated, status);
        var servers = Servers(Read(Path_));
        Assert.Equal("다른 서버", servers[0].GetString("name"));
        Assert.Equal("NEW NAME", servers[1].GetString("name"));
        Assert.Equal("iVBORw0KGgo=", servers[1].GetString("icon"));
    }

    [Fact]
    public void TheDefaultPortIsTreatedAsTheSameAddress()
    {
        var writer = new ServerListWriter();
        writer.Register(_root, "DOG'S SERVER", "play.example.com:25565");
        var status = writer.Register(_root, "DOG'S SERVER", "play.example.com");

        Assert.Equal(ServerListStatus.AlreadyPresent, status);
        Assert.Single(Servers(Read(Path_)));
    }

    [Fact]
    public void AnUnreadableListIsReportedInsteadOfBlockingTheLaunch()
    {
        File.WriteAllBytes(Path_, [0xFF, 0xFF, 0xFF]);

        var status = new ServerListWriter().Register(_root, "DOG'S SERVER", "play.example.com");

        Assert.Equal(ServerListStatus.Failed, status);
    }

    [Fact]
    public void NothingIsWrittenWhileTheGameIsRunning()
    {
        var status = new ServerListWriter(() => true).Register(_root, "DOG'S SERVER", "play.example.com");

        Assert.Equal(ServerListStatus.GameRunning, status);
        Assert.False(File.Exists(Path_));
    }

    [Fact]
    public void TheExistingFileIsBackedUpBeforeTheFirstEdit()
    {
        var writer = new ServerListWriter();
        writer.Register(_root, "DOG'S SERVER", "play.example.com");
        writer.Register(_root, "RENAMED", "play.example.com");

        Assert.True(File.Exists(Path_ + ".bak"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}

public sealed class NbtTests
{
    [Fact]
    public void EveryTagTypeSurvivesARoundTrip()
    {
        var root = new NbtCompound
        {
            ["b"] = (byte)7,
            ["s"] = (short)-300,
            ["i"] = 70000,
            ["l"] = 9_000_000_000L,
            ["f"] = 1.5f,
            ["d"] = -2.25d,
            ["ba"] = new byte[] { 1, 2, 3 },
            ["str"] = "한글 and ascii",
            ["ia"] = new[] { 1, -2, 3 },
            ["la"] = new[] { 1L, -2L },
            ["list"] = new NbtList(NbtTagType.String) { "a", "b" },
            ["nested"] = new NbtCompound { ["x"] = 1 },
        };

        using var stream = new MemoryStream();
        Nbt.Write(stream, root);
        stream.Position = 0;
        var copy = Nbt.Read(stream);

        Assert.Equal((byte)7, copy["b"]);
        Assert.Equal((short)-300, copy["s"]);
        Assert.Equal(70000, copy["i"]);
        Assert.Equal(9_000_000_000L, copy["l"]);
        Assert.Equal(1.5f, copy["f"]);
        Assert.Equal(-2.25d, copy["d"]);
        Assert.Equal(new byte[] { 1, 2, 3 }, (byte[])copy["ba"]);
        Assert.Equal("한글 and ascii", copy.GetString("str"));
        Assert.Equal(new[] { 1, -2, 3 }, (int[])copy["ia"]);
        Assert.Equal(new[] { 1L, -2L }, (long[])copy["la"]);
        Assert.Equal(["a", "b"], ((NbtList)copy["list"]).Cast<string>());
        Assert.Equal(1, ((NbtCompound)copy["nested"])["x"]);
    }

    [Fact]
    public void AnEmptyListKeepsItsElementType()
    {
        var root = new NbtCompound { ["empty"] = new NbtList(NbtTagType.Compound) };
        using var stream = new MemoryStream();
        Nbt.Write(stream, root);
        stream.Position = 0;

        Assert.Empty((NbtList)Nbt.Read(stream)["empty"]);
    }

    [Theory]
    [InlineData("emoji \U0001F600 here")]
    [InlineData("null \0 inside")]
    [InlineData("")]
    public void ModifiedUtf8RoundTripsWhereRealUtf8Would(string value)
    {
        var root = new NbtCompound { ["s"] = value };
        using var stream = new MemoryStream();
        Nbt.Write(stream, root);
        stream.Position = 0;

        Assert.Equal(value, Nbt.Read(stream).GetString("s"));
    }

    [Fact]
    public void SupplementaryCharactersUseTheSixByteJavaForm()
    {
        // Real UTF-8 would emit four bytes; Java writes each surrogate separately.
        Assert.Equal(6, ModifiedUtf8.Encode("\U0001F600").Length);
        // And U+0000 is two bytes rather than one, so it never terminates a string.
        Assert.Equal(new byte[] { 0xC0, 0x80 }, ModifiedUtf8.Encode("\0"));
    }
}
