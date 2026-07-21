# CustomLauncher 2.0 보완 작업지시서 (Remediation Order)

**작성일**: 2026-07-21
**대상 브랜치**: `11-feat-크로스플랫폼-런처-2.0-통합-구현` (HEAD = `343318d`)
**작업 분담**: 구현 = Codex CLI / 검증 = Claude Code
**상위 문서**: `CustomLauncher_v2_Implementation_Plan.md` (이하 "기획서")

---

## 0. 배경 — 왜 이 지시서가 필요한가

기획서 Phase 0~13에 대한 검증 결과, **기반 레이어와 Core 로직은 합격**이다. 특히 아래는 요구 수준을 상회한다.

- `AppPaths`의 Config/Log/GameDir 3분리 + 레거시 마이그레이션
- 3-OS 매트릭스 CI + `dotnet format --verify-no-changes`
- `ModuleManager` / `ModuleValidation`의 보안 처리(경로 탈출, 심볼릭 링크, HTTPS 강제, 다운로드 후 해시 재검증, 압축 폭탄 상한, staging→backup→commit 롤백, managed-state 기반 드롭인 보존)
- 기획서 §1.1 A(보호 대상이 `udata`가 아니라 CmlLib 세션 캐시라는 지적)를 정확히 수용한 Phase 3 처리
- 설정 JSON화(schemaVersion, 원자적 저장, 레거시 3줄 매핑)와 서버 상태 폴링(60초 + 백오프 + 창 비활성 대응)

**문제는 이것들이 애플리케이션에 연결되어 있지 않다는 점이다.**

```
$ grep -rl "ModuleManager|ShaderPackManager|ResourcePackManager|RamCalculator|JavaInstaller|*JavaDiscovery"
  → 자기 자신의 파일과 테스트 외 참조 0건
$ ls CustomLauncher/ViewModels/
  → MainViewModel.cs, ViewModelBase.cs  (기획서가 요구한 4개 ViewModel 부재)
$ grep -rn "Forge" --include=*.cs CustomLauncher/
  → LauncherConfig.cs:7  (상수 선언 1건뿐. 설치 코드 없음)
```

즉 현재 상태는 **"라이브러리 완성, 배선 미착수"** 다. 지금 빌드해서 실행하면:

1. 모드/콘텐츠 업데이트가 **한 번도 실행되지 않는다** (원본 런처에는 있던 기능)
2. **바닐라 1.20.1이 실행된다** — Forge 서버 접속 불가
3. 쉐이더/리소스팩/모드 탭은 빈 껍데기다
4. Java 자동 탐색·설치가 동작하지 않는다

이 지시서는 그 배선과 잔여 결함을 닫는 것이 목적이다. **새 기능 추가가 아니라, 이미 만든 것을 쓰이게 만드는 작업이다.**

---

## 1. 작업 프로토콜

기획서 §0의 프로토콜을 그대로 따른다. 추가로:

1. **R1~R7 단위로만 커밋한다.** 한 커밋에 여러 R을 섞지 않는다.
2. 각 R 완료 시 변경 파일 목록, 추가한 테스트 목록, 수동 확인 시나리오, 알려진 제약을 함께 제출한다.
3. **기존 Core 클래스의 public API를 바꾸지 않는 것을 기본으로 한다.** 이미 검증 통과한 영역이므로, 배선을 위해 시그니처 변경이 꼭 필요하면 이유를 보고서에 명시한다.
4. 새 로직에는 반드시 단위 테스트를 함께 추가한다. UI 배선이라 테스트가 어렵다면 **ViewModel까지는 테스트 가능하게** 설계한다(View에 로직을 넣지 않는다).
5. CI(`dotnet build` + `dotnet test` + `dotnet format`)가 3 OS 모두 초록불이어야 제출한다.

**의존 관계**

```
R1 (실행 파이프라인) ──┬── R3 (Java 통합)
                      ├── R4 (콘텐츠 관리 UI)
R2 (수명주기)  ────────┘
R5 (인증)   … 독립, 병렬 가능
R6 (설정창 바인딩) … R4와 같은 파일을 건드리므로 R4보다 먼저
R7 (마감)   … 전부 끝난 뒤 마지막
```

권장 순서: **R1 → R2 → R6 → R3 → R4 → R5 → R7**

---

## R1. 실행 파이프라인 복원 (최우선 · 치명적)

### 문제

`CustomLauncher/Core/LauncherService.cs`의 `CreateGameProcessAsync`는 다음만 한다.

```csharp
await _launcher.InstallAsync(LauncherConfig.McVersion, cancellationToken);
...
return await _launcher.CreateProcessAsync(LauncherConfig.McVersion, option);
```

