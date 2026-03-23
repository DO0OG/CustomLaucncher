using System;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace CustomLauncher.Core
{
    /// <summary>
    /// mcsrvstat.us API를 통해 Minecraft 서버 온라인 상태를 확인하는 서비스 클래스
    /// </summary>
    public class ServerStatusChecker
    {
        /// <summary>서버 상태 API 엔드포인트 (mcsrvstat.us v3)</summary>
        private const string StatusApiUrl = LauncherConfig.ServerStatusApiUrl;

        private readonly HttpClient _httpClient;

        /// <param name="httpClient">HTTP 요청에 사용할 클라이언트 인스턴스</param>
        public ServerStatusChecker(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        /// <summary>
        /// 서버 온라인 여부를 비동기로 확인합니다.
        /// </summary>
        /// <returns>서버가 온라인이면 true, 오프라인이거나 오류 발생 시 false</returns>
        public async Task<bool> CheckAsync()
        {
            try
            {
                string content = await _httpClient.GetStringAsync(StatusApiUrl);
                JObject json = JObject.Parse(content);
                return json.Value<bool>("online");
            }
            catch (Exception)
            {
                // 네트워크 오류 또는 파싱 실패 시 오프라인으로 간주
                return false;
            }
        }
    }
}
