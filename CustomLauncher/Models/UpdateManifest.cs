using System.Collections.Generic;
using Newtonsoft.Json;

namespace CustomLauncher.Models
{
    /// <summary>
    /// 자동 업데이트 매니페스트의 개별 파일 항목 모델
    /// </summary>
    public class FileManifest
    {
        /// <summary>배포 기준 상대 경로</summary>
        [JsonProperty("path")]
        public string Path { get; set; }

        /// <summary>파일 다운로드 URL</summary>
        [JsonProperty("url")]
        public string Url { get; set; }

        /// <summary>원격 파일 SHA256 해시 (버전 파일 방식인 경우 버전 식별자)</summary>
        [JsonProperty("hash")]
        public string Hash { get; set; }

        /// <summary>다운로드 후 7z 압축 해제 여부</summary>
        [JsonProperty("extract", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool Extract { get; set; }

        /// <summary>버전 파일 방식 사용 시 버전 기록 파일의 상대 경로</summary>
        [JsonProperty("version_file_path", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string VersionFilePath { get; set; }
    }

    /// <summary>
    /// 자동 업데이트 매니페스트 루트 모델
    /// </summary>
    public class UpdateManifest
    {
        /// <summary>매니페스트 버전</summary>
        [JsonProperty("version")]
        public string Version { get; set; }

        /// <summary>업데이트 대상 파일 목록</summary>
        [JsonProperty("files")]
        public List<FileManifest> Files { get; set; }
    }
}
