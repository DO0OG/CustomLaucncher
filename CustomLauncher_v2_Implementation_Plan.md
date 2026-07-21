# CustomLauncher 2.0 크로스플랫폼 통합 구현 기획서

**작성일**: 2026-07-21 (3차 개정 — 소스 전수 대조 기반 보강. 개정 이력은 §8 참조)
**대상**: CustomLauncher (.NET Framework 4.7.2 WinForms → .NET 8 Avalonia 크로스플랫폼 전환 + 전체 기능 확장)
**작업 분담**: 구현 = Codex CLI / 검증 = Claude Code
**목표 플랫폼**: Windows, macOS, Linux
**기반 원칙**: CmlLib.Core는 그대로 유지한다. Microsoft/Xbox 인증, 버전 매니페스트 파싱, 라이브러리/에셋 다운로드, Forge/Fabric 설치는 전부 CmlLib에 위임한다. 새로 만드는 것은 UI 레이어, 배포/관리 레이어, 그리고 이번에 확정된 크로스플랫폼 대응 레이어다.

---

## 0. 문서 사용법

이 문서는 Codex에게 그대로 전달하는 작업지시서 겸, Claude가 결과물을 검증할 때 쓰는 체크리스트다. 각 Phase는 동일한 구조를 따른다.

- **목표**: 무엇을 만드는가
- **데이터 모델**: 추가/변경되는 클래스
- **Codex 작업 지시**: 생성/수정할 파일과 요구사항을 파일 단위로 명시
- **Claude 검증 체크리스트**: PR 리뷰 시 반드시 확인할 항목

### 작업 프로토콜 (Codex ↔ Claude)

1. Codex는 Phase 단위로만 커밋한다. 한 커밋에 여러 Phase를 섞지 않는다.
2. Phase 완료 시 Codex는 변경 파일 목록, 수동 테스트 시나리오, 알려진 제약사항을 함께 제출한다. **크로스플랫폼 대상 Phase는 3개 OS(Windows/macOS/Linux) 각각에서 실행한 결과를 함께 제출한다.** 하나의 OS에서만 테스트하고 통과 처리하지 않는다.
3. Claude는 해당 Phase의 검증 체크리스트를 항목별로 pass/fail로 리뷰하고, fail 항목은 구체적 재현 조건과 함께 반려한다.
4. Phase 간 의존관계가 있는 경우 반드시 순서대로 진행하고, 선행 Phase의 스키마는 해당 Phase 검증 완료 시점에 동결한다.
5. 절대 여러 Phase를 한 번에 뭉쳐서 병렬 작업하지 않는다.

---

## 1. 2차 전수 점검에서 발견된 사항 요약

원본 코드(`CustomLaucncher-main.zip`)를 처음부터 다시 확인해 아래 항목들을 새로 반영했다. 1차 재작성본에서는 다뤄지지 않았던 부분들이다.

| # | 발견 사항 | 영향 Phase | 심각도 |
|---|---|---|---|
| 1 | MS 인증이 `CmlLib.Core.Auth.Microsoft`의 WinForms+WebView2 팝업에 의존 (`packages.config`의 `Microsoft.Web.WebView2`, csproj의 `Microsoft.Web.WebView2.WinForms`/`.Wpf` 참조로 확인). 크로스플랫폼에서 이 팝업 자체가 아예 안 뜬다 | Phase 2 (신규) | 치명적 |
| 2 | `.csproj`가 구형 non-SDK 스타일 + .NET Framework 4.7.2. Avalonia/크로스플랫폼은 SDK-style + .NET 8 필요 | Phase 0 (신규) | 치명적 |
| 3 | `EncryptionHelper.cs`가 Windows DPAPI(`DataProtectionScope.CurrentUser`) 전용 — macOS/Linux에 대응 API 없음 | Phase 3 (신규) | 치명적 |
| 4 | `AutoUpdater.cs`가 임베디드 `7z.exe`(Windows 실행파일)를 `Process.Start`로 실행 — macOS/Linux에서 실행 불가 | Phase 8 | 치명적 |
| 5 | `NAudio`(배경음악 재생)가 Windows 오디오 API(WaveOut/WASAPI/ASIO) 전용 라이브러리 | Phase 1 | 치명적 |
| 6 | Java 자동 탐색이 Windows 레지스트리 전제로만 설계돼 있었음 (1차 문서 기준) | Phase 7 | 높음 |
| 7 | `FolderBrowserDialog`(WinForms 전용) — 설치 경로 선택에 사용 | Phase 1 | 높음 |
| 8 | `Environment.SpecialFolder.ApplicationData` 하나만 쓰면 macOS에서 Helios 위키가 명시하는 OS 관례 경로(`~/Library/Application Support`)와 다르게 매핑될 수 있음 | Phase 0, 1 | 중간 |
| 9 | 아이콘이 `.ico`만 존재 — macOS `.icns`, Linux `.png` 세트 없음 | Phase 11 | 중간 |
| 10 | `FontLibrary.cs`의 임베디드 폰트 재귀 적용 로직이 Avalonia 이식 계획에 빠져 있었음 | Phase 1 | 중간 (1차 지적사항, 미반영이었음) |
| 11 | `DebugLogger.cs`에 로그 레벨/구조화가 없음 — 최초 비교 논의에서 나왔으나 어느 Phase에도 반영 안 됐던 항목 | Phase 1 | 중간 (누락 항목) |
| 12 | `AboutForm.cs`의 오픈소스 라이선스 목록이 하드코딩 — 새 의존성 추가 시 갱신 필요하다는 규칙이 없었음 | 전 Phase 공통 | 낮음 |
| 13 | 배경음악 관련 필드(`isMuted`, `musicPath`)가 선언만 있고 실제 재생/음소거 UI가 코드 어디에도 없는 미완성 기능. `btnSettings2_Click`도 어떤 컨트롤에도 연결 안 된 고아 핸들러 | Phase 1 | 낮음 (정리 여부 결정 필요) |
| 14 | `packages.config`/`.csproj`에 실제 코드에서 호출되는 흔적을 찾지 못한 패키지 다수 (`RestSharp`, `HtmlAgilityPack`, `SevenZipSharp`, `LZMA-SDK`, `System.Web`, `System.Deployment` 등) | Phase 0 | 낮음 |
| 15 | `SharpCompress`가 이미 `packages.config`에 있는데 실제로는 미사용으로 보임 — 4번 문제(7z 크로스플랫폼 해제) 해결에 그대로 재활용 가능 | Phase 8 | 참고사항(기회) |

### 1.1 3차 점검 추가 발견 사항 (소스 전수 대조)

2차 문서 작성 후 소스를 다시 한 줄씩 대조해 아래 항목을 추가로 발견했다. 이 중 A·B·C는 기존 Phase의 **전제 자체를 바꾸는** 항목이다.

| # | 발견 사항 | 영향 Phase | 심각도 |
|---|---|---|---|
| A | **`UserDataManager`가 저장하는 accessToken은 실제로 아무 데서도 읽히지 않는다.** `MainForm_Shown`은 `userData.Username`의 존재 여부만 "로그인한 적 있음" 플래그로 쓰고, 실제 세션 복원은 `JELoginHandler.Authenticate()`가 CmlLib 자체 캐시 파일에서 수행한다. 즉 **진짜 보호가 필요한 자산은 `customServer_udata`가 아니라 CmlLib의 세션 캐시 JSON**이다. Phase 3을 이 전제로 다시 써야 한다 | Phase 3 | 치명적(설계 전제 오류) |
| B | 테스트 프레임워크·린터·CI가 전무하다. 그런데 본 문서는 거의 모든 Phase에서 "3개 OS에서 검증"을 요구한다. **검증을 강제할 수단(멀티 OS 매트릭스 CI)이 없으면 이 요구사항은 전부 선언에 그친다** | Phase 0 (신규 항목) | 치명적 |
| C | `AutoUpdater`가 원격 매니페스트의 `path`를 검증 없이 `Path.Combine(baseDirectory, ...)`에 넣고, 아카이브도 검증 없이 추출한다 → **경로 탈출(zip slip) 가능**. 또한 다운로드 완료 후 해시를 **재검증하지 않고** 바로 추출한다(비교는 다운로드 전에만 함). URL이 HTTPS인지 강제하는 로직도 없다 | Phase 8 | 치명적(보안) |
| D | `Models/CMLaunchOption.cs`는 어디서도 인스턴스화되지 않는 죽은 코드다. `btnStartGame_Click`은 CmlLib의 `MLaunchOption`을 직접 만든다 → **G1GC 튜닝 인자와 Log4Shell(`-Dlog4j2.formatMsgNoLookups=true`) 패치가 실제 게임 실행에 전혀 적용되고 있지 않다.** 단순 삭제가 아니라 Phase 7에서 기본 JVM 인자로 되살려야 한다 | Phase 7 | 높음 |
| E | `CustomLauncher_TemporaryKey.pfx`가 리포지토리에 커밋돼 있다. 서명 키 자산이 VCS에 들어간 상태 | Phase 0, 11 | 높음(보안) |
| F | `AutoUpdater`가 아카이브 추출 전 `mods` 폴더를 **통째로 삭제**한다. Phase 8에서 드롭인 모드를 도입하면 사용자가 직접 넣은 모드가 매 업데이트마다 소실된다 | Phase 8 | 높음 |
| G | 서버 상태 폴링이 10초 간격이다. `mcsrvstat.us`는 응답을 수 분간 캐시하며 과도한 요청을 차단할 수 있다 → 폴링 주기 상향 + 실패 시 백오프 필요 | Phase 5 | 중간 |
| H | 게임 설치 경로와 런처 설정 경로가 뒤섞여 있다. 기본 설치 경로가 `%AppData%\.custom`인데, macOS/Linux에서는 게임 데이터를 설정 디렉토리(`~/.config`)에 두는 것이 관례에 어긋난다. `AppPaths`는 **설정 경로와 게임 데이터 경로를 분리**해야 한다 | Phase 0 | 중간 |
| I | 오디오 후보인 LibVLCSharp은 LGPL이고 VLC 네이티브 런타임 번들이 필요하다 → 배포 시 LGPL 준수 의무(동적 링크 유지, 라이선스 고지)가 발생한다. 라이선스 검토 없이 확정하면 안 된다 | Phase 1 | 중간 |
| J | `DebugLogger.cs` 등 일부 파일에 인코딩이 깨진 한글 주석이 남아있다(`???�로그램`). .NET 8 전환 시 전 소스 UTF-8 정규화가 필요 | Phase 0 | 낮음 |
| K | `CancellationTokenSource`가 `MainForm` 필드로 선언만 되고 사용되지 않는다 → 다운로드/설치 취소 기능이 없다. Phase 8의 대용량 다운로드에는 취소가 사실상 필수 | Phase 8 | 중간 |
| L | UI 문자열이 전부 하드코딩된 한국어다. 크로스플랫폼으로 배포 범위를 넓히는 이번 전환에서 다국어 지원 여부를 결정해두지 않으면 나중에 전 파일을 다시 건드려야 한다 | Phase 1 | 낮음(결정 필요) |