- **콘텐츠 업데이트 호출이 없다.** `ModuleManager`가 어디서도 생성되지 않는다. 원본 `AutoUpdater.CheckForUpdatesAsync`가 하던 역할이 통째로 사라졌다.
- **모드 로더 설치가 없다.** `LauncherConfig.ModLoaderType` / `ForgeVersion`은 아무도 읽지 않는 죽은 상수이고, `CmlLib.Core.Installer.Forge` 패키지는 참조만 되고 미사용이다. 원본 `MainForm.btnStartGame_Click`에는 Forge/Fabric 설치 분기가 있었다.

### Codex 작업 지시

**1-1. `Core/GameSessionPreparer.cs`(신규) — 게임 실행 전 준비 단계를 한 곳에 모은다**

`LauncherService`가 비대해지지 않도록 준비 파이프라인을 분리한다. 아래 순서를 고정한다.

```
1. 매니페스트 조회      ModuleManager.FetchDistributionAsync(LauncherConfig.ManifestUrl, ct)
2. 콘텐츠 동기화        ModuleManager.UpdateAsync(distribution, ct)
3. 모드 로더 확인/설치   (아래 1-2)
4. 게임 파일 설치        MinecraftLauncher.InstallAsync(targetVersionName, ct)
5. 프로세스 생성         MinecraftLauncher.CreateProcessAsync(targetVersionName, option)
```

- 각 단계의 진행 상황을 `IProgress<LaunchProgress>`(신규 record: 단계명, 0~1 비율)로 보고한다. 지금처럼 파일 진행률만 뭉뚱그리지 말고 **어느 단계인지 UI에 표시**되게 한다.
- `CancellationToken`을 전 단계에 관통시킨다.
- **`ModuleUpdateResult.Status` 처리를 명시한다**: `Updated`/`UpToDate`는 진행, `Cancelled`는 조용히 중단, **`Failed`는 실행을 중단하고 `Error` 메시지를 그대로 상위에 전달**한다. 기획서 §1.5의 "업데이트 실패와 최신 상태가 UI에서 구분되지 않는다"를 반복하지 않는다.
- 매니페스트 조회 실패(네트워크 단절 등) 시의 정책을 **명시적으로 결정**한다. 권장: 이전에 성공한 동기화 상태(`ManagedModuleStateStore`)가 존재하면 경고를 띄우고 오프라인 실행을 허용, 상태가 없으면(최초 실행) 중단. 어느 쪽이든 조용히 넘어가지 않는다.

**1-2. 모드 로더 설치 복원**

`Core/ModLoaderInstaller.cs`(신규)로 분리한다. `LauncherConfig.ModLoaderType`(`"forge"` / `"fabric"` / `"none"`) 분기를 복원하되, 원본의 문제를 답습하지 않는다.

- 설치된 버전 목록(`GetAllVersionsAsync`)을 확인해 **이미 있으면 재설치하지 않는다**.
- Forge: `ForgeInstaller.Install(mcVersion, forgeVersion, options)`의 **반환값을 실행 버전명으로 사용**한다. 원본처럼 `$"{mc}-forge-{mc}-{forge}"` 문자열을 직접 조립해 추측하지 않는다(버전 명명 규칙이 Forge 버전대마다 다르다).
- Fabric: `FabricInstaller.Install(...)` 반환값 사용.
- `"none"`이면 `LauncherConfig.McVersion`을 그대로 반환.
- 설치 진행률을 1-1의 `IProgress`로 흘린다.
- **`LauncherConfig.FabricVersion` 상수가 현재 삭제돼 있다.** `ModLoaderType`을 유지할 것이므로 `FabricVersion`도 복원한다. 셋 다 안 쓸 거라면 `ModLoaderType`/`ForgeVersion`까지 함께 제거하고 "바닐라 전용 런처"임을 기획서에 반영해야 한다 — **임의로 결정하지 말고, 둘 중 어느 쪽인지 보고서에 근거와 함께 명시한다.**

**1-3. `MainViewModel` 배선**

- `LaunchAsync`가 `GameSessionPreparer`를 거치도록 교체한다.
- 진행 단계명을 `Status`에, 비율을 `Progress`에 표시한다.
- **`ProcessWrapper`를 사용한다.** 현재는 `process.Start()`를 직접 호출해 게임 stdout/stderr가 버려지고 있다. 원본은 `ProcessWrapper.OutputReceived`로 로그를 받았다. 게임 출력은 `DebugLogger`에 `Debug` 레벨로 남긴다(크래시 원인 추적에 필수).

**1-4. `ModuleManager` 인스턴스 수명**

`ModuleManager`는 `gameDirectory`를 생성자에서 고정한다. 설정에서 설치 경로가 바뀌면 인스턴스를 새로 만들어야 한다. `LauncherService.EnsureLauncher`와 동일한 재생성 패턴을 적용한다.

### 완료 조건
- `dotnet test` 통과 + `GameSessionPreparer` 단위 테스트 신규(매니페스트 실패/취소/업데이트 실패 각 경로)
- 모드 로더 분기 단위 테스트(이미 설치됨 → 재설치 안 함, 반환 버전명 사용)

