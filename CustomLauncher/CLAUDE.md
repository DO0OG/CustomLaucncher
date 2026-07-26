# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 프로젝트 개요

CmlLib.Core 기반 Minecraft 커스텀 런처. **.NET 8 + Avalonia(MVVM)** 크로스플랫폼 데스크톱 앱
(Windows / macOS / Linux). 이전의 .NET Framework 4.7.2 + WinForms 구현은 제거됐다.

## 빌드 명령어

```bash
dotnet restore CustomLauncher/CustomLauncher.sln
dotnet build   CustomLauncher/CustomLauncher.sln
dotnet test    CustomLauncher/CustomLauncher.sln
dotnet format  CustomLauncher/CustomLauncher.sln --verify-no-changes
```

CI(`.github/workflows/ci.yml`)가 Windows/macOS/Linux 3종에서 위 네 가지를 모두 실행한다.
**경고 0개 / 포맷 변경 0건이 머지 조건이다.**

SDK-style 프로젝트이므로 `.cs` 파일을 `.csproj`에 등록할 필요는 없다.

## 솔루션 구성

| 프로젝트 | 역할 |
|---|---|
| `CustomLauncher` | 런처 본체 (Avalonia UI + Core 서비스) |
| `CustomLauncher.Shared` | 런처와 매니페스트 도구가 공유하는 스키마 (`DistroModule`, `ServerDistribution` 등) |
| `CustomLauncher.ManifestTool` | 서버 운영자용 매니페스트 생성/비교 CLI |
| `CustomLauncher.Tests` | xUnit 테스트 |

## 아키텍처

진입점: `Program.cs` → `VelopackApp` → Avalonia `App` → `MainWindow` + `MainViewModel`

```
CustomLauncher/
├── Core/                     # UI 비종속 로직
│   ├── AppPaths.cs             # OS별 Config/Log/GameDir 경로 + 레거시 마이그레이션
│   ├── AppSettingsManager.cs   # settings.json 로드/저장 (원자적 쓰기, 스키마 버전)
│   ├── AuthService.cs          # MSAL 디바이스 코드 인증 (docs/AUTH_FLOW.md 참조)
│   ├── LauncherService.cs      # 실행 파이프라인 조립
│   ├── GameSessionPreparer.cs  # 매니페스트→콘텐츠→Java→모드로더→설치→프로세스
│   ├── ContentUpdateService.cs # ModuleManager 래퍼 + 오프라인 캐시
│   ├── ModuleManager.cs        # 콘텐츠 동기화 (해시 검증·경로 탈출 방어·롤백)
│   ├── ModuleValidation.cs     # 매니페스트/경로/아카이브 보안 검증
│   ├── ModuleSelection.cs      # 선택형 모듈 on/off를 매니페스트에 적용
│   ├── ModLoaderInstaller.cs   # Forge/Fabric 설치
│   ├── DropInPackManagerBase.cs / DropInModManager / Shader / ResourcePackManager
│   ├── OptionsTextEditor.cs    # options.txt 키 단위 치환 (백업·원자적 쓰기)
│   ├── Java/                   # OS별 탐색, 검증, Adoptium 설치, 프로비저닝
│   └── ServerStatusPollingService.cs  # 백오프 폴링
├── ViewModels/               # MVVM
├── Views/                    # Avalonia XAML
└── Models/                   # LauncherSettings, JavaConfig 등
```

### 지켜야 할 규칙

1. **경로는 반드시 `AppPaths`를 거친다.** `Environment.GetFolderPath`를 직접 호출하지 않는다.
2. **OS 분기는 인터페이스 + 구현체로 한다.** `IJavaDiscovery`처럼 팩토리에서 선택한다.
   비즈니스 로직에 `if (OperatingSystem.IsWindows())`를 흩뿌리지 않는다.
3. **설정값은 `LauncherConfig`(컴파일 타임) 또는 `distribution.json`(런타임) 중 한 곳에만 둔다.**
   같은 값을 View와 Config에 중복 정의하지 않는다.
4. **`AsyncCommand`에서 예외가 새어나가면 안 된다.** `async void`이므로 프로세스가 죽는다.
   ViewModel의 `RunAsync`가 실패를 `Message`로 바꾸고, 처리되지 않은 것은
   `AsyncCommand.UnhandledError`로 로깅된다.
5. **Core가 돌려주는 상태 enum을 버리지 않는다.** `OptionsEditStatus`, `ModuleUpdateStatus`,
   `JavaInstallResult`는 반드시 사용자에게 표시한다. 성공과 "적용 안 됨"이 구분돼야 한다.
6. 새 의존성을 추가하면 `docs/DEPENDENCY_AUDIT.md`와 `Views/AboutWindow.axaml`을 함께 갱신한다.

### 보안 불변식 (`ModuleManager` / `ModuleValidation`)

- 매니페스트와 모듈 URL은 HTTPS만 허용
- 다운로드 후 SHA-256 재검증 (`FixedTimeEquals`)
- 경로 탈출(`..`, 절대 경로), 심볼릭 링크, 암호화 엔트리 거부
- 아카이브 엔트리 수/크기 상한
- 런처가 소유하지 않은 파일은 덮어쓰지 않음 → 사용자 드롭인 콘텐츠 보존
- staging → backup → commit 순서로 적용하며 실패 시 롤백
