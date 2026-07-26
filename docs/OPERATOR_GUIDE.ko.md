# 서버 운영자 가이드

런처를 새 서버용으로 만들고, 모드·리소스팩을 배포하는 방법입니다.
영문 참고 문서: [AUTH_FLOW.md](AUTH_FLOW.md), [DISTRIBUTION_SCHEMA.md](DISTRIBUTION_SCHEMA.md)

---

## 0. 전체 그림

```
[운영자]  배포 폴더 준비  →  Seafile 업로드  →  매니페스트 툴 실행  →  json 업로드
                                                                          │
[플레이어] 런처 실행  →  로그인  →  매니페스트 확인  →  변경분만 다운로드  →  게임 시작
```

런처는 실행할 때마다 `distribution.json`을 읽어서 **바뀐 파일만** 내려받습니다.
운영자가 할 일은 **파일을 올리고 매니페스트를 다시 만드는 것** 뿐입니다.

---

## 1. 가장 중요한 규칙

> **툴에 지정한 로컬 폴더의 내용 = 플레이어 게임 폴더의 내용 = 공유한 Seafile 폴더의 내용**

이 셋이 1:1로 같아야 합니다. 이것만 지키면 나머지는 자동입니다.

```
로컬              D:\TEST\mods\a.jar
Seafile           TEST/mods/a.jar          ← TEST 폴더를 공유
플레이어 게임 폴더   .custom\mods\a.jar
```

배포 폴더는 **게임 폴더처럼** 구성합니다. `mods`, `config`, `resourcepacks` 같은
폴더가 배포 폴더 **바로 아래** 오면 됩니다. 중간에 다른 층(`pack` 등)을 두어도
되지만, 그러면 URL 형식에 그 층을 적어줘야 합니다(→ 4-2).

---

## 2. 최초 1회 설정

### 2-1. 배포 폴더 만들기

플레이어에게 내려줄 파일을 게임 폴더 구조로 모읍니다.

```
D:\TEST\
├── mods\
│   ├── a.jar
│   └── b.jar
├── config\
│   └── sub\deep.toml
└── resourcepacks\
    └── x.zip
```

### 2-2. Seafile에 올리고 폴더를 공유

1. Seafile에 `TEST` 폴더 전체를 업로드
2. **`TEST` 폴더**를 우클릭 → 공유 → 링크 생성
3. `https://dogs.kro.kr/d/토큰/` 형태의 링크를 받습니다

> **파일이 아니라 폴더를 공유해야 합니다.**
> 폴더 공유는 `/d/토큰/`, 파일 공유는 `/f/토큰/` 입니다.
> 폴더를 공유하면 **토큰 하나로 그 안의 모든 파일**이 처리됩니다.
> 파일마다 링크를 딸 필요가 없습니다.

> **배포용 폴더만 공유하세요.** 상위 폴더를 공유하면 링크를 아는 사람이
> 그 안의 다른 자료까지 전부 접근할 수 있습니다.

### 2-3. `LauncherConfig.cs` 수정

`CustomLauncher/LauncherConfig.cs` 한 파일만 고치면 새 서버용 런처가 됩니다.

| 항목 | 설명 | 예시 |
|---|---|---|
| `ServerName` | 런처 화면에 표시될 서버 이름 | `"DOG'S SERVER"` |
| `ServerIp` | 서버 주소 | `"play.example.com"` |
| `ServerPort` | 서버 포트 | `25565` |
| `ManifestUrl` | `distribution.json` 다운로드 주소 (→ 3-3) | |
| `McVersion` | 마인크래프트 버전 | `"1.20.1"` |
| `ModLoaderType` | `"forge"` / `"fabric"` / `"none"` | `"forge"` |
| `ForgeVersion` | Forge 버전 | `"47.3.0"` |
| `MicrosoftClientId` | Azure 앱 ID (→ [AUTH_FLOW.md](AUTH_FLOW.md)) | |

**서버를 여러 개 운영한다면 아래 두 개는 반드시 서버마다 다르게** 하세요.
같은 값을 쓰면 두 런처가 같은 설정 파일을 공유해 설치 경로·메모리·모듈 선택이
서로 덮어써집니다.

