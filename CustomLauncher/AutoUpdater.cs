using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading.Tasks;
using CustomLauncher.Models;
using Newtonsoft.Json;

namespace CustomLauncher
{
    /// <summary>
    /// 원격 매니페스트 기반 파일 자동 업데이트 서비스.
    /// SHA256 해시 비교 또는 버전 파일 방식으로 변경 파일을 감지하고
    /// 7z 아카이브를 다운로드/추출합니다.
    /// </summary>
    public static class AutoUpdater
    {
        /// <summary>업데이트 매니페스트 JSON 다운로드 URL</summary>
        private const string ManifestUrl = LauncherConfig.ManifestUrl;

        /// <summary>
        /// 원격 매니페스트와 로컬 파일을 비교하여 필요한 파일을 업데이트합니다.
        /// </summary>
        /// <param name="baseDirectory">Minecraft 설치 기준 디렉토리</param>
        /// <returns>하나 이상의 파일이 업데이트되었으면 true</returns>
        public static async Task<bool> CheckForUpdatesAsync(string baseDirectory)
        {
            DebugLogger.Log("AutoUpdater.CheckForUpdatesAsync 시작 (버전 파일 방식 포함).");
            bool updatesWerePerformed = false;

            try
            {
                await Ensure7ZipInstalledAsync();

                using (var client = new HttpClient())
                {
                    // 캐시 우회를 위한 헤더 설정
                    client.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true };
                    string manifestJson = await client.GetStringAsync(ManifestUrl);
                    var manifest = JsonConvert.DeserializeObject<UpdateManifest>(manifestJson);

                    if (manifest?.Files == null || manifest.Files.Count == 0)
                    {
                        DebugLogger.Log("매니페스트가 비어 있거나 null입니다. 업데이트를 중단합니다.");
                        return false;
                    }
                    DebugLogger.Log($"매니페스트 v{manifest.Version} 파싱 완료. 파일 수: {manifest.Files.Count}");

                    foreach (var fileInfo in manifest.Files)
                    {
                        string remoteVersionHash = fileInfo.Hash;
                        bool needsUpdate = false;

                        if (fileInfo.Extract && !string.IsNullOrEmpty(fileInfo.VersionFilePath))
                        {
                            // 버전 파일 방식: 버전 식별자를 별도 파일에 기록하여 비교
                            string versionFilePath = Path.Combine(baseDirectory, fileInfo.VersionFilePath.Replace('/', Path.DirectorySeparatorChar));
                            string installedVersion = await GetInstalledVersionAsync(versionFilePath);
                            DebugLogger.Log($"아카이브 확인: '{fileInfo.Path}' | 원격 버전: {remoteVersionHash} | 설치된 버전: {installedVersion}");

                            if (!remoteVersionHash.Equals(installedVersion, StringComparison.OrdinalIgnoreCase))
                            {
                                needsUpdate = true;
                                DebugLogger.Log($"-> '{fileInfo.Path}' 버전 불일치. 업데이트 필요.");
                            }
                        }
                        else
                        {
                            // 일반 파일 방식: 로컬 파일의 SHA256 해시를 원격과 비교
                            string localFilePath = Path.Combine(baseDirectory, fileInfo.Path.Replace('/', Path.DirectorySeparatorChar));
                            string localFileHash = await GetLocalFileHashAsync(localFilePath);
                            DebugLogger.Log($"파일 확인: '{fileInfo.Path}' | 원격 해시: {remoteVersionHash} | 로컬 해시: {localFileHash}");

                            if (!remoteVersionHash.Equals(localFileHash, StringComparison.OrdinalIgnoreCase))
                            {
                                needsUpdate = true;
                                DebugLogger.Log($"-> '{fileInfo.Path}' 해시 불일치. 업데이트 필요.");
                            }
                        }

                        if (needsUpdate)
                        {
                            updatesWerePerformed = true;
                            string localFilePath = Path.Combine(baseDirectory, fileInfo.Path.Replace('/', Path.DirectorySeparatorChar));

                            // 대상 디렉토리가 없으면 생성
                            string directoryPath = Path.GetDirectoryName(localFilePath);
                            if (!Directory.Exists(directoryPath))
                                Directory.CreateDirectory(directoryPath);

                            DebugLogger.Log($"-> {fileInfo.Url} 에서 다운로드 중");
                            await DownloadFileAsync(client, fileInfo.Url, localFilePath);

                            if (fileInfo.Extract)
                            {
                                // mods 폴더 삭제 후 아카이브 추출 (모드 변경 반영)
                                string modsPath = Path.Combine(baseDirectory, "mods");
                                DebugLogger.Log($"-> 'mods' 폴더 삭제 확인: '{modsPath}'");
                                if (Directory.Exists(modsPath))
                                {
                                    try
                                    {
                                        Directory.Delete(modsPath, true);
                                        DebugLogger.Log("-> 'mods' 폴더 삭제 완료.");
                                    }
                                    catch (Exception ex)
                                    {
                                        DebugLogger.Log($"-> 오류: 'mods' 폴더 삭제 실패. {ex.Message}");
                                    }
                                }

                                DebugLogger.Log($"-> '{localFilePath}' 을 '{baseDirectory}' 에 추출 중");
                                await Extract7zAsync(localFilePath, baseDirectory);
                                DebugLogger.Log($"-> 추출 완료 후 아카이브 삭제: '{localFilePath}'");
                                File.Delete(localFilePath);

                                // 버전 파일에 최신 버전 해시 기록
                                if (!string.IsNullOrEmpty(fileInfo.VersionFilePath))
                                {
                                    string versionFilePath = Path.Combine(baseDirectory, fileInfo.VersionFilePath.Replace('/', Path.DirectorySeparatorChar));
                                    DebugLogger.Log($"-> 버전 파일에 '{remoteVersionHash}' 기록: '{versionFilePath}'");
                                    File.WriteAllText(versionFilePath, remoteVersionHash);
                                }
                            }
                        }
                    }
                }

                DebugLogger.Log("업데이트 확인 완료.");
                return updatesWerePerformed;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"AutoUpdater 예상치 못한 오류: {ex.GetType().Name} - {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        /// <summary>로컬 파일의 SHA256 해시를 계산합니다. 파일이 없으면 빈 문자열을 반환합니다.</summary>
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

        /// <summary>버전 파일에서 설치된 버전 식별자를 읽습니다. 파일이 없으면 빈 문자열을 반환합니다.</summary>
        private static async Task<string> GetInstalledVersionAsync(string versionFilePath)
        {
            if (!File.Exists(versionFilePath)) return string.Empty;
            return File.ReadAllText(versionFilePath).Trim();
        }

        /// <summary>HTTP로 파일을 다운로드하여 로컬에 저장합니다.</summary>
        private static async Task DownloadFileAsync(HttpClient client, string url, string filePath)
        {
            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await response.Content.CopyToAsync(fs);
            }
        }