---

## 1.5 이식 전 반드시 처리할 기존 버그 (회귀 기준선)

**원본 코드에는 이미 버그가 있다.** "WinForms 버전과 동일하게 동작하는가"를 검증 기준으로 삼으면 이 버그들까지 충실히 재현하게 된다. 아래는 이식 시 **고쳐야 할 목록**이며, Codex는 "원본과 동일" 대신 "아래 표의 기대 동작"을 기준으로 구현한다.

| 위치 | 현재 동작(버그) | 기대 동작 |
|---|---|---|
| `MainForm_Shown` / `btnStartGame_Click` | 시작 시에는 `new MinecraftPath()`(기본 `.minecraft`)로 런처를 초기화하고, 실행 시에는 `new MinecraftLauncher(directory)`로 **다른 경로의 런처를 새로 만든다** → 설정한 설치 경로가 초기화 단계에서 무시됨 | 설치 경로를 단일 소스로 삼아 런처 인스턴스를 1회만 구성 |
| `btnStartGame_Click` | `installSettings.InstallPath`가 null이면 `Path.Combine(null)`에서 NRE | 로드 시점에 기본 경로로 폴백 (`MainForm_Shown`에는 폴백이 있으나 실행 경로에는 없음) |
| `MainForm_Shown` | `new SettingsForm()`을 만들어 `saveSettings()`를 호출한다 → **런처 시작만 해도 UI 기본값이 저장 파일을 덮어쓴다** (사용자가 저장한 해상도/RAM이 초기화될 수 있음) | 시작 시 설정을 저장하지 않는다. 읽기 전용 로드만 수행 |
| `btnStartGame_Click` | `new SettingsForm()`을 또 만들어 `GetSelectedResolution()`을 호출 → 저장된 설정이 아니라 **폼 기본값(1920x1080)** 이 적용될 수 있음 | 저장된 설정 모델에서 해상도를 읽는다 |
| RAM 설정 | `MinimumRamMb = MaximumRamMb = ramMb` 단일 값 | Phase 7의 `JavaConfig`에서 Min/Max 분리 |
| `UserDataManager.Load` | 복호화 실패 시 **조용히 파일을 삭제**한다 | 로그를 남기고 사용자에게 재로그인이 필요함을 알린다 |
| `AutoUpdater.CheckForUpdatesAsync` | 모든 예외를 삼키고 `false`를 반환 → 업데이트 실패와 "최신 상태"가 UI에서 구분되지 않는다 | 실패는 실패로 UI에 보고 |
| `AutoUpdater` | `GetInstalledVersionAsync`가 `async`인데 내부는 동기 `File.ReadAllText` (`Task` 반환 경고성 코드) | 실제 비동기 I/O로 통일 |
| `MainForm` | `_httpClient` User-Agent를 Windows Chrome으로 위장 | 런처 식별용 UA로 교체 (`CustomLauncher/2.0 (+URL)`) |
| `AboutForm` | 라이선스 목록에 `SharpZipLib`, `SharpCompress`, `LZMA-SDK` 등 실제 참조 패키지 일부가 누락 | Phase 0 의존성 감사 결과와 1:1 동기화 |

### Claude 검증 체크리스트 (Phase 1·7·8 리뷰 시 공통)
- [ ] 위 표의 각 항목이 "원본과 동일하게" 이식되지 않고 기대 동작으로 수정됐는가
- [ ] Codex가 원본 버그를 발견하고도 그대로 옮긴 항목이 없는가

---

## 2. 전체 범위 요약

| Phase | 내용 | 예상 기간 |
|---|---|---|
| 0 | 프레임워크 마이그레이션 + 의존성 감사 + **테스트/CI 인프라 구축** (신규) | 8~11일 |
| 1 | Avalonia MVVM 스켈레톤 이식 (폰트/오디오/타이틀바/로깅/죽은 코드 정리 포함) | 1.5~2.5주 |
| 2 | 인증 전환 — WebView2 제거, MSAL.NET 기반 크로스플랫폼 로그인 (신규) | 1~1.5주 |
| 3 | 크로스플랫폼 보안 저장소 — EncryptionHelper 대체 (신규) | 3~5일 |
| 4 | 설정 파일 JSON화 | 0.5일 |
| 5 | 서버 상태 체크 확장 | 1~2일 |
| 6 | Discord RPC (크로스플랫폼 IPC 검증 포함) | 3~4일 |
| 7 | Java 관리 (크로스플랫폼 탐색/자동 설치) | 1.5주 |
| 8 | 모듈형 매니페스트 + Mod Management (SharpCompress로 압축 해제 전환) | 2~3주 |
| 9 | 쉐이더팩 관리 | 4~6일 |
| 10 | 리소스팩 관리 (드래그앤드롭 정렬 포함) | 6~8일 |
| 11 | 런처 자체 자동 업데이트 (Velopack, OS별 패키징) | 1주 |
| 12 | 통합 QA (Windows/macOS/Linux 매트릭스) | 1.5~2주 |
| 13 | 서버 운영자용 매니페스트 생성 도구 (크로스플랫폼 CLI) | 4~6일 |

**총합: 약 12~16주.** 크로스플랫폼 확정 전 추정치(8~11주)보다 늘어난 이유는 Phase 0(프레임워크 마이그레이션 + CI 인프라), Phase 2(인증 재작성), Phase 3(보안 저장소 재작성)이 통째로 새로 생겼고, Phase 7·11·12가 멀티 OS 검증 부담으로 길어졌기 때문이다.

> **추정 전제 명시**: 위 기간은 *상시 투입 1인(구현은 Codex 대행) + 리뷰어 1인* 기준이다. 파트타임이라면 실제 달력 기간은 2~3배로 본다. 또한 Phase 1은 원본 UI가 배경 이미지·이미지 버튼 기반의 픽셀 배치 커스텀 UI라 Avalonia 레이아웃으로 재현하는 비용이 과소평가되기 쉽다 — 착수 후 실측으로 재조정한다.

**의존 관계**
- Phase 0이 끝나야 Phase 1 이후 전부 착수 가능 (SDK-style/.NET 8 기반이 없으면 Avalonia 자체가 안 올라감).
- Phase 2, 3은 Phase 1(스켈레톤) 완료 후 착수하되 서로 독립적이라 병렬 가능. **단 Phase 3은 Phase 2에서 확정되는 "CmlLib이 세션 캐시를 어디에 어떤 형식으로 쓰는가"에 의존하므로, Phase 2의 캐시 경로/포맷 조사 결과를 먼저 받아야 한다.**
- Phase 9, 10은 Phase 8의 스키마에 의존 — Phase 8 검증 완료 전 착수 금지.
- Phase 13은 Phase 8 스키마에 의존하지만 런처 본체와 독립된 프로젝트라 Phase 9~12와 병렬 가능.

### 2.1 브랜치 · 롤백 전략 (신규)

13개 Phase를 `main`에 직행시키면 중간에 문제가 생겼을 때 되돌릴 지점이 없다.

- 통합 브랜치 `v2/main`을 파고, 각 Phase는 `v2/phase-N-<슬러그>` 브랜치에서 작업 후 PR로 `v2/main`에 머지한다.
- `main`은 **기존 WinForms 버전(1.x)의 유지보수 라인**으로 남긴다. v2 완료 전까지 기존 유저 대상 핫픽스가 필요할 수 있다.
- Phase 검증 통과 시 `v2/main`에 `v2-phase-N` 태그를 찍는다. 롤백 단위는 태그다.
- Phase 12(통합 QA) 통과 후에만 `v2/main` → `main` 머지 + 메이저 릴리스.
- **롤백 기준을 착수 전에 정한다**: Phase 0의 CmlLib 크로스플랫폼 스파이크가 실패하면 로드맵 전면 재검토, Phase 2의 3-OS 실계정 로그인이 실패하면 해당 OS를 1차 지원 대상에서 제외할지 결정.

---

## 3. 아키텍처 개요

```
CustomLauncher.Views/        ← Avalonia XAML (MainWindow, SettingsWindow, 각 탭)
CustomLauncher.ViewModels/   ← MVVM 바인딩
CustomLauncher.Core/         ← UI/OS 비종속 로직 + OS별 구현체
CustomLauncher.Models/       ← 데이터 모델
CustomLauncher.Shared/       ← 런처 본체와 Phase 13 도구가 함께 참조하는 스키마 라이브러리
```

OS별로 달라져야 하는 로직(보안 저장소, Java 탐색, 압축 해제 등)은 인터페이스 + OS별 구현체 패턴을 공통으로 쓴다.

```csharp
public interface IPlatformService { }

public static class PlatformDetector
{
    public static bool IsWindows => OperatingSystem.IsWindows();
    public static bool IsMacOS => OperatingSystem.IsMacOS();
    public static bool IsLinux => OperatingSystem.IsLinux();
}
```

각 Manager 클래스는 `if (PlatformDetector.IsWindows) ... else if (IsMacOS) ... else` 형태의 산발적 분기 대신, `ISecretStorage`, `IJavaDiscovery` 같은 인터페이스를 두고 OS별 구현 클래스를 DI로 주입받는 구조로 통일한다. 이렇게 해야 Claude 검증 시 "새 OS 지원이 추가될 때 기존 코드를 안 건드리고 구현체만 추가할 수 있는가"를 일관되게 확인할 수 있다.

---

## 4. 크로스플랫폼 대응 원칙 (신규 — 전 Phase 공통)

이번 재작성의 핵심 축이다. 아래 표는 원본 코드의 Windows 전용 요소와 그 대체 방안을 정리한 것이다.