| 항목 | 설명 | 예시 |
|---|---|---|
| `LauncherId` | 설정·로그·인증 캐시 폴더 이름 | `"TestServer"` |
| `GameFolderName` | 기본 게임 설치 폴더 이름 | `".testserver"` |

> `MicrosoftClientId`는 **반대로 서버마다 같아도 됩니다.** 오히려 하나로 쓰는 게
> 낫습니다 — 마인크래프트 API 승인이 클라이언트 ID 단위라, 새로 만들면 승인을
> 다시 받아야 합니다.

---

## 3. 배포할 때마다 하는 일

### 3-1. 파일 준비

배포 폴더(`D:\TEST\`)의 모드·설정을 원하는 대로 수정합니다.
그리고 **Seafile에 올립니다.** (Seafile 클라이언트로 동기화하면 자동)

### 3-2. 매니페스트 툴 실행

`ManifestTool.exe`를 **더블클릭**합니다. 창이 뜹니다.

| 입력란 | 넣을 값 |
|---|---|
| **배포 폴더** | `D:\TEST` — `폴더 선택` 버튼으로 고릅니다 |
| **다운로드 URL 형식** | `https://dogs.kro.kr/d/토큰/files/?p=/{path}&dl=1` |
| **서버 ID** | `TEST` (비워두면 폴더 이름 자동 사용) |
| **저장 위치** | `D:\TEST\distribution.json` (폴더 고르면 자동 입력) |
| **기존 설정 유지** | 켜둡니다 (필수/선택 구분, 로드 순서 보존) |

`매니페스트 생성` 버튼을 누르면 결과가 표시됩니다.

```
모듈 12개를 스캔했습니다.
변경 3건:
  + mods/newmod.jar
  ~ mods/existing.jar
  - mods/removed.jar
저장했습니다: D:\TEST\distribution.json
```

**변경 내역을 꼭 확인하세요.** 실수로 파일이 빠졌는지 올리기 전에 알 수 있습니다.

> 입력한 값은 자동으로 저장됩니다. 두 번째 배포부터는 **창 열고 버튼 한 번**입니다.

### 3-3. `distribution.json` 업로드

생성된 `distribution.json`을 Seafile에 올립니다.
배포 폴더 안에 저장했다면 폴더째 동기화할 때 같이 올라갑니다.
(툴이 이 파일은 스캔 대상에서 자동으로 제외하므로 배포 폴더 안에 둬도 됩니다.)

**최초 1회만**, 이 파일의 주소를 `LauncherConfig.ManifestUrl`에 넣습니다.

```
https://dogs.kro.kr/d/토큰/files/?p=/distribution.json&dl=1
```

이후로는 파일 내용만 바뀌므로 주소를 다시 건드릴 일이 없습니다.

---

## 4. URL 형식 이해하기

### 4-1. `{path}` 가 하는 일

`{path}` 자리에 **배포 폴더 기준 상대경로**가 들어갑니다.

```
로컬 D:\TEST\mods\a.jar        →  {path} = mods/a.jar
생성 URL  .../files/?p=/mods/a.jar&dl=1
```

한글 파일명도 자동으로 처리됩니다(퍼센트 인코딩). 실제 서버에서 확인했습니다.

### 4-2. 중간 층이 있을 때

**공유한 폴더**와 **배포 폴더**가 다르면 그 사이 경로를 URL에 적어야 합니다.

| 공유한 폴더 | 툴의 배포 폴더 | URL 형식 |
|---|---|---|
| `TEST` | `D:\TEST` | `?p=/{path}&dl=1` |
| `TEST` | `D:\TEST\pack` | `?p=/pack/{path}&dl=1` |
| `TEST/pack` | `D:\TEST\pack` | `?p=/{path}&dl=1` |

헷갈리지 않으려면 **배포 폴더를 그대로 공유**하는 게 가장 단순합니다.

### 4-3. 맞게 설정했는지 확인하는 법

생성된 `distribution.json`을 열어 URL 하나를 복사해 **브라우저 주소창에 붙여넣습니다.**

- 파일이 다운로드되면 ✅ 설정이 맞습니다
- 404나 Seafile 오류 페이지가 뜨면 ❌ 경로가 틀렸습니다 (→ 4-2)

하나만 확인하면 나머지는 자동으로 맞습니다.

---

## 5. 모듈 종류 지정하기

기본은 전부 **필수 모드**입니다. 선택형으로 만들거나 리소스팩 순서를 정하려면
`distribution.json`을 직접 편집합니다.

```json
{
  "id": "mods/optional-minimap",
  "path": "mods/minimap.jar",
  "type": "OptionalMod",
  "parentId": null,
  "loadOrder": null
}
```

| `type` | 뜻 |
|---|---|
| `RequiredMod` | 필수. 항상 설치됨 |
| `OptionalMod` | 선택. 플레이어가 설정에서 끌 수 있음 |
| `DropInMod` | 드롭인 |
| `ShaderPack` | 셰이더팩 |
| `ResourcePack` | 리소스팩 (`loadOrder`로 순서 지정) |

`parentId`를 다른 모듈 id로 지정하면 **하위 모듈**이 됩니다.
상위를 끄면 하위도 함께 꺼집니다.

**손으로 고친 값은 다음 생성 때 보존됩니다.** (`기존 설정 유지` 체크 시)
해시만 갱신되고 `type`·`parentId`·`loadOrder`는 그대로 남습니다.

---

## 6. 자주 겪는 문제

| 증상 | 원인 | 해결 |
|---|---|---|
| 런처가 "매니페스트에 연결할 수 없습니다" | `ManifestUrl`이 틀림 | 브라우저에서 그 주소를 열어 json이 받아지는지 확인 |
| 특정 파일만 다운로드 실패 | URL 경로 접두사 문제 | → 4-2, 4-3 |
| "다운로드한 파일이 손상되었습니다" | 업로드 후 매니페스트를 다시 안 만듦 | 파일 올린 뒤 툴을 다시 실행 |
| 플레이어가 넣은 모드가 사라짐 | 없음 — 런처가 소유하지 않은 파일은 건드리지 않습니다 | |
| `ManifestTool.exe`가 바로 닫힘 | 예전 CLI 버전 | 현재 버전은 더블클릭하면 창이 뜹니다 |
| HTTPS 오류 | 자체 서명 인증서 | 정식 인증서 필요 (Let's Encrypt 등) |

---

## 7. 툴 빌드 방법

```bash
dotnet publish CustomLauncher.ManifestTool -c Release -r win-x64 ^
  --self-contained false -p:PublishSingleFile=true -o .\dist\manifesttool
```

`dist\manifesttool\ManifestTool.exe` 가 생성됩니다.

> 인자를 주고 실행하면 명령줄로도 동작합니다. 나중에 자동화할 때 쓸 수 있습니다.
> `ManifestTool.exe --help`

---

## 8. 안전장치 (참고)

런처는 다음을 자동으로 보장합니다. 운영자가 신경 쓰지 않아도 됩니다.

- 모든 다운로드 주소는 **HTTPS만** 허용
- 다운로드 후 **SHA-256 재검증** — 일치하지 않으면 적용하지 않음
- 매니페스트가 게임 폴더 **바깥을 가리키면 거부** (경로 탈출 방어)
- 압축 파일의 심볼릭 링크·과도한 크기 거부
- 적용은 **임시 폴더 → 백업 → 교체** 순서라 중간에 실패해도 되돌아감
- **런처가 소유하지 않은 파일은 절대 삭제·덮어쓰기 하지 않음**
  → 플레이어가 직접 넣은 모드는 업데이트해도 유지됩니다
- 게임 실행 시 `LauncherConfig`의 `ServerName`·`ServerIp`로 **멀티플레이 목록에 서버를 자동
  등록**합니다. 접속을 끊고 메뉴로 나와도 목록에 남아 다시 들어갈 수 있습니다. 플레이어가
  직접 추가한 다른 서버는 병합되어 보존됩니다.
- 네트워크가 끊기면 마지막으로 검증된 상태로 실행 (최초 실행 제외)