### Claude 검증 체크리스트
- [ ] 게임 실행 시 `ModuleManager.UpdateAsync`가 실제로 호출되는가 (호출 경로를 코드로 추적 가능한가)
- [ ] `ModuleUpdateStatus.Failed`가 실행을 중단시키고 오류 메시지가 UI에 표시되는가
- [ ] 매니페스트 조회 실패 시의 정책이 결정·구현·문서화됐는가 (조용한 통과는 fail)
- [ ] Forge/Fabric 설치가 복원됐고, 버전명을 **문자열 조립이 아니라 installer 반환값**에서 얻는가
- [ ] `FabricVersion` 상수 복원 여부, 또는 "바닐라 전용" 결정이 근거와 함께 보고됐는가
- [ ] 이미 설치된 모드 로더를 재설치하지 않는가
- [ ] 게임 프로세스의 stdout/stderr가 로그로 남는가
- [ ] 설치 경로 변경 시 `ModuleManager`/`MinecraftLauncher`가 새 경로로 재생성되는가

---

## R2. 수명주기 · 리소스 누수 수정 (치명적)

### 문제

**2-A. `CancelCommand`가 앱 전체를 죽인다** — `ViewModels/MainViewModel.cs`

```csharp
CancelCommand = new RelayCommand(() => _lifetime.Cancel(), () => Busy);
```

`_lifetime`은 **애플리케이션 수명 전체**를 담당하는 CTS다(폴링, 설정 저장, 로그인이 전부 이걸 쓴다). 사용자가 다운로드를 한 번 취소하면 이후 로그인·설정 저장·서버 상태 폴링이 **영구히 죽는다.** 앱을 재시작해야 복구된다.

**2-B. 이벤트 핸들러 누수** — `Core/LauncherService.cs`

```csharp
public async Task<Process> CreateGameProcessAsync(...)
{
    EnsureLauncher(settings.InstallPath);
    _launcher!.FileProgressChanged += (_, e) => progress?.Report(...);   // ← 호출마다 구독
```

`_launcher`는 재사용되는데 구독은 매 실행마다 추가된다. 게임을 N번 실행하면 핸들러가 N개 누적되고, 이전 실행의 `progress`(이미 죽은 ViewModel 참조)까지 계속 호출된다. 기획서 Phase 12 "핸들러 해제" 항목 위반이다.

**2-C. 앱 초기화가 `async void`에서 예외를 삼킬 수 있다** — `App.axaml.cs`

```csharp
public override async void OnFrameworkInitializationCompleted()
{
    ...
    await viewModel.InitializeAsync();          // 네트워크/파일 I/O 포함
    desktop.MainWindow = new MainWindow { ... }; // 이게 끝나야 창이 뜬다
```

`InitializeAsync`가 던지면 `async void`라 잡히지 않고 앱이 조용히 죽는다. 또한 초기화(세션 복원 등)가 끝날 때까지 **창이 아예 뜨지 않는다** — 네트워크가 느리면 아무것도 안 뜬 채 대기한다.

### Codex 작업 지시

**2-1. CTS 계층 분리**
- `_lifetime`: 앱 수명 전용. 오직 `DisposeAsync`에서만 취소한다.
- 작업 단위 CTS를 별도로 둔다. `CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token)`로 만들어 작업 시작 시 생성, 완료 시 dispose.
- `CancelCommand`는 **현재 진행 중인 작업 CTS만** 취소한다.
- 취소 후 `Busy=false`로 복귀하고, 이후 로그인·실행이 정상 동작해야 한다.

**2-2. 이벤트 구독 정리**
- `LauncherService`에서 `FileProgressChanged` 구독을 생성자(또는 `EnsureLauncher`)로 옮기고, 현재 활성 `IProgress`를 필드로 두어 갈아끼우는 방식으로 바꾼다. 또는 실행 종료 시 `-=`로 확실히 해제한다.
- **동일 패턴이 다른 곳에 없는지 전수 확인한다** (`+=`가 메서드 본문 안에 있는 케이스 전부).
- `MainViewModel.LaunchAsync`의 `process.Exited += ...`도 프로세스 dispose 시점을 함께 관리한다.

**2-3. 앱 시작 흐름 개선**
- 창을 먼저 띄우고, `InitializeAsync`는 창이 뜬 뒤 백그라운드로 진행하도록 바꾼다(로딩 상태는 `Status`로 표시).
- `InitializeAsync` 내부를 try/catch로 감싸 실패 시 `DebugLogger`에 기록하고 사용자에게 표시한다. 앱이 조용히 죽지 않게 한다.
- `AppDomain.CurrentDomain.UnhandledException` / `TaskScheduler.UnobservedTaskException` 핸들러를 등록해 최후의 예외를 로그로 남긴다.