| 원본 코드 요소 | 문제 | 대체 방안 | 담당 Phase |
|---|---|---|---|
| WebView2 기반 MS 로그인 팝업 | macOS/Linux에서 WinForms+WebView2 자체가 존재하지 않음 | MSAL.NET 기반 인증(브라우저 리다이렉트 또는 디바이스 코드 플로우)으로 전환 — CmlLib.Core.Auth.Microsoft가 공식 지원 | Phase 2 |
| `EncryptionHelper`(DPAPI) | macOS/Linux에 DPAPI 없음 | `ISecretStorage` 추상화: Windows=DPAPI, macOS=Keychain, Linux=Secret Service(libsecret) 또는 OS 미지원 시 사용자 파일 권한으로 보호된 AES 폴백 | Phase 3 |
| 임베디드 `7z.exe` 실행 | macOS/Linux 실행 불가 | `SharpCompress`(이미 있는 미사용 의존성)로 순수 관리 코드 압축 해제로 전환, 네이티브 실행파일 의존 제거 | Phase 8 |
| `NAudio` 배경음악 재생 | Windows 오디오 API 전용 | 크로스플랫폼 오디오 라이브러리로 교체 (후보: LibVLCSharp 등 — Phase 1에서 스파이크 테스트 후 확정) | Phase 1 |
| Windows 레지스트리 기반 Java 탐색 | macOS/Linux에 레지스트리 없음 | OS별 `IJavaDiscovery` 구현체: Windows=레지스트리+표준 경로, macOS=`/usr/libexec/java_home` + `~/Library/Java/JavaVirtualMachines`, Linux=`update-alternatives` + `/usr/lib/jvm` | Phase 7 |
| `FolderBrowserDialog`(WinForms) | Avalonia에 해당 컨트롤 없음 | Avalonia `IStorageProvider.OpenFolderPickerAsync()` (자체가 크로스플랫폼) | Phase 1 |
| `Environment.SpecialFolder.ApplicationData` | .NET의 기본 매핑이 macOS 관례(`~/Library/Application Support`)와 다를 수 있음 | `Core/AppPaths.cs` 신규: OS별로 Helios 위키가 명시한 것과 동일한 관례 경로를 직접 계산 (Windows `%AppData%`, macOS `~/Library/Application Support`, Linux `~/.config` 또는 `$XDG_CONFIG_HOME`) | Phase 0, 1 |
| `.ico`만 존재하는 아이콘 | macOS는 `.icns`, Linux는 여러 크기의 `.png` 세트 필요 | 아이콘 세트 추가 제작 | Phase 11 (배포 패키징 시점) |
| Java 실행 파일명 (`javaw.exe` vs `java`) | OS별로 실행 파일명이 다름 | `IJavaDiscovery` 구현체 내에서 OS별 실행 파일명 분기 | Phase 7 |
| Velopack 배포 | 자체는 크로스플랫폼 지원하지만 OS별 패키징 산출물 형식이 다름 (Windows: Squirrel 인스톨러, macOS: `.pkg`/zip, Linux: AppImage 등) | OS별 릴리스 파이프라인 구성 | Phase 11 |
| Discord RPC IPC | Windows는 Named Pipe, macOS/Linux는 Unix Domain Socket | 라이브러리가 내부적으로 처리하는지 Phase 6에서 3 OS 모두 직접 검증 | Phase 6 |

### Codex 공통 작업 지시
- 새 OS 종속 로직을 만들 때는 반드시 인터페이스 + OS별 구현체 패턴을 따른다. `if (OperatingSystem.IsWindows())`를 비즈니스 로직 클래스 여기저기에 흩뿌리지 않는다.
- OS별 구현체가 준비되지 않은 플랫폼에서는 기능을 조용히 실패시키지 말고, 명확한 예외 메시지("이 기능은 현재 Linux에서 지원되지 않습니다")로 사용자에게 알린다.

### Claude 공통 검증 항목 (전 Phase, 해당 시)
- [ ] 새 OS 종속 기능이 인터페이스+구현체 패턴을 따르는가, 로직 곳곳에 `if(IsWindows)` 산발 분기가 있지는 않은가
- [ ] 실제로 3개 OS(또는 최소 Windows + Linux 컨테이너/VM)에서 빌드 및 핵심 플로우 실행이 확인됐는가
- [ ] 지원 안 되는 플랫폼에서 크래시 대신 명확한 에러 메시지가 나오는가

---

## 5. 설정 단일화 원칙 (Single Source of Configuration)

원본 코드의 `LauncherConfig.cs`는 서버 주소, 버전, 매니페스트 URL 등을 한 파일에 상수로 몰아넣어서, 새 서버용 런처를 만들 때 그 파일 하나만 고치면 되게 설계돼 있다. 이 원칙을 두 단계로 나눠서 유지한다.

### 5.1 컴파일 타임 설정 — `LauncherConfig.cs` 확장
재빌드해야 반영되는 값(런처 이름, Discord Client ID, 기능 on/off 플래그 등)은 전부 이 한 파일에만 추가한다.

```csharp
public static class LauncherConfig
{
    // 기존 상수들...
    public const string DiscordClientId = "...";
    public const string JavaRuntimeFolderName = "runtime";
    public const bool EnableShaderPackManagement = true;
    public const bool EnableResourcePackManagement = true;
    public const bool EnableDiscordRpc = true;
}
```

### 5.2 런타임 설정 — `distribution.json` (Phase 8의 `ServerDistribution`)
재빌드 없이 서버 운영자가 바꿔야 하는 값(모드/쉐이더/리소스팩 목록, Java 요구 버전 등)은 전부 `distribution.json`으로 몰아넣는다.

### 5.3 공통 유지보수 규칙
- 새 NuGet 의존성을 추가하는 Phase는 반드시 `AboutForm`(또는 그 Avalonia 이식본)의 오픈소스 라이선스 목록에 항목을 추가한다. (원본 `AboutForm.cs`에 하드코딩돼 있던 목록을 그대로 방치하면 새 의존성이 계속 누락된다.)
- 새로운 설정값이 필요할 때마다 "컴파일 타임 고정값인가, 서버 운영자가 재빌드 없이 바꿀 수 있어야 하는가"를 먼저 판단하고 5.1/5.2 중 하나로 수렴시킨다.

### Claude 검증 항목 (전 Phase 공통)
- [ ] 새로 추가된 설정값이 `LauncherConfig.cs` 또는 `ServerDistribution` 둘 중 하나로 수렴하는가
- [ ] 같은 값이 여러 파일에 중복 정의돼 있지 않은가
- [ ] 새 의존성이 추가됐다면 라이선스 목록이 함께 갱신됐는가

---

## Phase 0: 프레임워크 마이그레이션 + 의존성 감사 (신규, 최우선)

### 목표
.NET Framework 4.7.2 + 구형 non-SDK 스타일 프로젝트를 .NET 8 + SDK-style로 전환하고, 기존 의존성 전체의 크로스플랫폼/​.NET 8 호환성을 검증한다. 이 Phase가 끝나기 전에는 다른 어떤 Phase도 착수하지 않는다.

### Codex 작업 지시
- `CustomLauncher.csproj`를 SDK-style로 전환 (`<Project Sdk="Microsoft.NET.Sdk">`), `TargetFramework`을 `net8.0`으로 변경 (WinForms 전용 코드가 남아있는 동안은 과도기적으로 `net8.0-windows`를 병행 타깃으로 둘 수 있으나, 최종적으로는 `net8.0`으로 수렴)
- 의존성 감사표 작성 후 Codacy/코드 검색으로 실제 호출부가 있는지 확인, 없는 패키지는 제거 후보로 표시:
  - 제거 후보: `RestSharp`, `HtmlAgilityPack`, `SevenZipSharp`, `LZMA-SDK`, `System.Web`, `System.Deployment`, `Microsoft.Web.WebView2.*`(Phase 2 완료 후 제거), `NAudio.*`(Phase 1에서 대체 라이브러리로 교체 후 제거)
  - 유지: `CmlLib.Core` 계열, `Newtonsoft.Json`, `SharpCompress`(Phase 8에서 실제로 쓰기 시작), `ZstdSharp`(SharpCompress 내부 의존성일 가능성 있음 — 제거 전 확인 필수)
- 의존성 감사 시 **각 패키지의 TFM(대상 프레임워크)도 함께 기록**한다. `netstandard2.0`으로만 배포되는 패키지(CmlLib 계열 등)는 .NET 8에서 로드는 되지만 내부적으로 Windows 전용 API를 호출할 수 있으므로, 크로스플랫폼 가능 여부는 TFM만으로 판단하지 말고 스파이크로 확인한다.
- `Core/AppPaths.cs` 신규: OS별 데이터 폴더 경로를 Helios 위키 관례에 맞게 직접 계산하는 유틸리티. 이후 모든 Phase에서 `Environment.SpecialFolder.ApplicationData`를 직접 호출하지 않고 이 클래스를 통하도록 강제한다. **아래 3가지 경로를 각각 분리해서 제공한다** (원본은 설정·로그·게임 데이터를 전부 `%AppData%` 루트에 평평하게 흩뿌려 놓았다):

  | 용도 | Windows | macOS | Linux |
  |---|---|---|---|
  | 설정 (`ConfigDir`) | `%AppData%\CustomLauncher` | `~/Library/Application Support/CustomLauncher` | `$XDG_CONFIG_HOME`(기본 `~/.config`)`/CustomLauncher` |
  | 로그·캐시 (`LogDir`) | `%LocalAppData%\CustomLauncher\logs` | `~/Library/Logs/CustomLauncher` | `$XDG_STATE_HOME`(기본 `~/.local/state`)`/CustomLauncher` |
  | 게임 데이터 기본값 (`DefaultGameDir`) | `%AppData%\.custom` | `~/Library/Application Support/custom` | `~/.custom` |

  또한 **원본의 평평한 경로(`%AppData%\customServer_settings.txt` 등)에서 새 하위 폴더 구조로 옮기는 1회성 마이그레이션**을 이 클래스에 함께 넣는다. 이걸 Phase 0에서 안 하면 Phase 4·3에서 각자 다른 방식으로 마이그레이션을 구현하게 된다.
- 전 소스 파일 **UTF-8(BOM 없음) 정규화**: 현재 `DebugLogger.cs` 등에 인코딩이 깨진 한글 주석이 남아있다. 전환 과정에서 일괄 복구한다.
- **`CustomLauncher_TemporaryKey.pfx`를 리포지토리에서 제거**하고 `.gitignore`에 서명 키 패턴(`*.pfx`, `*.snk`, `*.p12`)을 추가한다. 이미 커밋된 이력에 남아있으므로 해당 키는 폐기하고 재발급 대상으로 표시한다(Phase 11 코드 서명과 연동).
- **크로스플랫폼 스파이크 테스트**: 이 시점에서 CmlLib.Core 4.0.6이 실제로 Linux/macOS에서 Minecraft 버전 조회 + 라이브러리 다운로드 + (가능하면) 실행까지 되는지 최소 스파이크로 검증한다. 여기서 막히면 이후 모든 Phase의 전제가 무너지므로 최우선으로 확인한다.

**테스트 · CI 인프라 (신규 — 이 Phase의 핵심 산출물)**