        #region 7-Zip 관련 메서드

        /// <summary>7z 아카이브를 지정 디렉토리에 추출합니다.</summary>
        private static async Task Extract7zAsync(string archivePath, string outputDirectory)
        {
            string sevenZipExePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                LauncherConfig.SevenZipTempFolder, "7z.exe");

            if (!File.Exists(sevenZipExePath))
                throw new FileNotFoundException("7-Zip 실행 파일을 찾을 수 없습니다.", sevenZipExePath);

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
                    if (process == null)
                        throw new InvalidOperationException("7z.exe 프로세스를 시작할 수 없습니다.");

                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    if (process.ExitCode != 0)
                        throw new InvalidOperationException($"7z 추출 실패 (종료 코드: {process.ExitCode})\n오류: {error}");
                }
            });
        }

        /// <summary>
        /// 임베디드 리소스에서 7-Zip 실행 파일을 AppData에 설치합니다.
        /// 이미 존재하면 건너뜁니다.
        /// </summary>
        private static async Task Ensure7ZipInstalledAsync()
        {
            string programFilesPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "7Ziptemp");
            string sevenZipExePath = Path.Combine(programFilesPath, "7z.exe");

            if (File.Exists(sevenZipExePath)) return;

            Directory.CreateDirectory(programFilesPath);

            // 운영체제 아키텍처에 따라 적절한 7-Zip 번들 선택
            byte[] zipFileData = Environment.Is64BitOperatingSystem
                ? Properties.Resources._7_Zip_x64
                : Properties.Resources._7_Zip_x86;

            string tempZipPath = Path.Combine(Path.GetTempPath(), "7z_temp.zip");
            File.WriteAllBytes(tempZipPath, zipFileData);

            try
            {
                System.IO.Compression.ZipFile.ExtractToDirectory(tempZipPath, programFilesPath);
                if (!File.Exists(sevenZipExePath))
                    throw new InvalidOperationException("7-Zip 실행 파일 추출에 실패했습니다.");
            }
            finally
            {
                File.Delete(tempZipPath);
            }
        }

        #endregion
    }
}