**2-4. 미사용 매개변수 정리**
- `MainViewModel` 생성자의 `AppPaths paths`는 본문에서 사용되지 않는다. R3/R4에서 실제로 필요해질 예정이므로 **그때 사용하거나, 아니면 제거한다** — 방치하지 않는다.

### Claude 검증 체크리스트
- [ ] 다운로드 취소 → 이후 로그인 → 게임 실행이 정상 동작하는가 (2-A 재현 시나리오로 확인)
- [ ] 서버 상태 폴링이 취소 후에도 계속 도는가
- [ ] 게임을 3회 연속 실행했을 때 `FileProgressChanged` 핸들러가 1개만 유지되는가
- [ ] 메서드 본문 안에서 `+=` 하는 다른 누수 지점이 남아있지 않은가
- [ ] 네트워크를 차단한 상태로 앱을 실행했을 때 창이 뜨고 오류가 표시되는가 (무한 대기/무음 종료는 fail)
- [ ] 미처리 예외가 로그 파일에 남는가

---

## R3. Java 통합 (치명적)

### 문제

`Core/Java/` 아래에 `WindowsJavaDiscovery` / `MacJavaDiscovery` / `LinuxJavaDiscovery` / `JavaValidator` / `JavaInstaller` / `RamCalculator`가 **전부 구현되고 테스트까지 있지만, 앱에서 아무도 호출하지 않는다.**

- OS별 구현체를 선택하는 **팩토리가 없다** (기획서는 `SecretStorageFactory`와 동일한 패턴을 요구했다).
- `Views/JavaSettingsTab.axaml`은 경로 입력 텍스트박스 4개뿐이다. 탐색·검증·설치 버튼이 없다.
- 결과적으로 `settings.Java.ExecutablePath`가 비어 있으면 `MLaunchOption.JavaPath`가 `null`로 넘어간다.

### Codex 작업 지시

**3-1. `Core/Java/JavaDiscoveryFactory.cs`(신규)**
- `PlatformDetector.Current` 기준으로 `IJavaDiscovery` 구현체를 반환한다.
- 지원하지 않는 플랫폼에서는 기획서 §4 원칙대로 **조용한 실패가 아니라 명확한 예외 메시지**를 던진다.

**3-2. `Core/Java/JavaProvisioningService.cs`(신규) — 탐색→검증→설치 오케스트레이션**

```
1. settings.Java.ExecutablePath 가 지정돼 있으면 → JavaValidator로 검증
2. 없거나 검증 실패 → JavaDiscoveryFactory로 시스템 탐색
3. 탐색 결과도 없거나 요구 버전 미달 → 사용자에게 자동 설치 제안
4. 동의 시 JavaInstaller.InstallAsync(featureVersion, progress, ct)
   - 설치 위치는 AppPaths.RuntimeDir (이미 존재하는 프로퍼티)
5. 최종 경로를 settings.Java.ExecutablePath에 저장
```

- 요구 버전은 **`ServerDistribution.Java`(`JavaRequirement.MinVersion` / `RecommendedVersion` / `MinRamMb`)에서 가져온다.** 이 모델은 이미 있는데 아무도 읽지 않는다. R1의 매니페스트 조회 결과를 재사용한다.
- `JavaRequirement.MinRamMb`가 지정돼 있으면 `settings.Java.MaxRamMb`가 그 미만일 때 경고한다.
- **R1의 실행 파이프라인에 이 단계를 끼운다** — 게임 실행 직전 Java 유효성 검사(기획서 Phase 7 "Helios 방식: 실행 전 유효성 검사 → 자동 설치 제안").

**3-3. `ViewModels/JavaSettingsViewModel.cs`(신규) + `Views/JavaSettingsTab.axaml` 확장**
- 현재 경로 표시 + "자동 탐색" 버튼 + "찾아보기"(파일 피커) + "자동 설치" 버튼 + 검증 결과(감지된 버전) 표시
- RAM: `RamCalculator.Calculate(총 물리 메모리)`로 권장값을 구해 **슬라이더의 min/max/기본값에 반영**한다. 현재 `NumericUpDown`의 `Minimum=512 Maximum=65536`은 시스템 메모리와 무관한 하드코딩이다.
  - 총 물리 메모리는 `GC.GetGCMemoryInfo().TotalAvailableMemoryBytes` 또는 OS별 조회로 얻되, **OS 종속 코드가 필요하면 인터페이스로 감싼다**(테스트 가능하게).
- **`-Xmx`/`-Xms` 직접 입력 경고**: `JavaConfig.ContainsHeapOverride`가 이미 구현돼 있으나 호출부가 없다. JVM 인자 편집 시 이 검사를 걸고 경고를 표시한다.
- 설치 진행률을 `JavaInstallProgress`로 받아 표시한다.