본 문서는 거의 모든 Phase에서 "3개 OS에서 검증"을 요구한다. 그 요구를 실제로 강제할 수단을 Phase 0에서 먼저 만든다. 없으면 이후 모든 검증 체크리스트가 자기 신고에 의존하게 된다.

- `CustomLauncher.Tests` 프로젝트 신규 (xUnit). 최소한 아래는 단위 테스트 대상이다 — UI 없이 검증 가능한 순수 로직들이다:
  - `AppPaths` OS별 경로 계산 및 레거시 경로 마이그레이션
  - 설정 마이그레이션(Phase 4), RAM 계산(Phase 7), `options.txt` 파싱/치환(Phase 9·10)
  - 매니페스트 diff 로직과 **경로 탈출 방어**(Phase 8)
- GitHub Actions 워크플로 신규: `runs-on: [windows-latest, macos-latest, ubuntu-latest]` 매트릭스로 `dotnet build` + `dotnet test`를 3 OS에서 자동 실행. **PR 머지 조건으로 건다.**
- 코드 스타일 강제: `.editorconfig` + `dotnet format --verify-no-changes`를 CI에 추가 (현재 린터 설정이 전무함).
- 각 Phase 완료 보고서에 CI 실행 링크를 첨부하게 한다.

> 자동화로 커버되지 않는 것(실계정 로그인, 오디오 재생, Discord IPC, GUI 렌더링)은 여전히 수동 테스트가 필요하다. Phase 0에서 **수동 테스트 항목과 자동 테스트 항목의 경계를 문서로 확정**하고, 수동 항목은 3 OS 실기기/VM 접근 수단(예: macOS 실기기 또는 CI러너, Linux VM)을 착수 전에 확보한다. 이 확보가 안 되면 크로스플랫폼 목표 자체를 재검토해야 한다.

### Claude 검증 체크리스트
- [ ] SDK-style 전환 후 Windows에서 기존과 동일하게 빌드/실행되는가 (회귀 없음 확인)
- [ ] 의존성 감사표가 실제 코드 검색(grep/IDE 참조 찾기) 근거와 **각 패키지 TFM**과 함께 제출됐는가, "안 쓰는 것 같다"는 추측만으로 제거하지 않았는가
- [ ] `Core/AppPaths.cs`가 설정/로그/게임 데이터 경로를 **분리**해서 제공하고, 3개 OS에서 위 표와 동일하게 매핑되는가
- [ ] 레거시 평평 경로 → 신규 구조 마이그레이션이 `AppPaths` 한 곳에 구현됐는가 (Phase 3·4에서 중복 구현되지 않도록)
- [ ] **3 OS 매트릭스 CI가 실제로 초록불이고, PR 필수 체크로 설정됐는가**
- [ ] 소스 인코딩이 UTF-8로 정규화되고 깨진 주석이 복구됐는가
- [ ] `.pfx`가 리포에서 제거되고 `.gitignore`에 등록됐는가
- [ ] CmlLib.Core 크로스플랫폼 스파이크 테스트가 실제로 수행됐고, 결과(성공/부분성공/실패)가 명확히 보고됐는가 — 실패 시 이후 Phase 진행 여부를 재논의해야 하는 게이트임을 인지
- [ ] 수동 테스트가 필요한 항목의 3 OS 실행 환경이 실제로 확보됐는가 (계획서상 "확보 예정"은 불가)

---

## Phase 1: Avalonia MVVM 스켈레톤 이식

### 목표
기존 WinForms UI를 Avalonia + MVVM으로 전환한다. 이번 재작성에서는 폰트, 오디오, 로깅, 죽은 코드 정리까지 이 Phase에 포함시킨다 (1차 문서에서 누락됐던 부분).

### Codex 작업 지시

**UI 이식**
- `Views/MainWindow.axaml`, `Views/SettingsWindow.axaml`, `Views/AboutWindow.axaml` (+ ViewModel) — 기존 레이아웃/기능을 그대로 재현
- 타이틀바 드래그: `ReleaseCapture`/`SendMessage` P/Invoke → Avalonia `PointerPressed` + `BeginMoveDrag` (이미 크로스플랫폼)
- 설치 경로 선택: `FolderBrowserDialog` → Avalonia `IStorageProvider.OpenFolderPickerAsync()`

**폰트 (1차 문서 누락분)**
- `Core/FontLibrary.cs`의 임베디드 DNFBitBitv2 폰트 로직을 Avalonia 방식으로 전환: 폰트 파일을 `Assets/Fonts/`에 두고 `avares://CustomLauncher/Assets/Fonts/DNFBitBitv2.ttf`로 참조, 앱 전역 `Styles`에 `FontFamily` 리소스로 등록해 모든 컨트롤에 일괄 적용 (기존처럼 컨트롤 트리를 재귀 순회하며 개별 적용할 필요 없음 — Avalonia 스타일 시스템이 이걸 대체)

**오디오 (신규 — 크로스플랫폼 교체 필요)**
- `Core/AudioService.cs` 신규: 기존 `MainForm.cs`에 필드로 박혀있던 NAudio 재생 로직을 분리
- NAudio는 Windows 전용이므로 교체 필요. 후보 라이브러리로 스파이크 테스트 후 확정. **선정 시 크기뿐 아니라 라이선스를 반드시 함께 검토한다**:
  - LibVLCSharp: LGPL + VLC 네이티브 런타임 번들 필요 → **LGPL 준수 의무 발생**(동적 링크 유지, 라이선스 고지, 소스 제공 경로 안내). 번들 크기도 수십 MB 단위로 늘어난다.
  - 대안: `ManagedBass`(BASS는 비상업 무료/상업 유료 — 라이선스 확인 필수), `SDL2` 바인딩, 또는 **배경음악을 짧은 루프 하나로 제한하고 OS 기본 재생 API를 얇게 감싸는 자체 `IAudioPlayer` 구현**.
  - 배경음악 하나 때문에 배포 크기가 수십 MB 늘고 LGPL 의무가 붙는다면, 아래 "기능 완성 여부 결정"에서 **제외 쪽을 기본안으로 검토**하는 것을 권한다.
- **기능 완성 여부 결정 필요**: 원본 코드의 `isMuted`, `musicPath` 필드는 선언만 있고 실제 재생 트리거/음소거 버튼이 없는 미완성 상태였다. Codex는 이걸 그대로 이식하지 말고, (a) 배경음악을 정식 기능으로 완성(음소거 버튼 UI 포함)할지 (b) 이번 범위에서 제외할지 착수 전에 결정해서 진행한다. 결정 없이 죽은 필드만 그대로 옮기지 않는다.

**로깅 구조화 (누락됐던 항목 반영)**
- `Core/DebugLogger.cs`에 `LogLevel`(Debug/Info/Warn/Error) 개념 추가, 로그 파일 크기 기준 롤링(예: 5MB 초과 시 `.old`로 이동) 추가
- 로그 파일 경로는 Phase 0의 `Core/AppPaths.cs`를 통해 계산 (직접 `Environment.GetFolderPath` 호출 금지)

**문자열 · 다국어 (신규 결정 항목)**
- 현재 모든 UI 문자열이 소스에 하드코딩된 한국어다. Avalonia 이식은 전 화면을 다시 쓰는 시점이므로, **다국어 지원 여부를 지금 결정**한다. 나중에 하려면 전 파일을 또 건드려야 한다.
- 최소안: 지금은 한국어만 지원하더라도 문자열을 `Assets/Strings/ko.json`(또는 `.resx`) 한 곳에 모아두기만 한다. 언어 추가는 나중에 파일만 추가하면 되는 구조가 된다.

**죽은 코드 정리**
- `MainForm.cs`의 `btnSettings2_Click`처럼 어떤 컨트롤에도 연결되지 않은 고아 핸들러는 이식하지 않는다 (Avalonia ViewModel의 `Command`로 옮길 때 자연히 걸러지겠지만, Codex는 명시적으로 "이 핸들러는 원본에서 미사용이라 제외함"이라고 작업 보고에 남긴다)
- `Models/CMLaunchOption.cs`는 미사용 죽은 코드지만 **그냥 삭제하지 않는다**. 이 클래스에만 들어있는 G1GC 튜닝 인자와 Log4Shell 패치(`-Dlog4j2.formatMsgNoLookups=true`)는 실전에 적용된 적이 없으므로, Phase 7의 기본 JVM 인자로 이관한 뒤 클래스를 제거한다. (§1.1 D 항목)
- §1.5 "이식 전 반드시 처리할 기존 버그" 표의 UI/설정 관련 항목(설치 경로 이중 초기화, 시작 시 설정 덮어쓰기, 해상도를 폼 기본값에서 읽는 문제)은 이 Phase에서 함께 수정한다.

### Claude 검증 체크리스트
- [ ] 로그인 → 게임 시작 → 종료까지 전체 플로우가 동작하는가 (3개 OS). **단 "원본과 동일"이 아니라 §1.5 표의 기대 동작 기준으로 판정한다**
- [ ] §1.5의 UI/설정 관련 버그 4건이 실제로 수정됐는가 (특히 런처를 켜기만 해도 설정이 덮어써지는 문제)
- [ ] UI 문자열이 한 곳에 모여 있는가, 다국어 지원 여부가 명시적으로 결정·문서화됐는가
- [ ] 오디오 라이브러리 선정 시 라이선스(특히 LGPL 의무)와 배포 크기 영향이 함께 보고됐는가
- [ ] 폰트가 재귀 순회 방식이 아니라 전역 스타일로 일괄 적용되는가, 그리고 3개 OS에서 폰트가 실제로 렌더링되는가
- [ ] 오디오 대체 라이브러리가 실제로 3개 OS에서 소리가 나는가 (스파이크 테스트 결과 포함해서 제출됐는가)
- [ ] 배경음악 기능 완성/제외 여부가 명시적으로 결정되고 문서화됐는가, 원본처럼 죽은 필드만 옮겨놓지 않았는가
- [ ] `DebugLogger`가 로그 레벨을 구분해서 기록하는가, 파일 롤링이 실제로 동작하는가
- [ ] `Core/AppPaths.cs`를 거치지 않고 `Environment.SpecialFolder`를 직접 호출하는 곳이 남아있지 않은가

---

## Phase 2: 인증 전환 — WebView2 제거, MSAL.NET 기반 크로스플랫폼 로그인 (신규)

### 목표
`CmlLib.Core.Auth.Microsoft`의 기본 로그인 플로우가 내부적으로 WinForms `Form` + WebView2 컨트롤을 띄우는 방식(`JELoginHandlerBuilder.BuildDefault()`)이라는 게 확인됐다. 이건 macOS/Linux에서 아예 동작하지 않는다. MSAL.NET 기반(브라우저 리다이렉트 또는 디바이스 코드 플로우) 인증으로 전환한다.

