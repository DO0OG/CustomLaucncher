# 의존성 감사

영문 버전: [DEPENDENCY_AUDIT.md](DEPENDENCY_AUDIT.md)

## 런처 런타임

| 의존성 | 버전 | 용도 / 결정 |
|---|---:|---|
| Avalonia | 11.3.12 | 크로스플랫폼 데스크톱 UI (MIT) |
| CmlLib.Core | 4.0.6 | 마인크래프트 설치·실행 오케스트레이션 (MIT) |
| CmlLib.Core.Auth.Microsoft | 3.3.1 | 마인크래프트/Xbox 인증 파이프라인 (MIT) |
| CmlLib.Core.Installer.Forge | 1.1.1 | Forge 설치 (MIT) |
| CmlLib.Core.Installer.NeoForge | 4.0.1 | NeoForge 설치 (MIT) |
| XboxAuthNet.Game.Msal | 0.1.2 | MSAL 브라우저·디바이스 코드 provider 및 OS 보호 토큰 캐시 (MIT) |
| SharpCompress | 0.50.5 | 7z 압축 해제. 모든 항목은 `ModuleValidation`으로 사전 검증 후 스트리밍됩니다. |
| Velopack | 1.2.0 | 시작 훅만 사용(`VelopackApp.Build().Run()`). 업데이트 확인·적용 UI와 업데이트 피드는 아직 없음 (MIT) |
| DiscordRichPresence | 1.6.1.70 | 선택적 Discord IPC. 컴파일 플래그와 사용자 opt-in 뒤에 있음 (MIT) |

## 매니페스트 도구

`CustomLauncher.Shared`를 공유하고, 창을 위해 Avalonia(11.3.12, MIT)를 참조합니다. Tmds.DBus.Protocol 고정(아래 참고 참조)
외에는 직접 의존성이 없습니다.

## 참고

- 테스트 전용 패키지(xUnit, Apache-2.0)는 배포물에 포함되지 않으므로 앱 내 라이선스 목록에서
  제외합니다.
- 제거한 레거시 직접 의존성: WebView2, NAudio, RestSharp, SevenZipSharp, LZMA-SDK,
  Newtonsoft.Json, System.Web, System.Deployment, 수동 고정 BCL 어셈블리.
- 번들 서드파티 폰트는 재배포 허가를 확인할 수 없어 제거했습니다.
- 평문 토큰 파일을 만들지 않습니다. 토큰은 가능한 경우 OS 보호를 사용하는 MSAL 캐시에
  보관됩니다(Windows DPAPI, macOS Keychain, Linux libsecret 키링).
- Tmds.DBus.Protocol은 Avalonia.Desktop의 전이 의존성(MIT)이며, GHSA-xrw6-gwf8-vvr9 때문에
  CustomLauncher와 CustomLauncher.ManifestTool에서 0.21.3으로 직접 고정했습니다. 새 런타임 기능이
  아니라 Avalonia 스택의 일부이므로 앱 내 라이선스 목록(`Views/AboutWindow.axaml`)에는 따로 올리지
  않습니다. Avalonia를 Tmds.DBus.Protocol 0.21.3 이상을 끌어오는 버전으로 올리면 이 고정을 지울 수
  있습니다.
- HtmlAgilityPack은 CmlLib.Core.Installer.NeoForge의 전이 의존성(MIT)입니다. 설치기가 쓰는 파싱
  라이브러리이므로 앱 내 라이선스 목록에는 따로 올리지 않습니다.

런타임 의존성이 바뀌면 이 파일과 `Views/AboutWindow.axaml`을 함께 갱신하세요.