### Claude 검증 체크리스트
- [ ] Java가 없는 환경에서 자동 탐색 → 설치 제안 → 설치 → 실행까지 이어지는가
- [ ] `JavaDiscoveryFactory`가 OS별 구현체를 반환하고, 미지원 플랫폼에서 명확한 메시지를 내는가
- [ ] `ServerDistribution.Java`(요구 버전/최소 RAM)가 실제로 읽히는가
- [ ] RAM 슬라이더 범위가 `RamCalculator` 결과에 연동되는가 (하드코딩 512~65536이면 fail)
- [ ] `-Xmx`를 커스텀 인자에 입력하면 경고가 뜨는가 (`ContainsHeapOverride` 호출부 존재)
- [ ] macOS/Linux에서 실행 파일명이 `java`, Windows가 `javaw.exe`로 올바른가
- [ ] Adoptium API 장애 시 앱이 멈추지 않고 메시지를 내는가

---

## R4. 콘텐츠 관리 UI (치명적)

### 문제

`Views/ModManagementTab.axaml`, `ShaderPackTab.axaml`, `ResourcePackTab.axaml`은 각각 **한 줄짜리 목업**이다.

```xml
<ListBox/>                          <!-- ItemsSource 없음 -->
<Button Content="추가"/>             <!-- Command 없음 -->
<Button Content="▲"/><Button Content="▼"/>   <!-- 핸들러 없음 -->
```

바인딩 0개, Command 0개다. 뒤에 있는 `ShaderPackManager` / `ResourcePackManager` / `ModuleManager`는 완성돼 있는데 UI가 연결되지 않았다.

### Codex 작업 지시

**공통**: `SettingsWindow`의 `DataContext`가 현재 `LauncherSettings`(POCO)다. 탭마다 독립 ViewModel이 필요하므로 **R6에서 도입하는 `SettingsViewModel`의 하위 프로퍼티로 각 탭 VM을 노출**하고, 탭에서 `DataContext="{Binding ModManagement}"` 식으로 잡는다.

**4-1. `ViewModels/ModManagementViewModel.cs`**
- 매니페스트의 모듈 목록을 `RequiredMod` / `OptionalMod` / `DropInMod`로 그룹화해 표시
- Optional 모드 체크박스 토글, 서브모드(`ParentId`) 부모-자식 트리 표시 — **부모를 끄면 자식도 함께 꺼진다**
- 드롭인 모드 추가(파일 피커) / 삭제 / 활성화 토글
- "업데이트 확인" → `ModuleManager.UpdateAsync` 호출, 진행률·결과 표시, **취소 버튼 동작**(R2의 작업 단위 CTS 사용)
- `IsServerManaged` 항목은 삭제 버튼 비활성화

> 참고: Optional 모드의 선택 상태를 저장할 곳이 현재 없다. `LauncherSettings`에 `DisabledOptionalModuleIds` 같은 필드를 추가하고 `ModuleManager` 호출 시 반영하는 방식을 권장한다. **`ServerDistribution`(서버 소유)에 사용자 상태를 쓰지 않는다.**

**4-2. `ViewModels/ShaderPackViewModel.cs`**
- `ShaderPackManager.GetPacksAsync()` → 목록 바인딩
- 단일 선택 → `SetSelected(fileName)` → "적용" → `ApplyAsync()`
- **`OptionsEditStatus` 반환값을 UI에 반영한다**: `Applied`(성공), `MissingFile`(options 파일 없음 — 게임을 한 번도 실행하지 않음), `GameRunning`(실행 중이라 적용 불가). 세 경우를 구분해 안내한다. 지금은 이 enum이 어디서도 소비되지 않는다.
- 서버 관리 쉐이더는 삭제 버튼 비활성화
- 선택된 파일이 사라지면 자동으로 OFF 폴백 (매니저에 이미 구현됨 — UI가 그 결과를 반영하는지 확인)

**4-3. `ViewModels/ResourcePackViewModel.cs`**
- `ResourcePackManager.RefreshAsync()` → `Packs` 바인딩
- 다중 활성화 토글(`SetEnabled`), 순서 변경(`Reorder`)
- **▲▼ 버튼과 드래그앤드롭을 둘 다 구현한다** (기획서 Phase 10 "접근성 폴백 필수" — 둘 중 하나만 하지 않는다). 현재는 ▲▼ 버튼 모양만 있고 핸들러가 없다.
- 드래그앤드롭: Avalonia `ListBox` + `Avalonia.Xaml.Behaviors`(기획서 §7.2에서 사용 허용) 또는 `PointerPressed`/`Moved`/`Released`. **드롭 위치 인디케이터 필수.**
- `Reorder`/`SetEnabled`가 던지는 `InvalidOperationException`(서버 필수 팩을 최상단 밖으로 이동, 비활성화 시도)을 **UI에서 잡아 안내 메시지로 바꾼다.** 예외가 그대로 올라와 앱이 죽으면 안 된다.
- 적용 결과의 `OptionsEditStatus` 처리는 4-2와 동일

