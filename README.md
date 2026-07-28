# CustomLauncher

.NET 8과 Avalonia로 작성된 Windows · macOS · Linux용 Minecraft 서버 런처입니다.
하나의 코드베이스를 서버별로 빌드해서 배포하며, 플레이어는 자신의 Microsoft 계정으로
로그인하고 실행할 때마다 바뀐 콘텐츠만 내려받습니다.

## 주요 기능

- 시스템 브라우저 기반 Microsoft/Xbox 로그인(브라우저 불가 환경은 디바이스 코드로 폴백),
  세션 복원, 계정 전환을 위한 로그아웃
- OS 관례에 맞춘 설정·로그·게임 데이터 경로, 런처별 폴더 분리, 기존 설정 마이그레이션
- 서버 상태·MOTD·접속자 수 표시와 장애 시 백오프
- Java 탐색·검증·Adoptium 자동 설치, 시스템 메모리 기반 RAM 권장값, JVM 인자 관리
- SHA-256·HTTPS·경로 탈출·압축 폭탄·취소·롤백을 처리하는 모듈 업데이트
- 필수/선택/드롭인 모드, 셰이더팩, 리소스팩 관리(드래그앤드롭 순서)
- 멀티플레이 목록(`servers.dat`)에 서버 자동 등록(플레이어가 넣은 항목 보존)
- 창으로 조작하는 배포 매니페스트 생성 도구
- 색·배경·투명도를 한 파일(`Appearance.axaml`)에서 제어하는 외형 설정
- Velopack 시작 훅과 OS별 self-contained 패키징 워크플로

## 빠른 시작 (운영자)

새 서버용 런처를 만들고 콘텐츠를 배포하는 전 과정은
**[서버 운영자 가이드](docs/OPERATOR_GUIDE.ko.md)** 에 단계별로 정리돼 있습니다. 요약하면:

1. `CustomLauncher/LauncherConfig.cs`에서 서버 이름·주소·버전·클라이언트 ID를 설정
2. Microsoft 인증 앱 등록 ([AUTH_FLOW.ko.md](docs/AUTH_FLOW.ko.md))
3. 배포 폴더를 서버에 올리고 매니페스트 도구로 `distribution.json` 생성
4. (선택) `CustomLauncher/Appearance.axaml`에서 색·배경 이미지·투명도 조정

## 빌드 및 테스트

```shell
dotnet restore CustomLauncher/CustomLauncher.sln
dotnet build   CustomLauncher/CustomLauncher.sln -c Release
dotnet test    CustomLauncher/CustomLauncher.sln -c Release
dotnet run --project CustomLauncher/CustomLauncher.csproj
```

GitHub Actions가 Windows·macOS·Linux에서 빌드·테스트·포맷(140개 테스트)을 검증합니다.

## 매니페스트 도구

배포용 exe로 빌드합니다.

```shell
dotnet publish CustomLauncher.ManifestTool -c Release -r win-x64 \
  --self-contained false -p:PublishSingleFile=true -o ./dist/manifesttool
```

`ManifestTool.exe`를 **더블클릭하면 창**이 열립니다(배포 폴더 선택, 다운로드 URL 입력, 이전
매니페스트 대비 변경 내역 표시, 입력값 기억). 인자를 주면 명령줄로도 동작해 자동화에 쓸 수
있습니다. 자세한 사용법은 [운영자 가이드](docs/OPERATOR_GUIDE.ko.md)를 참고하세요.

## 문서

| 문서 | 내용 |
|---|---|
| [서버 운영자 가이드](docs/OPERATOR_GUIDE.ko.md) | 런처 설정·배포 전 과정 (한글) |
| Microsoft 인증 | [한글](docs/AUTH_FLOW.ko.md) · [English](docs/AUTH_FLOW.md) |
| 배포 매니페스트 스키마 | [한글](docs/DISTRIBUTION_SCHEMA.ko.md) · [English](docs/DISTRIBUTION_SCHEMA.md) |
| 의존성 감사 | [한글](docs/DEPENDENCY_AUDIT.ko.md) · [English](docs/DEPENDENCY_AUDIT.md) |
| QA 매트릭스 | [한글](docs/QA_MATRIX.ko.md) · [English](docs/QA_MATRIX.md) |

## 배포 전 필수 작업

공개 릴리스 전에는 실제 계정과 대상 서버 배포본을 이용한 3개 OS 수동 검증, 그리고 Windows
서명 인증서와 Apple Developer 서명/공증 자격 증명이 필요합니다. 상세 범위는
[QA 매트릭스](docs/QA_MATRIX.ko.md)를 참고하세요.

## 주요 라이선스

- Avalonia UI — MIT
- CmlLib.Core / CmlLib.Core.Auth.Microsoft / CmlLib.Core.Installer.Forge — MIT
- XboxAuthNet.Game.Msal — MIT
- SharpCompress — MIT
- DiscordRichPresence — MIT
- Velopack — MIT
