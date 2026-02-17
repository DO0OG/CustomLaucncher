using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace CustomLauncher
{
    #region ManifestDataModels
    public class FileManifest
    {
        [JsonProperty("path")]
        public string Path { get; set; }

        [JsonProperty("url")]
        public string Url { get; set; }

        [JsonProperty("hash")]
        public string Hash { get; set; }

        [JsonProperty("extract", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool Extract { get; set; }

        [JsonProperty("version_file_path", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string VersionFilePath { get; set; }
    }

    public class Manifest
    {
        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("files")]
        public List<FileManifest> Files { get; set; }
    }
    #endregion

    public static class AutoUpdater
    {
        private const string ManifestUrl = "https://dogs.kro.kr/f/d7799eeff50149a4804f/?dl=1";

        public static async Task<bool> CheckForUpdatesAsync(string baseDirectory)
        {
            DebugLogger.Log("AutoUpdater.CheckForUpdatesAsync entered (Version File Logic).");
            bool updatesWerePerformed = false;

            try
            {
                await Ensure7ZipInstalledAsync();

                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true };
                    string manifestJson = await client.GetStringAsync(ManifestUrl);
                    var manifest = JsonConvert.DeserializeObject<Manifest>(manifestJson);

                    if (manifest?.Files == null || manifest.Files.Count == 0)
                    {
                        DebugLogger.Log("Manifest is null or empty. Aborting.");
                        return false;
                    }
                    DebugLogger.Log($"Manifest v{manifest.Version} parsed. Found {manifest.Files.Count} files.");

                    foreach (var fileInfo in manifest.Files)
                    {
                        string remoteVersionHash = fileInfo.Hash;
                        bool needsUpdate = false;

                        // "버전 파일" 로직: extract와 version_file_path가 모두 지정된 경우
                        if (fileInfo.Extract && !string.IsNullOrEmpty(fileInfo.VersionFilePath))
                        {
                            string versionFilePath = Path.Combine(baseDirectory, fileInfo.VersionFilePath.Replace('/', Path.DirectorySeparatorChar));
                            string installedVersion = await GetInstalledVersionAsync(versionFilePath);
                            DebugLogger.Log($"Checking archive: '{fileInfo.Path}' | Remote Version: {remoteVersionHash} | Installed Version: {installedVersion}");

                            if (!remoteVersionHash.Equals(installedVersion, StringComparison.OrdinalIgnoreCase))
                            {
                                needsUpdate = true;
                                DebugLogger.Log($"-> Version mismatch for '{fileInfo.Path}'. Update required.");
                            }
                        }
                        // 일반 파일 로직: 기존 방식대로 파일 자체의 해시를 비교
                        else
                        {
                            string localFilePath = Path.Combine(baseDirectory, fileInfo.Path.Replace('/', Path.DirectorySeparatorChar));
                            string localFileHash = await GetLocalFileHashAsync(localFilePath);
                            DebugLogger.Log($"Checking file: '{fileInfo.Path}' | Remote Hash: {remoteVersionHash} | Local Hash: {localFileHash}");
                            
                            if (!remoteVersionHash.Equals(localFileHash, StringComparison.OrdinalIgnoreCase))
                            {
                                needsUpdate = true;
                                DebugLogger.Log($"-> Hash mismatch for '{fileInfo.Path}'. Update required.");
                            }
                        }

                        if (needsUpdate)
                        {
                            updatesWerePerformed = true;
                            string localFilePath = Path.Combine(baseDirectory, fileInfo.Path.Replace('/', Path.DirectorySeparatorChar));
                            
                            // 파일의 상위 디렉토리가 없으면 생성
                            string directoryPath = Path.GetDirectoryName(localFilePath);
                             if (!Directory.Exists(directoryPath))
                            {
                                Directory.CreateDirectory(directoryPath);
                            }

                            DebugLogger.Log($"-> Downloading from {fileInfo.Url}");
                            await DownloadFileAsync(client, fileInfo.Url, localFilePath);

                            if (fileInfo.Extract)
                            {
                                // mods 폴더 삭제 로직 추가
                                string modsPath = Path.Combine(baseDirectory, "mods");
                                DebugLogger.Log($"-> Checking for existing 'mods' folder at '{modsPath}' to remove it before extraction.");
                                if (Directory.Exists(modsPath))
                                {
                                    try
                                    {
                                        Directory.Delete(modsPath, true);
                                        DebugLogger.Log("-> Successfully deleted 'mods' folder.");
                                    }
                                    catch (Exception ex)
                                    {
                                        DebugLogger.Log($"-> ERROR: Failed to delete 'mods' folder. {ex.Message}");
                                        // 오류를 던지거나 사용자에게 알릴 수 있지만, 여기서는 로그만 남기고 계속 진행합니다.
                                    }
                                }

                                DebugLogger.Log($"-> Extracting '{localFilePath}' to '{baseDirectory}'");
                                await Extract7zAsync(localFilePath, baseDirectory);
                                DebugLogger.Log($"-> Deleting archive '{localFilePath}' after extraction.");
                                File.Delete(localFilePath);

                                // 버전 파일 로직: 업데이트 완료 후, 버전 파일에 최신 해시 기록
                                if (!string.IsNullOrEmpty(fileInfo.VersionFilePath))
                                {
                                    string versionFilePath = Path.Combine(baseDirectory, fileInfo.VersionFilePath.Replace('/', Path.DirectorySeparatorChar));
                                    DebugLogger.Log($"-> Writing new version '{remoteVersionHash}' to '{versionFilePath}'");
                                    File.WriteAllText(versionFilePath, remoteVersionHash);

                                }
                            }
                        }
                    }
                }
                DebugLogger.Log("Update check loop finished.");
                return updatesWerePerformed;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"UNEXPECTED ERROR in AutoUpdater: {ex.GetType().Name} - {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        private static async Task<string> GetLocalFileHashAsync(string filePath)
        {
            if (!File.Exists(filePath)) return string.Empty;
            using (var sha256 = SHA256.Create())
            using (var stream = File.OpenRead(filePath))
            {
                byte[] hashBytes = await Task.Run(() => sha256.ComputeHash(stream));
                return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            }
        }

        private static async Task<string> GetInstalledVersionAsync(string versionFilePath)
        {
            if (!File.Exists(versionFilePath)) return string.Empty;
            return File.ReadAllText(versionFilePath).Trim();
        }

        private static async Task DownloadFileAsync(HttpClient client, string url, string filePath)
        {
            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await response.Content.CopyToAsync(fs);
            }
        }

        #region 7-Zip Logic
        private static async Task Extract7zAsync(string archivePath, string outputDirectory)
        {
            string sevenZipExePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "7Ziptemp", "7z.exe");
            if (!File.Exists(sevenZipExePath)) throw new FileNotFoundException("7-Zip executable not found!", sevenZipExePath);

            var processStartInfo = new ProcessStartInfo
            {
                FileName = sevenZipExePath,
                Arguments = $"x \"{archivePath}\" -o\"{outputDirectory}\" -y",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            await Task.Run(() =>
            {
                using (var process = Process.Start(processStartInfo))
                {
                    if (process == null) throw new InvalidOperationException("7z.exe process could not be started.");
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    if (process.ExitCode != 0)
                    {
                        throw new InvalidOperationException($"7z extraction failed with exit code {process.ExitCode}.\nError: {error}");
                    }
                }
            });
        }

        private static async Task Ensure7ZipInstalledAsync()
        {
            string programFilesPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "7Ziptemp");
            string sevenZipExePath = Path.Combine(programFilesPath, "7z.exe");

            if (File.Exists(sevenZipExePath)) return;

            Directory.CreateDirectory(programFilesPath);

            byte[] zipFileData = Environment.Is64BitOperatingSystem ? 
                Properties.Resources._7_Zip_x64 : 
                Properties.Resources._7_Zip_x86;

            string tempZipPath = Path.Combine(Path.GetTempPath(), "7z_temp.zip");
            File.WriteAllBytes(tempZipPath, zipFileData);

            try
            {
                System.IO.Compression.ZipFile.ExtractToDirectory(tempZipPath, programFilesPath);
                if (!File.Exists(sevenZipExePath))
                {
                    throw new InvalidOperationException("Failed to extract 7-Zip executable.");
                }
            }
            finally
            {
                File.Delete(tempZipPath);
            }
        }
        #endregion
    }
}