**4-4. 쉐이더 대상 파일 확정 (기획서 §7.2 미결 항목)**
- `ShaderPackManager`는 `settingsFile` / `settingsKey`를 생성자로 받도록 이미 설계돼 있다(Iris/OptiFine 차이 대응). **어떤 값을 넣을지 결정해야 한다.**
- `docs/QA_MATRIX.md`에 "릴리스 프로파일이 실제 모드팩에 맞춰 선택해야 한다"고만 적혀 있고 실제 값이 없다. `LauncherConfig`에 `ShaderSettingsFileName` / `ShaderSettingsKey` 상수를 추가하고 기본값을 명시한다(기획서 §5.1 컴파일 타임 설정).

### Claude 검증 체크리스트
- [ ] 세 탭 모두 실제 데이터가 뜨고 버튼이 동작하는가 (빈 `ListBox`면 fail)
- [ ] 모드: 부모 모드를 끄면 자식도 꺼지는가, 서버 관리 모드는 삭제 불가인가
- [ ] 모드: 업데이트 진행률·취소가 동작하는가, Optional 선택 상태가 재시작 후 유지되는가
- [ ] Optional 상태가 `ServerDistribution`이 아닌 사용자 설정에 저장되는가
- [ ] 쉐이더/리소스팩: `OptionsEditStatus` 3종(Applied/MissingFile/GameRunning)이 각각 다른 안내로 표시되는가
- [ ] 리소스팩: **▲▼ 버튼과 드래그앤드롭이 둘 다** 동작하는가, 드롭 인디케이터가 있는가
- [ ] 리소스팩: 서버 필수 팩을 최상단 밖으로 끌면 앱이 죽지 않고 안내가 뜨는가
- [ ] 순서 변경이 `options.txt`의 `resourcePacks` 라인에 정확히 반영되고 다른 설정이 보존되는가
- [ ] 쉐이더 대상 파일/키가 `LauncherConfig`에 확정돼 있는가

---

## R5. 인증 플로우 실체 확인 (높음)

### 문제

`Core/AuthService.cs`는 여전히 기획서가 문제로 지목한 그 호출을 쓴다.

```csharp
private readonly JELoginHandler _handler = JELoginHandlerBuilder.BuildDefault();
```

`Microsoft.Web.WebView2.*` 패키지가 제거되어 net8.0에서는 WinForms 팝업을 타지 않을 것으로 **추정**되지만, **무엇을 타는지 확인된 근거가 없다.** 기획서 Phase 2가 요구한 다음 항목이 전부 미제출이다.

- Azure 앱 등록 / 클라이언트 ID 요구사항 사전 조사
- 디바이스 코드 플로우 폴백 (헤드리스·원격 SSH·일부 리눅스 데스크톱 대응)
- 루프백 포트 동적 할당 및 취소 시 리스너 해제
- 3 OS 실계정 로그인 결과

`docs/DEPENDENCY_AUDIT.md`도 "live-account testing still required"로 미완을 인정하고 있다.

### Codex 작업 지시

**5-1. 실제 플로우 조사 보고서 제출 (코드 작성 전)**
- net8.0 런타임에서 `JELoginHandlerBuilder.BuildDefault()`가 최종적으로 사용하는 OAuth 방식이 무엇인지 확인한다(시스템 브라우저 + 루프백인지, 다른 방식인지).
- 루프백 포트가 고정인지 동적인지, 리다이렉트 URI가 무엇인지, 그것이 Azure 앱 등록에 사전 등록돼 있어야 하는지.
- `docs/AUTH_FLOW.md`로 문서화한다. **이 조사 없이 5-2를 진행하지 않는다.**

**5-2. 디바이스 코드 폴백 구현**
- `IAuthService`에 폴백 경로를 추가하고, 브라우저 실행 실패 시 자동 전환한다.
- 사용자에게 코드와 인증 URL을 표시하는 UI를 만든다(복사 버튼 포함).
- 라이브러리가 디바이스 코드를 지원하지 않으면, **지원하지 않는다는 사실과 그 영향(해당 환경에서 로그인 불가)을 문서에 명시**한다. 임의로 자체 구현하지 말고 먼저 보고한다.

**5-3. 취소/실패 처리**
- 로그인 취소 시 루프백 리스너가 확실히 해제되는지 확인한다(포트 점유 잔존 금지).
- 로그인 타임아웃을 둔다(무한 대기 금지).
- 실패 사유(취소/네트워크/계정 문제)를 구분해 메시지를 낸다. 현재는 전부 "로그인 중 오류가 발생했습니다."로 뭉뚱그린다.

### Claude 검증 체크리스트
- [ ] `docs/AUTH_FLOW.md`가 제출됐고, 실제 사용 플로우가 추정이 아니라 확인된 사실로 기술됐는가
- [ ] 브라우저를 열 수 없는 환경에서 폴백이 동작하는가 (미지원이면 그 사실이 명시됐는가)
- [ ] 로그인 취소 후 포트가 해제되고 재시도가 가능한가
- [ ] 실패 사유가 구분되어 표시되는가
- [ ] 3 OS 실계정 로그인 결과가 제출됐는가 (미수행이면 그 사실을 명시 — 추정 통과 금지)