### Codex 작업 지시
- `CmlLib.Core.Auth.Microsoft`가 제공하는 MSAL.NET 기반 빌더로 `JELoginHandlerBuilder` 구성을 교체 (WebView2 관련 빌더 대신 사용)
- 로그인 UX: 시스템 기본 브라우저를 열어 MS 로그인 페이지로 리다이렉트하고, 로컬 루프백으로 콜백을 받는 방식이 3 OS 모두에서 가장 안정적이므로 이 방식을 기본으로 채택
- **선행 작업 — Azure 앱 등록 확인**: MSAL 기반 루프백 리다이렉트는 클라이언트 ID와 리다이렉트 URI(`http://localhost`) 등록을 전제로 한다. CmlLib이 내장 클라이언트 ID를 제공하는지, 자체 Azure Entra 앱 등록이 필요한지, 그리고 Minecraft(Xbox) 인증에 그 앱이 승인돼야 하는지를 **코드 작성 전에 먼저 확인해서 보고**한다. 여기서 막히면 Phase 2 전체가 진행 불가다.
- **폴백 플로우 필수**: 브라우저를 띄울 수 없는 환경(헤드리스, 원격 SSH, 일부 리눅스 데스크톱)을 위해 **디바이스 코드 플로우를 폴백으로 함께 구현**한다. 루프백 하나만 구현하면 해당 환경에서 로그인이 아예 불가능해진다.
- 루프백 서버는 **고정 포트를 쓰지 말고 임의 유휴 포트를 할당**한다(포트 충돌·방화벽 프롬프트 회피). 콜백 대기에 타임아웃을 두고, 취소 시 리스너를 확실히 해제한다.
- `Core/AuthService.cs` 신규: `MainViewModel`에서 기존 `loginHandler.Authenticate()` 호출부를 그대로 유지하되 내부 구현만 교체 (인터페이스 변경 최소화로 다른 Phase에 영향 안 가게)
- **CmlLib 세션 캐시 조사 결과를 산출물로 제출한다**: `JELoginHandler`가 세션/리프레시 토큰을 *어느 경로에, 어떤 형식으로, 암호화 여부는 어떻게* 저장하는지 확인해 문서화한다. **Phase 3의 보호 대상이 바로 이 파일이므로**(§1.1 A), 이 조사 없이는 Phase 3을 착수할 수 없다.
- `Microsoft.Web.WebView2.*` NuGet 참조 제거 (Phase 0 의존성 감사 결과와 연동)

### Claude 검증 체크리스트
- [ ] 3개 OS 모두에서 실제 MS 계정으로 로그인이 성공하는가 (이 부분이 이번 재작성 전체에서 가장 리스크가 큰 지점 — 실계정 테스트 없이 통과 처리하지 않는다)
- [ ] Azure 앱 등록/클라이언트 ID 요구사항이 사전 조사돼 보고됐는가
- [ ] 브라우저를 열 수 없는 환경에서 디바이스 코드 폴백으로 로그인이 되는가
- [ ] 루프백 포트가 동적 할당이고, 로그인 취소 시 리스너가 누수 없이 해제되는가
- [ ] 로그인 실패/취소 시 3 OS 모두에서 동일하게 에러 메시지가 뜨는가
- [ ] 자동 로그인(저장된 세션 재사용)이 새 인증 방식에서도 동작하는가 — **원본의 "udata에 Username이 있으면 로그인한 적 있음"이라는 우회 판정 대신, 실제 세션 캐시 유효성으로 판정하도록 고쳤는가**
- [ ] CmlLib 세션 캐시의 경로·형식·암호화 여부 조사 결과가 문서로 제출됐는가 (Phase 3 착수 조건)
- [ ] WebView2 관련 참조가 프로젝트에서 완전히 제거됐는가

---

## Phase 3: 크로스플랫폼 보안 저장소 — EncryptionHelper 대체 (신규)

### 목표
Windows DPAPI 전용이던 `EncryptionHelper.cs`를 OS별 보안 저장소로 추상화한다.

> ### ⚠ 착수 전 전제 재정의 (§1.1 A)
> 2차 문서는 "`customServer_udata`에 저장된 accessToken을 크로스플랫폼으로 안전하게 저장한다"를 목표로 잡았다. **이 전제는 틀렸다.**
>
> 소스를 보면 `UserDataManager.Save()`가 쓴 accessToken을 **읽는 코드가 어디에도 없다.** `MainForm_Shown`은 `userData.Username`이 비어있지 않은지만 보고("이 PC에서 로그인한 적 있음" 플래그), 실제 세션 복원은 `JELoginHandler.Authenticate()`가 **CmlLib 자체 캐시 파일**에서 수행한다. 즉:
> - `customServer_udata`는 사실상 죽은 데이터이며, 토큰을 디스크에 남기는 **불필요한 공격면**이다.
> - **진짜 보호 대상은 CmlLib이 관리하는 세션/리프레시 토큰 캐시**다.
>
> 따라서 이 Phase는 다음 순서로 진행한다.
> 1. Phase 2에서 제출된 CmlLib 세션 캐시 조사 결과를 받는다(경로·형식·자체 암호화 여부).
> 2. **CmlLib이 이미 자체적으로 안전하게 저장하고 있다면**, 우리가 할 일은 `ISecretStorage` 신규 구현이 아니라 *캐시 경로를 `AppPaths` 아래로 통일하고, `UserDataManager`/`EncryptionHelper`/`customServer_udata`를 통째로 제거*하는 것이다. 이 경우 Phase 3은 3~5일이 아니라 1~2일로 줄어든다.
> 3. **CmlLib이 평문으로 저장한다면**, 그때 `ISecretStorage`를 도입해 캐시 파일을 감싼다.
>
> **Codex는 2번인지 3번인지 먼저 판정해서 보고한 뒤 착수한다. 판정 없이 `ISecretStorage` 3종 구현부터 만들지 않는다** — 쓰이지도 않는 저장소를 macOS Keychain까지 붙여가며 만드는 것이 이 Phase의 최대 낭비 리스크다.

### 데이터 모델 (위 3번으로 판정된 경우에만 해당)
```csharp
public interface ISecretStorage
{
    void Save(string key, string plainText);
    string? Load(string key);
    void Delete(string key);
}
```

### Codex 작업 지시
- `Core/Security/WindowsSecretStorage.cs`: 기존 DPAPI 로직 그대로 이식
- `Core/Security/MacOsSecretStorage.cs`: macOS Keychain 연동 (네이티브 상호운용 또는 커뮤니티 래퍼 라이브러리 검토)
- `Core/Security/LinuxSecretStorage.cs`: Secret Service(libsecret) D-Bus 연동. **libsecret이 없는 배포판/헤드리스 환경 대비 폴백 경로 필수**: 사용자 홈 디렉토리에 파일 권한(600)으로 제한한 AES 암호화 파일 저장 방식을 폴백으로 둔다
- `Core/Security/SecretStorageFactory.cs`: `PlatformDetector` 기준으로 적절한 구현체 반환
- `Core/UserDataManager.cs`: **제거를 기본안으로 한다.** 로그인 상태 표시에 필요한 정보(사용자명, 스킨 등)는 토큰과 분리해 평문 설정에 두고, 토큰은 CmlLib 캐시에만 존재하게 한다. 같은 비밀을 두 곳에 저장하지 않는다.
- 폴백 AES 파일 저장을 쓰는 경우, 키를 소스에 하드코딩하지 않는다(하드코딩 키는 난독화일 뿐 암호화가 아니다). 키 유도 방식과 그 한계를 주석과 보고서에 명시한다.
- Linux 폴백 파일은 퍼미션 `600` + 상위 디렉토리 `700`으로 생성하고, 생성 직후 실제 퍼미션을 검증한다.

### Claude 검증 체크리스트
- [ ] **착수 전 판정(CmlLib이 이미 안전 저장하는가)이 근거와 함께 보고됐는가.** 판정 없이 `ISecretStorage` 3종을 먼저 구현했다면 반려
- [ ] 토큰이 두 곳(우리 파일 + CmlLib 캐시)에 중복 저장되지 않는가
- [ ] 3개 OS 각각에서 저장 → 재시작 → 자동 로그인까지 실제로 되는가
- [ ] Linux에서 libsecret이 없는 환경(예: 최소 설치 서버 배포판, 헤드리스 컨테이너)에서 폴백 경로가 크래시 없이 동작하는가, 폴백 파일 퍼미션이 실제로 600인가
- [ ] 폴백 AES의 키가 소스에 하드코딩돼 있지 않은가, 하드코딩이 불가피하다면 그 한계가 명시됐는가
- [ ] 기존 Windows 유저의 DPAPI 암호화 파일 처리 방침(마이그레이션 or 재로그인 안내)이 명시적으로 구현됐는가 — 조용히 깨지면 안 된다. **레거시 `customServer_udata`는 마이그레이션 후 디스크에서 삭제하는가**
- [ ] `EncryptionHelper.cs`가 완전히 제거됐는가 (Windows 전용 API 잔존 금지)
- [ ] macOS Keychain 접근 시 OS 권한 프롬프트가 뜨는 게 정상 동작인지 확인하고 UX 문서에 남겼는가

---

## Phase 4: 설정 파일 JSON화

### 목표
`Core/AppSettingsManager.cs`의 줄바꿈 텍스트 저장 방식을 JSON 직렬화로 교체한다.

### Codex 작업 지시
- `Core/AppSettingsManager.cs` 수정: `File.WriteAllLines` → `JsonConvert.SerializeObject`/`DeserializeObject`
- 저장 경로는 Phase 0의 `Core/AppPaths.cs` 사용
- **마이그레이션 매핑을 명시적으로 구현한다.** 기존 포맷은 순서에만 의존하는 3줄 텍스트다:

  | 기존 파일 (줄 번호) | 신규 JSON 필드 |
  |---|---|
  | 1행 | `resolution` (`"1920x1080"` → `{ width, height }`로 분해 저장 권장) |
  | 2행 | `installPath` |
  | 3행 | `ramMb` (문자열 → 정수 파싱, 실패 시 기본값) |

  3줄 미만이거나 파싱 실패 시 해당 필드만 기본값으로 채우고 나머지는 살린다(전체 폐기 금지).
