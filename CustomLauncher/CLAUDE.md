# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 프로젝트 개요

CmlLib.Core를 사용한 Minecraft 커스텀 런처. .NET Framework 4.7.2 + Windows Forms 기반 데스크톱 앱.
빌드 출력 어셈블리명은 `DOGLAUNCHER.exe`.

## 빌드 명령어

```bash
# NuGet 패키지 복원
nuget restore CustomLauncher.sln

# Debug 빌드
msbuild CustomLauncher.sln /p:Configuration=Debug

# Release 빌드
msbuild CustomLauncher.sln /p:Configuration=Release
```

빌드 결과물: `bin\Debug\` 또는 `bin\Release\`

테스트 프레임워크 없음. 린터 설정 없음.

> **중요**: `.csproj`는 구형(non-SDK) 스타일로 `<Compile Include="..." />` 항목이 명시적으로 필요합니다.
> 새 `.cs` 파일을 추가할 때는 반드시 `.csproj`에도 항목을 추가해야 합니다.

## 아키텍처

### 진입점
`Program.cs` → `MainForm` 실행

### 폴더 구조

```
CustomLauncher/
├── Models/                  # 데이터 모델
│   ├── LauncherSettings.cs  # 런처 설정 및 사용자 정보 모델
│   ├── UpdateManifest.cs    # 자동 업데이트 매니페스트 모델 (FileManifest, UpdateManifest)
│   └── CMLaunchOption.cs    # Minecraft 게임 실행 옵션 모델 (G1GC/Log4j2 패치 포함)
├── Core/                    # 서비스 로직
│   ├── AppSettingsManager.cs  # 설정 파일 로드/저장 (AppData/customServer_settings.txt)
│   ├── UserDataManager.cs     # 사용자 인증 데이터 AES 암호화 로드/저장
│   └── ServerStatusChecker.cs # mcsrvstat.us API로 서버 온라인 상태 확인
├── MainForm.cs              # 메인 UI (로그인, 게임 실행, 서버 상태 폴링)
├── SettingsForm.cs          # 설정 화면 (해상도, 설치 경로, RAM)
├── AutoUpdater.cs           # SHA256/버전 파일 기반 자동 업데이트
├── EncryptionHelper.cs      # AES-256 암호화 유틸리티
├── DebugLogger.cs           # 파일 기반 디버그 로거 (AppData/rooftop_debug_log.txt)
└── FontLibrary.cs           # DNFBitBitv2 폰트 로드 및 ApplyToControls() 제공
```

### 핵심 데이터 흐름
1. **설정**: `AppSettingsManager.Load/Save()` → `customServer_settings.txt` (해상도, 설치 경로, RAM)
2. **인증**: `JELoginHandler` → `UserDataManager.Save()` → AES 암호화 → `customServer_udata`
3. **업데이트**: `AutoUpdater.CheckForUpdatesAsync()` → 원격 `manifest.json` → SHA256 비교 → 7z 다운로드/추출
4. **게임 실행**: `MinecraftLauncher` + `ForgeInstaller` → `MLaunchOption` → `ProcessWrapper`

### 주요 NuGet 의존성
| 패키지 | 용도 |
|--------|------|
| CmlLib.Core 4.0.6 | Minecraft 런처 핵심 |
| CmlLib.Core.Auth.Microsoft 3.3.1 | Xbox 인증 |
| CmlLib.Core.Installer.Forge 1.1.1 | Forge 설치 |
| NAudio 2.2.1 | 배경음악 재생 |
| Newtonsoft.Json 13.0.4 | JSON 파싱 |
| RestSharp 113.0.0 | HTTP 요청 |
| Microsoft.Web.WebView2 1.0.3650.58 | 인앱 웹뷰 |
| SharpZipLib / SevenZipSharp | ZIP/7z 압축 해제 |
