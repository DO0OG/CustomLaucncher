using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using CustomLauncher.Core.Java;

namespace CustomLauncher.Tests;

public sealed class JavaInstallerTests
{
    [Theory]
    [InlineData(Architecture.X64, "x64")]
    [InlineData(Architecture.Arm64, "aarch64")]
    public void GetArchitecture_UsesAdoptiumNames(Architecture architecture, string expected)
    {
        Assert.Equal(expected, JavaInstaller.GetArchitecture(architecture));
    }

    [Theory]
    [InlineData(true, false, false, "windows")]
    [InlineData(false, true, false, "mac")]
    [InlineData(false, false, true, "linux")]
    public void GetPlatform_UsesAdoptiumNames(bool windows, bool mac, bool linux, string expected)
    {
        Assert.Equal(expected, JavaInstaller.GetPlatform(windows, mac, linux));
    }

    [Fact]
    public void Extract_RejectsZipTraversal()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var archivePath = Path.Combine(root, "java.zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            archive.CreateEntry("../escape.txt");
        }

        try
        {
            Assert.Throws<InvalidDataException>(() =>
                new JavaArchiveExtractor().Extract(archivePath, Path.Combine(root, "out")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task InstallAsync_ChecksumMismatchDoesNotExtractOrInstall()
    {
        var runtimeRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var archiveBytes = Encoding.UTF8.GetBytes("not an archive");
        var wrongChecksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("different")));
        var handler = new QueueHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($$"""
                    [{ "binary": { "package": {
                      "link": "https://example.invalid/java.zip",
                      "checksum": "{{wrongChecksum}}",
                      "name": "java.zip"
                    } } }]
                    """),
            },
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archiveBytes) });
        var extractor = new RecordingExtractor();
        var installer = new JavaInstaller(new HttpClient(handler), runtimeRoot, extractor: extractor);

        try
        {
            var result = await installer.InstallAsync(21);

            Assert.False(result.Succeeded);
            Assert.Contains("checksum", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, extractor.Calls);
            Assert.Empty(Directory.EnumerateFileSystemEntries(runtimeRoot));
        }
        finally
        {
            if (Directory.Exists(runtimeRoot)) Directory.Delete(runtimeRoot, recursive: true);
        }
    }

    private sealed class RecordingExtractor : IJavaArchiveExtractor
    {
        public int Calls { get; private set; }
        public void Extract(string archivePath, string destinationDirectory) => Calls++;
    }

    private sealed class QueueHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> responses;
        public QueueHandler(params HttpResponseMessage[] responses) => this.responses = new Queue<HttpResponseMessage>(responses);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responses.Dequeue());
    }
}