- **`LauncherSettings` 모델을 분리한다.** 현재 이 모델은 설정(해상도/경로/RAM)과 인증정보(Username/Password)를 한 클래스에 섞어놓았다 — 설정 JSON을 저장할 때 토큰이 함께 직렬화될 위험이 있다. `LauncherSettings`(설정)와 계정 정보를 별도 타입으로 분리한다.
- **원자적 저장**: 임시 파일에 쓰고 `File.Move`로 교체한다. 현재는 저장 중 종료되면 설정이 잘린 채 남는다.
- 스키마 버전 필드(`schemaVersion`)를 넣어 이후 Phase에서 설정 항목이 늘어날 때 마이그레이션 기준점을 만든다.

### Claude 검증 체크리스트
- [ ] 신규 설치 환경에서 JSON 저장/로드가 3개 OS 모두 정상 동작하는가
- [ ] 기존 텍스트 설정 파일 마이그레이션이 위 매핑표대로, 데이터 손실 없이 되는가 (**단위 테스트로 검증**)
- [ ] 일부 줄만 손상된 파일에서 나머지 값이 보존되는가
- [ ] 마이그레이션 실패 시 크래시 대신 기본값으로 안전하게 폴백하는가
- [ ] 설정 JSON에 액세스 토큰류가 직렬화되어 들어가지 않는가
- [ ] 저장이 원자적인가 (저장 도중 강제 종료 후에도 설정 파일이 유효한가)

---

## Phase 5: 서버 상태 체크 확장

### 목표
`Core/ServerStatusChecker.cs`를 플레이어 수·목록·MOTD까지 확장한다.

### Codex 작업 지시
- `mcsrvstat.us` 응답에서 `players.online`, `players.max`, `players.list`, `motd.clean` 파싱 추가
- `Models/ServerStatusInfo.cs` 신규
- UI에 플레이어 수 표시 라벨 추가
- **폴링 주기를 10초 → 60초 이상으로 상향한다.** `mcsrvstat.us`는 응답을 수 분간 캐시하므로 10초 폴링은 새 정보를 주지 못하면서 레이트리밋만 유발한다. 실패 시 지수 백오프(예: 60s → 120s → 300s 상한)를 적용하고, 성공하면 기본 주기로 복귀한다. (§1.1 G)
- 폴링은 `System.Windows.Forms.Timer`가 아니라 취소 가능한 `PeriodicTimer` 기반 백그라운드 루프로 구현하고, 창이 닫힐 때 확실히 취소한다.
- 창이 최소화/비활성 상태일 때는 폴링을 중단하거나 주기를 늘린다.
- HTTP 요청에 타임아웃을 명시한다(현재 `HttpClient` 기본 100초 → 폴링 주기보다 짧게).
- User-Agent를 브라우저 위장 문자열이 아니라 런처 식별자로 교체한다 (§1.5).

### Claude 검증 체크리스트
- [ ] `players` 필드가 없는 응답(서버가 정보 숨김)에서도 예외 없이 처리되는가
- [ ] 네트워크 오류 시 기존처럼 오프라인으로 안전하게 폴백하는가
- [ ] 폴링 주기가 API 정책에 부합하고, 연속 실패 시 백오프가 실제로 동작하는가
- [ ] 창을 닫은 뒤 폴링 태스크가 남아있지 않은가 (Phase 12 메모리 누수 항목과 연동)
- [ ] MOTD/플레이어 목록 문자열이 UI에 그대로 렌더링될 때 서식 코드(§)나 과도한 길이가 레이아웃을 깨뜨리지 않는가

---

## Phase 6: Discord Rich Presence

### 목표
로그인/게임 실행 상태에 맞춰 Discord Presence를 갱신한다. Windows는 Named Pipe, macOS/Linux는 Unix Domain Socket으로 IPC 방식이 다르므로 이번 재작성에서는 이 부분을 명시적으로 검증한다.

### Codex 작업 지시
- `DiscordRPC.NET` NuGet 추가, `Core/DiscordPresenceService.cs` 신규 (`Init`, `SetState`, `Shutdown`)
- 앱 종료 시 `Shutdown()` 호출 필수
- **3개 OS에서 IPC가 실제로 붙는지 개별 검증** (라이브러리 문서상 크로스플랫폼을 표방해도, Discord 데스크톱 앱 자체의 소켓/파이프 위치가 OS/배포판마다 조금씩 다를 수 있어 실제 확인이 필요함)
- **Linux는 패키징 형태별로 소켓 경로가 다르다.** 네이티브 설치는 `$XDG_RUNTIME_DIR/discord-ipc-0`이지만, Flatpak은 `$XDG_RUNTIME_DIR/app/com.discordapp.Discord/`, Snap은 `$XDG_RUNTIME_DIR/snap.discord/` 아래에 있다. 라이브러리가 이들을 모두 탐색하는지 확인하고, 안 되면 후보 경로를 순회하는 탐색 로직을 우리가 추가한다. **Flatpak Discord는 리눅스에서 흔한 설치 형태이므로 "네이티브에서만 됨"은 통과가 아니다.**
- 라이브러리 선정 시 유지보수 상태(최근 커밋, .NET 8 지원 여부)를 확인해 보고한다.
- Presence 표시 항목에 **개인정보가 새지 않도록 한다** — 사용자명/UUID를 그대로 노출할지 여부를 결정하고, 기본값은 노출하지 않는 쪽으로 둔다. `LauncherConfig.EnableDiscordRpc` 외에 **사용자가 UI에서 끌 수 있는 토글**을 제공한다.

### Claude 검증 체크리스트
- [ ] 3개 OS 각각에서 Discord 실행 중일 때 Presence가 실제로 갱신되는가
- [ ] Linux에서 **Flatpak/Snap 설치 Discord**로도 연결되는가 (네이티브 설치만 확인한 경우 반려)
- [ ] 사용자가 Presence를 UI에서 끌 수 있는가, 기본 표시 문자열에 개인 식별 정보가 포함되지 않는가
- [ ] 앱 강제 종료 시에도 일정 시간 후 Presence가 자동 해제되는가
- [ ] Discord 미실행 상태에서 크래시 없이 조용히 무시되는가 (3 OS 공통)

---

## Phase 7: Java 관리 (크로스플랫폼)

### 목표
Helios 방식(실행 전 유효성 검사 → 자동 설치 제안, RAM 슬라이더, 커스텀 실행파일, JVM 인자 편집)을 크로스플랫폼으로 구현한다.

### 데이터 모델
```csharp
public interface IJavaDiscovery
{
    string? ScanSystemForValidJava();
    string ExecutableFileName { get; } // "javaw.exe" | "java"
}

public class JavaConfig
{
    public string? ExecutablePath { get; set; }
    public int MinRamMb { get; set; }
    public int MaxRamMb { get; set; }
    public List<string> CustomJvmArguments { get; set; } = new();
}
```

### Codex 작업 지시
- `Core/Java/WindowsJavaDiscovery.cs`: 레지스트리 + 표준 설치 경로 탐색, 실행 파일명 `javaw.exe`
- `Core/Java/MacJavaDiscovery.cs`: `/usr/libexec/java_home -V` 실행 결과 파싱 + `~/Library/Java/JavaVirtualMachines` 스캔, 실행 파일명 `java`
- `Core/Java/LinuxJavaDiscovery.cs`: `update-alternatives --list java` + `/usr/lib/jvm` 스캔, 실행 파일명 `java`
- `Core/JavaValidator.cs`: `java -version` 실행 후 출력 파싱 (OS 무관 공용 로직)
- `Core/JavaInstaller.cs`: Adoptium API(`api.adoptium.net`)로 OS/아키텍처별 OpenJDK 빌드 조회/다운로드, 설치 위치는 `Core/AppPaths.cs` 기준 런처 전용 로컬 경로
- `Core/RamCalculator.cs`: Helios 공식 이식 (OS 무관 공용 로직)
- **기본 JVM 인자 복원 (§1.1 D)**: 죽은 코드였던 `Models/CMLaunchOption.cs`의 G1GC 튜닝 인자와 **Log4Shell 패치(`-Dlog4j2.formatMsgNoLookups=true`)** 를 `JavaConfig`의 기본값으로 이관한다. 원본에서는 이 인자들이 정의만 되고 실제 실행에 **한 번도 적용된 적이 없다.** 이관 후 `CMLaunchOption.cs`를 제거한다.
- `JavaConfig`에 `MinRamMb`/`MaxRamMb`가 분리돼 있으므로, 기존 설정의 단일 `RamValue`는 마이그레이션 시 Max에 넣고 Min은 별도 기본값(예: Max의 절반 또는 512MB)으로 설정한다.
- macOS Apple Silicon(arm64) / Intel(x64) 구분, Linux arm64를 Adoptium API 조회 시 아키텍처 파라미터로 정확히 전달한다. **Rosetta로 x64 JDK가 잘못 설치되지 않도록 주의.**
- 다운로드한 JDK 아카이브는 **Adoptium이 제공하는 체크섬으로 검증**한 뒤 설치한다. macOS/Linux에서는 압축 해제 후 `java` 바이너리에 실행 퍼미션(`+x`)을 부여해야 한다 — 순수 관리 코드 압축 해제 시 유닉스 퍼미션이 유실되는 대표적 함정이다.
- `ViewModels/JavaSettingsViewModel.cs`, `Views/JavaSettingsTab.axaml`

### Claude 검증 체크리스트
- [ ] 3개 OS 각각에서, Java가 전혀 없는 깨끗한 환경에서 자동 설치 → 게임 실행까지 되는가
- [ ] **G1GC 인자와 Log4Shell 패치가 실제 실행 프로세스의 커맨드라인에 포함되는가** (프로세스 인자를 캡처해 증거로 제출)
- [ ] Apple Silicon에서 arm64 JDK가 설치되는가 (x64가 설치되면 fail)
- [ ] 다운로드한 JDK의 체크섬을 검증하는가, macOS/Linux에서 `java` 실행 퍼미션이 부여되는가
- [ ] 기존 단일 RAM 설정이 Min/Max로 손실 없이 마이그레이션되는가
- [ ] macOS/Linux에서 실행 파일명이 올바르게 `java`(확장자 없음)로 지정되는가, Windows의 `javaw.exe` 관례가 잘못 섞여 들어가지 않았는가
- [ ] RAM 계산 경계값(6GB, 16GB)이 정확한가 (단위 테스트)
- [ ] 커스텀 JVM 인자에 `-Xmx`/`-Xms` 직접 입력 시 경고가 뜨는가
- [ ] Adoptium API 장애 시 에러 메시지가 명확하고 앱이 멈추지 않는가

---

## Phase 8: 모듈형 매니페스트 + Mod Management (SharpCompress 전환 포함)

### 목표
`.7z` 통짜 배포(임베디드 `7z.exe` 실행 방식)를 개별 모듈 단위 diff로 교체하면서, 동시에 압축 해제 방식 자체를 크로스플랫폼 호환되는 `SharpCompress`로 전환한다. Helios 방식의 모드 관리(필수/선택 Built-in, 서브모드 의존성, 드롭인)도 구현한다.

