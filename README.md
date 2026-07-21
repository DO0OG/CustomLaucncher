# CustomLauncher

.NET 8과 Avalonia로 작성된 Windows, macOS, Linux용 Minecraft 서버 런처입니다.

## 주요 기능

- Microsoft/Xbox 계정 로그인과 세션 복원
- OS 관례에 맞춘 설정·로그·게임 데이터 경로 및 기존 설정 마이그레이션
- 서버 상태/MOTD/접속자 표시와 장애 백오프
- Discord Rich Presence 사용자 선택 기능
- Java 탐색·검증·Adoptium 자동 설치, 최소/최대 RAM 및 JVM 인자 관리
- SHA-256, HTTPS, 경로 탈출, 압축 폭탄, 취소, 롤백을 처리하는 모듈 업데이트
- 필수/선택/드롭인 모드와 쉐이더팩·리소스팩 관리
- Velopack 시작 훅과 OS별 self-contained 패키징 워크플로
- 서버 운영자용 배포 매니페스트 생성/비교 CLI

## 빌드 및 테스트

```shell
dotnet restore CustomLauncher/CustomLauncher.sln
dotnet build CustomLauncher/CustomLauncher.sln -c Release
dotnet test CustomLauncher/CustomLauncher.sln -c Release
dotnet run --project CustomLauncher/CustomLauncher.csproj
```

GitHub Actions는 Windows, macOS, Linux에서 빌드·테스트·포맷을 검증합니다.

## 매니페스트 도구

```shell
dotnet run --project CustomLauncher.ManifestTool -- generate ./content \
  --base-url https://cdn.example.com/server/ --server-id example

dotnet run --project CustomLauncher.ManifestTool -- diff old.json distribution.json
```

아카이브는 파일 확장자로 추측하지 않습니다. 압축 해제가 필요한 모듈만
`--packaging archive`를 명시해야 합니다.

## 문서

- [`docs/OPERATOR_GUIDE.ko.md`](docs/OPERATOR_GUIDE.ko.md) — **서버 운영자용 한글 가이드.**
  새 서버용 런처 설정, 배포 폴더 구성, Seafile 업로드, 매니페스트 생성까지 전 과정
- [`docs/AUTH_FLOW.md`](docs/AUTH_FLOW.md) — Microsoft 인증 및 Azure 앱 등록
- [`docs/DISTRIBUTION_SCHEMA.md`](docs/DISTRIBUTION_SCHEMA.md) — 매니페스트 스키마와 호스팅 방식
- [`docs/DEPENDENCY_AUDIT.md`](docs/DEPENDENCY_AUDIT.md) — 의존성 감사
- [`docs/QA_MATRIX.md`](docs/QA_MATRIX.md) — 자동/수동 검증 범위

## 배포 전 필수 작업

공개 릴리스 전에는 실제 Microsoft 계정과 대상 서버 배포본을 이용한 3개 OS
수동 검증이 필요합니다. Windows 서명 인증서와 Apple Developer 서명/공증
자격 증명도 저장소 시크릿으로 제공해야 합니다. 상세 범위는
[`docs/QA_MATRIX.md`](docs/QA_MATRIX.md)를 참고하세요.

## 주요 라이선스

- Avalonia UI — MIT
- CmlLib.Core — MIT
- SharpCompress — MIT
- DiscordRichPresence — MIT
- Velopack — MIT
- xUnit — Apache-2.0