---

## R6. 설정 창 바인딩 수정 (높음 · R4보다 먼저)

### 문제

**6-A. `LauncherSettings`가 `INotifyPropertyChanged`를 구현하지 않는데 양방향 바인딩 대상이다.**

`Views/SettingsWindow.axaml.cs`:
```csharp
public SettingsWindow(LauncherSettings settings) : this() => DataContext = settings;

private async void BrowseFolderClicked(...)
{
    ...
    settings.InstallPath = folders[0].Path.LocalPath;   // ← UI가 갱신되지 않는다
}
```

`Models/LauncherSettings.cs`는 순수 POCO다. **"찾아보기"로 폴더를 골라도 TextBox 내용이 바뀌지 않는다.** 사용자에게는 버튼이 고장 난 것으로 보인다.

**6-B. View가 코드비하인드에서 모델을 직접 조작한다.** 기획서가 요구한 MVVM 구조가 아니다. R4에서 탭마다 ViewModel이 붙으면 이 구조로는 감당이 안 된다.

**6-C. `Resolution`이 `record`(불변)인데 `NumericUpDown`이 `{Binding Resolution.Width}`로 양방향 바인딩돼 있다.** `record`의 init-only 속성이 아니라 positional record라 setter가 없다 — 값 변경이 반영되지 않는다.

### Codex 작업 지시

**6-1. `ViewModels/SettingsViewModel.cs`(신규)**
- `LauncherSettings`를 감싸는 ViewModel을 만든다. `ViewModelBase`(INPC 구현 완료)를 상속한다.
- `InstallPath`, `ResolutionWidth`, `ResolutionHeight`, `DiscordRpcEnabled` 등을 알림 가능한 프로퍼티로 노출한다.
- 하위 탭 VM(`Java`, `ModManagement`, `ShaderPack`, `ResourcePack`)을 프로퍼티로 노출한다 → R4가 여기에 붙는다.
- 폴더 선택을 `IStorageProvider` 기반으로 하되, **ViewModel이 View에 직접 의존하지 않도록** 인터페이스(`IFolderPicker` 등)로 감싸거나 View에서 결과만 VM에 전달한다.
- "저장" 버튼이 실제로 저장을 수행하게 한다. 현재 `SaveClicked`는 `Close()`만 하고, 저장은 `MainWindow.ShowSettings`가 창이 닫힌 뒤 무조건 수행한다 → **"취소"로 닫아도 저장된다.** 저장/취소를 구분한다.

**6-2. 해상도 모델 수정**
- `Resolution` record를 가변 프로퍼티로 바꾸거나, VM에서 `Width`/`Height`를 개별 `int` 프로퍼티로 다루고 저장 시 새 record를 만든다. 후자를 권장한다(모델 불변성 유지).

**6-3. 검증**
- 설치 경로가 쓰기 가능한지, 존재하지 않으면 생성 가능한지 확인하고 안내한다.
- Min RAM > Max RAM 입력을 UI에서 막는다(`AppSettingsManager.Normalize`가 뒤에서 고쳐주지만, 사용자에게는 값이 조용히 바뀌는 것으로 보인다).

### Claude 검증 체크리스트
- [ ] "찾아보기"로 폴더를 선택하면 TextBox가 즉시 갱신되는가
- [ ] 해상도 `NumericUpDown` 변경이 저장 후 재시작까지 유지되는가
- [ ] "저장"과 "취소"가 구분되는가 (취소로 닫았을 때 변경이 반영되지 않는가)
- [ ] ViewModel이 `INotifyPropertyChanged`를 통해 UI에 반영되는가
- [ ] View 코드비하인드에 비즈니스 로직이 남아있지 않은가
- [ ] Min > Max RAM 입력이 UI 단계에서 막히는가

---

## R7. 마감 정리 (중간)

### Codex 작업 지시

**7-1. 라이선스 목록 동기화 (기획서 §5.3)**

`Views/AboutWindow.axaml`의 목록이 실제 의존성과 불일치한다. 현재 4개(Avalonia / CmlLib.Core / SharpCompress / xUnit)만 있고 아래가 누락됐다.

- `DiscordRichPresence` (MIT)
- `Velopack` (MIT)
- `Avalonia.Fonts.Inter`
- `CmlLib.Core.Auth.Microsoft`, `CmlLib.Core.Installer.Forge`
- `HtmlAgilityPack` (전이 의존성 — 포함 여부 판단 후 명시)
- **`DNFBitBitv2` 폰트의 라이선스** (임베디드 배포 중인데 목록에 없다. 재배포 허용 여부를 확인해서 명시할 것 — 확인 안 되면 그 사실을 보고한다)