### 데이터 모델
```csharp
public enum ModuleType { RequiredMod, OptionalMod, DropInMod, ShaderPack, ResourcePack }

public class DistroModule
{
    public string Id { get; set; }
    public string Path { get; set; }
    public string Url { get; set; }
    public string Hash { get; set; }
    public ModuleType Type { get; set; }
    public string? ParentId { get; set; }
    public int? LoadOrder { get; set; }
    public bool IsServerManaged { get; set; }
}

public class JavaRequirement
{
    public string MinVersion { get; set; }
    public string RecommendedVersion { get; set; }
    public int? MinRamMb { get; set; }
}

public class ServerDistribution
{
    public string ServerId { get; set; }
    public List<DistroModule> Modules { get; set; }
    public JavaRequirement Java { get; set; }
    public string? DefaultShaderPackId { get; set; }
}
```

### Codex 작업 지시
- `Models/DistroModule.cs`, `Models/ServerDistribution.cs` 신규
- `Core/ModuleManager.cs` 신규: `ModuleType`에 무관하게 동작하는 공용 diff/다운로드 로직. **압축 해제는 `SharpCompress`의 `SevenZipArchive`/`ArchiveFactory`를 사용하고, 임베디드 `7z.exe` 및 `Ensure7ZipInstalledAsync` 계열 로직은 완전히 제거한다.**
- `Core/AutoUpdater.cs` 리팩터링: 기존 `.7z` 통짜 로직 및 Windows 전용 7z.exe 실행 코드 제거, `ModuleManager` 호출로 교체
- `ViewModels/ModManagementViewModel.cs`: Required/Optional 모드, 서브모드 의존성 UI, 드롭인 관리
- `Views/ModManagementTab.axaml`

#### 보안 요구사항 (신규 — §1.1 C. 이 Phase에서 가장 중요한 항목)

원본 `AutoUpdater`는 **원격 서버가 지정한 경로에 원격 서버가 준 파일을 검증 없이 푼다.** 매니페스트 호스트가 침해되면 사용자 PC의 임의 위치에 임의 파일을 쓸 수 있다. 새 `ModuleManager`는 아래를 반드시 만족한다.

- **경로 탈출 방어**: 매니페스트의 `Path`와 아카이브 내부 엔트리 경로를 모두 정규화(`Path.GetFullPath`)한 뒤, 결과가 게임 디렉토리 하위인지 검증한다. `..`, 절대 경로, 드라이브 문자, 심볼릭 링크 엔트리는 거부한다. (zip slip / 7z slip)
- **다운로드 후 해시 재검증**: 원본은 다운로드 *전* 로컬 해시만 비교하고, 받은 파일은 검증 없이 추출한다. 받은 파일의 SHA256이 매니페스트 값과 일치할 때만 적용하고, 불일치 시 폐기 후 재시도한다.
- **HTTPS 강제**: 매니페스트 URL과 모든 모듈 `Url`이 `https`가 아니면 거부한다.
- **원자적 적용**: 임시 디렉토리에 받아 검증까지 끝낸 뒤 최종 위치로 옮긴다. 중간에 실패해도 게임 디렉토리가 반쯤 갱신된 상태로 남지 않게 한다.
- **압축 폭탄 방어**: 압축 해제 전 엔트리 총 크기 상한과 엔트리 개수 상한을 둔다.
- **드롭인 모드 보존 (§1.1 F)**: 원본은 업데이트 시 `mods` 폴더를 통째로 삭제한다. Phase 8에서 드롭인 모드를 도입하면 사용자 파일이 매번 소실된다. **서버 관리 모듈만 선택적으로 교체**하고, `IsServerManaged=false` 파일은 절대 건드리지 않는다.
- **취소 지원 (§1.1 K)**: 모든 다운로드/해제 경로에 `CancellationToken`을 관통시키고 UI에 취소 버튼을 둔다. 원본의 `CancellationTokenSource` 필드는 선언만 되고 쓰이지 않았다.
- 실패는 조용히 삼키지 않고 UI에 보고한다 (§1.5).

### Claude 검증 체크리스트
- [ ] **압축 해제가 3개 OS 모두에서 네이티브 실행파일 없이 순수 관리 코드로 동작하는가** (이번 전환의 핵심 검증 포인트)
- [ ] **경로 탈출 방어가 동작하는가 — `../../evil.txt` 엔트리를 담은 악성 아카이브와 `Path`가 `../`로 시작하는 매니페스트로 실제 테스트했는가 (단위 테스트 필수)**
- [ ] 다운로드 완료 후 해시를 재검증하는가, 불일치 파일이 적용되지 않는가
- [ ] `http://` URL이 거부되는가
- [ ] 업데이트 중간에 실패했을 때 게임 디렉토리가 일관된 상태로 남는가
- [ ] **사용자가 직접 넣은 드롭인 모드가 업데이트 후에도 살아남는가** (`mods` 폴더 통째 삭제가 제거됐는가)
- [ ] 다운로드 중 취소가 실제로 동작하고, 부분 다운로드 파일이 정리되는가
- [ ] 모드 1개만 변경됐을 때 나머지 모드는 재다운로드하지 않는가
- [ ] 서브모드 부모/자식 의존성이 올바르게 동작하는가 (부모 비활성 시 자식도 비활성)
- [ ] 드롭인 모드 추가/삭제/토글이 정상 동작하는가
- [ ] `ModuleManager`가 실제로 Type에 무관한 공용 로직인지 확인 (Phase 9/10 재사용 가능성)
- [ ] 기존 임베디드 7z.exe 관련 코드/리소스(`Resources/7-Zip_x86.zip`, `7-Zip_x64.zip` 등)가 완전히 제거됐는가
- [ ] 업데이트 실패와 "이미 최신 상태"가 UI에서 구분되는가

---

## Phase 9: 쉐이더팩 관리

### 목표
서버 배포 쉐이더와 드롭인 쉐이더를 관리하고 실행 전 하나를 선택해 `options.txt`에 반영한다.

### 데이터 모델
```csharp
public class ShaderPackInfo
{
    public string Id { get; set; }
    public string FileName { get; set; }
    public bool IsServerManaged { get; set; }
    public bool IsSelected { get; set; }
}
```

### Codex 작업 지시
- `Core/DropInPackManagerBase.cs` 신규 (Phase 10과 공유하는 추상 베이스): 스캔/추가/삭제 공용 로직
- `Core/ShaderPackManager.cs`: `SetSelected`, `ApplyToOptionsTxt`(라인 단위 파싱 후 대상 키만 치환, 전체 덮어쓰기 금지)
- `ViewModels/ShaderPackViewModel.cs`, `Views/ShaderPackTab.axaml`

**`options.txt` 취급 공통 규칙 (Phase 9·10 공유)**
- 파일이 **아직 없는 경우**(최초 실행 전)를 반드시 처리한다. 없는데 만들면 마인크래프트가 나중에 기본값으로 덮을 수 있으므로, 게임 실행 직전에 적용하거나 부재 시 적용을 건너뛰고 사용자에게 알린다.
- **게임 실행 중에는 쓰지 않는다.** 마인크래프트는 종료 시 `options.txt` 전체를 덮어쓰므로 실행 중 수정은 소실된다. 실행 중이면 UI에서 적용을 막는다.
- 인코딩(UTF-8)과 줄바꿈을 원본과 동일하게 보존하고, 알 수 없는 키는 그대로 통과시킨다.
- 쓰기는 임시 파일 + 교체 방식으로 원자적으로 하고, 최초 수정 전 `.bak`을 남긴다.
- 쉐이더 설정 키는 모드팩(Iris/OptiFine)에 따라 파일이 다르다 — `options.txt`가 아니라 `optionsshaders.txt` / `config/iris.properties`일 수 있다. **대상 파일과 키를 착수 전에 실제 게임으로 확인해서 확정한다.**

### Claude 검증 체크리스트
- [ ] `options.txt` 파싱이 다른 그래픽 설정을 보존하는가 (전/후 diff 확인, **단위 테스트로 고정**)
- [ ] 파일이 없는 최초 실행 환경에서 크래시나 설정 유실이 없는가
- [ ] 게임 실행 중 적용 시도가 차단되는가
- [ ] 대상 파일/키가 실제 게임(Iris/OptiFine 등 실제 사용 조합)으로 확인됐는가
- [ ] 서버 배포/드롭인 쉐이더가 충돌 없이 공존하는가
- [ ] 선택된 쉐이더 파일이 삭제됐을 때 Off로 안전하게 폴백하는가
- [ ] 서버 배포 쉐이더의 Remove 버튼이 비활성화돼 있는가
- [ ] `DropInPackManagerBase`를 실제로 상속해서 재사용했는가

---

## Phase 10: 리소스팩 관리 (드래그앤드롭 정렬 포함)

### 목표
쉐이더팩과 동일한 구조를 리소스팩에 적용하되, 다중 선택 + 드래그앤드롭 순서 조정을 지원한다.

### 데이터 모델
```csharp
public class ResourcePackInfo
{
    public string Id { get; set; }
    public string FileName { get; set; }
    public bool IsServerManaged { get; set; }
    public bool IsEnabled { get; set; }
    public int Order { get; set; }
}
```

### Codex 작업 지시
- `Core/ResourcePackManager.cs` (`DropInPackManagerBase` 상속): `SetEnabled`, `Reorder`, `ApplyToOptionsTxt`(`resourcePacks:["..."]` 라인만 치환)
- `Views/ResourcePackTab.axaml`: Avalonia `ListBox` + `PointerPressed`/`PointerMoved`/`PointerReleased` 기반 드래그 정렬 (`Avalonia.Xaml.Behaviors` 사용 허용), 드롭 위치 인디케이터 필수
- **접근성 폴백 필수**: 드래그와 함께 ▲▼ 버튼도 반드시 구현 (둘 중 하나만 하지 않는다)

### Claude 검증 체크리스트
- [ ] 드래그앤드롭 순서 변경이 `options.txt`에 정확히 반영되는가
- [ ] ▲▼ 버튼으로도 동일하게 순서 변경이 가능한가
- [ ] `resourcePacks` 문자열 포맷이 실제 Minecraft가 생성한 `options.txt`와 정확히 일치하는가
- [ ] 서버 필수 리소스팩을 드래그로 최상단 밖으로 옮길 수 없는가
- [ ] 대용량 리소스팩 추가가 비동기로 처리되는가 (UI 블로킹 없음)
- [ ] `DropInPackManagerBase`를 Phase 9와 공유하는가

---

## Phase 11: 런처 자체 자동 업데이트 (Velopack, OS별 패키징)

### 목표
런처 실행 파일 자체의 자동 업데이트를 크로스플랫폼으로 구현한다.

### Codex 작업 지시
- Velopack 도입, `Program.cs`(Avalonia 진입점)에 업데이트 체크 추가
- **OS별 패키징 산출물 구성**: Windows(Squirrel 인스톨러), macOS(.pkg 또는 zip + 공증 절차 고려), Linux(AppImage 등)
- 아이콘 세트 추가: macOS `.icns`, Linux 여러 크기 `.png` (기존 `.ico`만으로는 부족)
- **코드 서명 (신규 — §1.1 E)**: 서명이 없으면 Windows SmartScreen 경고, macOS Gatekeeper 차단으로 일반 사용자가 실행하지 못한다.
  - Windows: 코드 서명 인증서 확보 여부와 비용을 착수 전에 결정한다(OV/EV). 확보 못 하면 최소한 SmartScreen 경고 우회 방법을 사용자 안내 문서에 넣는다.
  - macOS: Apple Developer 계정($99/년) + 공증이 사실상 필수다. **계정이 없으면 macOS 배포 자체가 성립하지 않으므로, 이건 Phase 11이 아니라 프로젝트 착수 시점에 결정해야 하는 항목이다.**
  - 리포에 커밋됐던 `CustomLauncher_TemporaryKey.pfx`는 폐기하고 재발급한다. 새 키는 VCS에 넣지 않고 CI 시크릿으로 주입한다.
- 업데이트 릴리스는 CI(Phase 0)에서 3 OS 아티팩트를 자동 생성하도록 연결한다.

### Claude 검증 체크리스트
- [ ] 3개 OS 각각에서 구버전 → 신버전 업데이트가 실제로 적용되는가
- [ ] 업데이트 중 게임 실행 상태에서 강제 종료 없이 대기/경고하는가
- [ ] 업데이트 실패 시 기존 버전으로 안전하게 폴백하는가
- [ ] 업데이트 패키지의 서명/해시가 검증되는가 (갱신 채널이 공격면이 되지 않는가)
- [ ] macOS에서 공증(notarization) 관련 경고 없이 실행되는가, Windows에서 SmartScreen 경고 없이 실행되는가
- [ ] 서명 키가 리포지토리가 아니라 CI 시크릿에서 주입되는가

---

## Phase 12: 통합 QA (Windows/macOS/Linux 매트릭스)

### 목표
전체 Phase 통합 후 3개 OS 기준 회귀 테스트.

### Claude 검증 체크리스트
- [ ] 3개 OS 각각에서: 신규 설치 → 로그인 → 모드/쉐이더/리소스팩 전체 다운로드 → 게임 실행까지 끊김 없이 되는가
- [ ] 기존 Windows WinForms 버전 유저의 설정/인증 정보가 신버전(3 OS 공통)으로 마이그레이션될 때 손실이 없는가
- [ ] 저사양/네트워크 불안정 환경에서 각 단계가 적절히 재시도/에러 표시하는가
- [ ] 메모리 누수 여부 (배경음악, Discord Presence, 서버 상태 폴링, 드래그앤드롭 핸들러 해제)
- [ ] OS별로 다른 부분(보안 저장소, Java 탐색, 압축 해제, 오디오, 패키징)이 각각 최소 1회씩 실제 기기/VM에서 검증됐는가 — 시뮬레이터나 문서상의 추정으로 QA를 종료하지 않는다

---

## Phase 13: 서버 운영자용 매니페스트 생성 도구 (크로스플랫폼 CLI)

### 배경
Helios Launcher는 `distribution.json`을 손으로 관리하기 어렵다는 문제로 별도 CLI 도구 **Nebula**를 두고 있다. 우리도 Phase 8부터 매니페스트에 해시가 들어가는데, 원본 `manifest.json` 템플릿에 `"해쉬값(certutil -hashfile 파일.7z SHA256)"`라는 수동 계산 안내가 박혀 있었던 것 자체가 이 문제를 보여준다. Nebula만큼 무거울 필요는 없다 — Forge/Fabric 설치는 클라이언트의 CmlLib이 처리하므로, 이 도구는 해시 계산 + JSON 조립만 하면 된다. **서버 운영자가 어떤 OS를 쓸지 알 수 없으므로 이 도구 자체도 크로스플랫폼 콘솔 앱으로 만든다** (`certutil`처럼 Windows 전용 명령에 의존하지 않는다).

### Codex 작업 지시
- 별도 프로젝트 `CustomLauncher.ManifestTool` (.NET 8 콘솔 앱, 크로스플랫폼)
- `CustomLauncher.Shared` 클래스 라이브러리 분리 — `Models/DistroModule.cs`, `Models/ServerDistribution.cs`를 여기로 옮기고 런처 본체와 이 도구 양쪽이 참조 (스키마 이중 정의 금지)
- `Commands/GenerateCommand.cs`: 폴더 재귀 스캔 후 SHA256 계산(`System.Security.Cryptography`는 OS 무관), `DistroModule` 리스트 생성
- `Commands/DiffCommand.cs`: 기존 `distribution.json`과 비교해 변경분만 콘솔 출력
- 필수/선택/드롭인, 서브모드 부모, 리소스팩 `LoadOrder`는 CLI 인자(`--type`, `--parent`) 또는 대화형 프롬프트로 지정
- 업로드 자동화는 1차 범위 제외 — 로컬 JSON 생성까지만

### Claude 검증 체크리스트
- [ ] 3개 OS 모두에서 도구가 빌드/실행되는가 (Windows 전용 명령어나 경로 구분자 하드코딩이 남아있지 않은가)
- [ ] 생성된 해시값이 각 OS의 표준 방법(Windows `certutil`, macOS/Linux `shasum -a 256`)으로 수동 계산한 값과 일치하는가
- [ ] 기존 `distribution.json`의 수동 설정값(`ParentId`, `LoadOrder` 등)이 재생성 시 스캔 결과와 머지되는가, 통째로 덮어써지지 않는가 (Fail 조건)
- [ ] `CustomLauncher.Shared` 분리로 스키마가 한 곳에서만 정의되는가

---

## 6. 통합 UI 구조

```
[설정 창]
 ├─ 일반 설정
 ├─ Java 관리          (Phase 7)
 ├─ 계정/보안          (Phase 2, 3)
 └─ 콘텐츠 관리
     ├─ 모드             (Phase 8)
     ├─ 쉐이더팩          (Phase 9)
     └─ 리소스팩          (Phase 10, 드래그앤드롭 정렬)
```

---

## 7. 열린 이슈 (착수 전 결정 필요)

### 7.1 프로젝트 착수 전에 결정해야 하는 항목 (Phase 0보다 앞)

아래 3개는 특정 Phase의 세부사항이 아니라 **프로젝트 범위 자체를 정하는 항목**이다. 여기서 "안 된다"가 나오면 12~16주를 투입한 뒤에 알게 되는 것이 최악이므로 지금 결정한다.

- **macOS를 정말 1차 지원 대상에 넣을 것인가?** Apple Developer 계정(연 $99)과 공증 없이는 일반 사용자가 앱을 실행조차 못 한다. 계정을 만들 의사가 없다면 macOS는 "소스 빌드 사용자 한정"으로 격하하고 Windows+Linux 2종으로 범위를 줄이는 게 정직하다 — 그러면 Phase 3·11·12의 부담이 크게 준다.
- **3 OS 실기기/VM 테스트 환경을 확보할 수 있는가?** 본 문서의 검증 체크리스트 대부분이 실기기 테스트를 전제한다. macOS 실기기 또는 CI 러너, Linux VM 접근이 없으면 Phase 2·6·7의 검증이 불가능하다.
- **Windows 코드 서명 인증서를 확보할 것인가?** 없으면 SmartScreen 경고를 감수하는 것으로 명시 결정한다.

### 7.2 각 Phase 착수 전 결정 항목

- Phase 0 크로스플랫폼 스파이크 테스트에서 CmlLib.Core가 macOS/Linux에서 문제없이 동작하는지가 이 프로젝트 전체의 최대 기술 리스크다 — 여기서 심각한 문제가 발견되면 로드맵 전체를 재검토해야 한다
- Phase 1 배경음악 기능을 정식으로 완성할지, 이번 범위에서 제외할지 (§1.1 I의 라이선스·용량 부담을 감안하면 **제외가 기본안**)
- Phase 1 오디오 대체 라이브러리 최종 확정 (라이선스 검토 포함)
- Phase 1 다국어 지원 여부 — 지금 결정하지 않으면 나중에 전 화면을 다시 건드려야 한다
- Phase 2 Azure 앱 등록/클라이언트 ID 확보 방법 (CmlLib 내장 vs 자체 등록)
- Phase 3 **CmlLib 세션 캐시가 이미 안전한지 판정** — 이 판정 결과에 따라 Phase 3의 범위가 "제거 작업"이 될 수도, "3종 구현"이 될 수도 있다 (§1.1 A)
- Phase 3 macOS Keychain 연동 방식 (위 판정에서 필요하다고 나온 경우에만)
- Phase 9 쉐이더 설정의 실제 대상 파일/키 확정 (`options.txt` vs `optionsshaders.txt` vs Iris 설정)
- Phase 10 드래그앤드롭 구현에 `Avalonia.Xaml.Behaviors` 외부 패키지 사용 허용 여부 (본 문서는 허용을 기본안으로 채택) *(2차 문서에서 Phase 8로 잘못 표기돼 있던 것을 수정)*
- Phase 11 Velopack 배포 채널 (GitHub Releases vs 자체 서버)
- Phase 13을 Phase 9~12와 병렬 진행할지 여부

이 항목들은 각 해당 Phase 착수 전에 결정해서 문서에 반영해두는 것을 추천한다.

---

## 8. 문서 개정 이력

| 판본 | 일자 | 주요 변경 |
|---|---|---|
| 1차 | — | 초기 기능 확장 기획 |
| 2차 | 2026-07-21 | 크로스플랫폼 목표 확정 반영, Phase 0·2·3 신설 |
| 3차 | 2026-07-21 | 소스 전수 대조 후 보강 — §1.1 추가 발견 12건, §1.5 기존 버그 회귀 기준선, §2.1 브랜치·롤백 전략, Phase 0 테스트/CI 인프라 신설, Phase 3 전제 재정의(보호 대상이 udata가 아니라 CmlLib 세션 캐시), Phase 8 보안 요구사항(경로 탈출·해시 재검증·드롭인 보존), Phase 11 코드 서명, §7.1 착수 전 범위 결정 항목 |