또한 xUnit은 테스트 전용이라 **배포물에 포함되지 않는다** — 런타임 라이선스 목록에 넣는 것이 맞는지 판단한다.

`docs/DEPENDENCY_AUDIT.md`와 이 목록이 **항상 일치하도록** 하는 방법을 함께 제안한다(예: 목록을 리소스 JSON으로 빼고 감사 문서와 같은 소스에서 생성).

**7-2. 하드코딩 제거 (기획서 §5 단일화 원칙 위반)**
- `Views/MainWindow.axaml`의 `Text="play.example.com"` → `LauncherConfig.ServerIp` 바인딩
- `Views/MainWindow.axaml`의 `Text="CUSTOM LAUNCHER"`, `Title="CustomLauncher 2.0"` → `LauncherConfig`에 런처 표시명 상수 추가 후 참조
- UI 문자열이 `Assets/Strings/ko.json`에 모여 있는데 **실제로 사용되지 않는다.** axaml에 한국어가 직접 박혀 있다. 기획서 Phase 1의 "문자열을 한 곳에 모은다"가 절반만 됐다 — 실제로 사용하거나, 사용하지 않을 거면 파일을 제거하고 그 결정을 문서화한다.

**7-3. Discord 설정**
- `LauncherConfig.DiscordClientId = ""`, `EnableDiscordRpc = false` 상태라 Phase 6 검증이 불가능하다. 테스트용 Client ID를 채우거나, **비워둘 거면 "배포 시 운영자가 채워야 하는 값"임을 주석과 README에 명시**한다.
- Linux Flatpak/Snap 소켓 경로 탐색(기획서 Phase 6)이 라이브러리에서 처리되는지 확인하고, 안 되면 후보 경로 순회를 추가한다.

**7-4. 저장소 정리**
- `CustomLauncher/.claude/settings.local.json`이 커밋돼 있다 → 제거하고 `.gitignore`에 추가
- `CustomLauncher/packages/` 레거시 NuGet 폴더가 디스크에 잔존한다(untracked). 정리한다.
- `.omx/` 임시 디렉토리도 `.gitignore` 확인

**7-5. `docs/QA_MATRIX.md` 갱신**
- R1~R6에서 새로 배선된 기능의 수동 검증 항목을 추가한다.
- "자동 검증됨"으로 적힌 항목 중 실제로는 Core 단위 테스트만 있고 통합 경로가 검증되지 않은 것들을 정직하게 재분류한다.

### Claude 검증 체크리스트
- [ ] 라이선스 목록이 `docs/DEPENDENCY_AUDIT.md`와 일치하는가, 폰트 라이선스가 확인·명시됐는가
- [ ] `play.example.com` 등 설정값 중복 정의가 제거됐는가
- [ ] `ko.json`이 실제로 사용되는가, 아니면 제거 결정이 문서화됐는가
- [ ] Discord Client ID 정책이 명시됐는가
- [ ] 커밋된 로컬 설정 파일이 제거됐는가
- [ ] QA 매트릭스가 실제 검증 상태를 정직하게 반영하는가 (과장된 "자동 검증됨"이 없는가)

---

## 2. 전체 완료 판정 기준

아래를 **전부** 만족해야 기획서 Phase 12(통합 QA) 착수가 가능하다.

- [ ] 3 OS CI 초록불 (build + test + format)
- [ ] 신규 ViewModel 4종에 대한 단위 테스트 존재
- [ ] **실제 실행 검증**: 깨끗한 환경에서 실행 → 로그인 → 콘텐츠 동기화 → Forge 설치 → 게임 실행 → 종료까지 끊김 없이 진행
- [ ] 위 플로우 중 각 단계가 UI에 구분되어 표시되고, 취소가 동작하며, 취소 후 재시도가 가능
- [ ] 쉐이더/리소스팩 변경이 `options.txt`에 반영되고 다른 설정이 보존됨
- [ ] R1~R7의 모든 체크리스트가 항목별 pass 판정

### 검증 시 Claude가 특히 확인할 것

이번 라운드에서 **"Core는 훌륭한데 아무도 호출하지 않는다"** 는 문제가 반복됐다. 따라서 다음 리뷰에서는 각 기능에 대해 아래를 먼저 확인한다.

```
1. 이 클래스를 호출하는 곳이 테스트 밖에 존재하는가?
2. 그 호출은 사용자가 실제로 밟는 경로에 있는가?
3. 반환값(특히 실패/상태 enum)이 소비되는가, 아니면 버려지는가?
```

3번을 특히 본다. 현재 `OptionsEditStatus`, `ModuleUpdateStatus`, `JavaInstallResult`, `RamRecommendation`, `JavaRequirement`가 모두 **정의만 되고 소비되지 않는 상태**다. 값을 만들어놓고 쓰지 않는 것은 기능이 없는 것과 같다.
