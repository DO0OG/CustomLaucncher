# 배포 매니페스트 스키마

영문 버전: [DISTRIBUTION_SCHEMA.md](DISTRIBUTION_SCHEMA.md)

`distribution.json`은 런처가 각 플레이어의 게임 폴더에 설치할 파일 목록입니다. 매니페스트
도구(`CustomLauncher.ManifestTool`)가 생성하고, 런처가 실행할 때마다 읽습니다.

## 모듈 규칙

- 모든 모듈은 기본적으로 단일 파일입니다. 압축 해제가 필요한 모듈만 `packaging`을 아카이브
  값으로 지정하며, 파일명으로 추측하지 않습니다.
- 런처는 설치 기록(`.launcher-managed-modules.json`)에 아카이브 원본 SHA-256과 추출된 각 파일의 SHA-256을 저장합니다. 이 기록이 없는 기존 설치는 업데이트 후 첫 실행 때 아카이브 모듈을 한 번 다시 내려받습니다(매니페스트 형식 변화 없음).
- 모든 다운로드 URL은 HTTPS여야 합니다. 모든 설치 경로는 게임 루트 기준 상대경로입니다.
- 무언가를 쓰기 전에 업데이터가 다음을 거부합니다: 절대 경로, `..` 경로 탈출, 링크 항목,
  잘못된 SHA-256, 중복 id/경로, 의존성 순환, 아카이브 크기/항목 수 초과.
- 런처의 관리 상태(managed-state) 인덱스에 기록된 경로만 교체·삭제할 수 있습니다. 런처가
  소유하지 않은 기존 파일은 업데이트 대상이 아니라 충돌로 처리되므로, 서버 매니페스트가
  침해되더라도 플레이어가 직접 넣은 드롭인 콘텐츠는 보존됩니다.

## 서버 목록 (여러 서버)

매니페스트의 최상위 `servers` 배열에 서버를 여러 개 담을 수 있습니다. 플레이어는 런처에서
서버를 골라 접속합니다. 각 항목의 필드는 다음과 같습니다.

| 필드 | 필수 | 설명 |
|---|---|---|
| `id` | 예 | 영문 소문자·숫자 kebab-case(`^[a-z0-9]+(-[a-z0-9]+)*$`)이며, 매니페스트 안에서 중복될 수 없습니다. 플레이어가 고른 서버와 서버별 설정의 키입니다. |
| `name` | 예 | 런처 화면과 서버 목록에 표시되는 이름입니다. |
| `address` | 예 | 서버 주소입니다. |
| `port` | 아니요 | 1~65535이며, 생략하면 25565입니다. |
| `minecraftVersion` | 예 | 마인크래프트 버전입니다(예: `1.20.1`). |
| `modLoader` | 예 | `type`과 `version`을 담은 객체입니다. `type`은 `forge`·`fabric`·`neoforge`·`quilt`·`vanilla`(소문자)입니다. `vanilla` 외에는 `version`이 필수입니다. 런처는 `forge`·`fabric`·`neoforge`·`quilt`를 아직 설치되어 있지 않을 때 설치합니다. `neoforge`의 `version`은 NeoForge 버전(예: Minecraft `1.21.1`과 함께 쓰는 `21.1.100`)이고, `quilt`의 `version`은 Quilt 로더 버전(예: `0.29.1`)입니다. |
| `java` | 예 | `minVersion`, `recommendedVersion`, 선택 항목 `minRamMb`입니다. 단일 서버 형식의 최상위 `java`와 같은 구조입니다. |
| `modules` | 예 | 위 모듈 규칙을 따르는 모듈 목록입니다. |
| `discord` | 아니요 | 선택 항목 `details`·`state`·`largeImageKey`입니다. `details`를 지정하면 Discord 상태에서 서버 이름 대신 이 값이 표시됩니다. `state`는 예약된 필드로, 현재 런처는 사용하지 않습니다. |
| `statusUrl` | 아니요 | 서버 상태 확인에 쓰는 절대 `https` URL입니다. 지정하면 런처가 이 상태 API를 먼저 조회하고, 성공하면 그 결과를 씁니다(응답 시간은 표시하지 않습니다). 생략하거나 조회에 실패하면 런처가 서버 `address:port`로 Minecraft Server List Ping(SLP)을 직접 보내 접속자 수, MOTD, 버전 이름, 응답 시간(ms)을 읽습니다. 방화벽이 서버 포트로의 직접 연결을 막는 환경이면 `statusUrl`을 지정하세요. |

서버마다 게임 폴더가 따로 있습니다. 서버 `id`가 `default`인 서버(옛 단일 서버 형식 포함)는 설정의 게임 폴더를 그대로 쓰고, 그 밖의 서버는 `{게임 폴더}/instances/{서버 id}`를 게임 폴더로 씁니다. 그래서 mods·셰이더팩·리소스팩·options·servers.dat·logs·crash-reports·saves가 서버마다 따로 저장됩니다. Minecraft 공유 파일(libraries·versions·assets), 매니페스트 캐시, Java 런타임은 공유합니다. 오프라인 실행 가능 여부는 서버 폴더마다 따로 판정합니다. 플레이어가 다른 서버로 바꿔 실행하면 서버 관리 모듈이 새로 선택한 서버 기준으로 다시 동기화됩니다. 서버 선택 목록은 서버가 둘 이상일 때만 보입니다.

```json
{
  "serverId": "",
  "servers": [
    {
      "id": "main",
      "name": "DOG'S SERVER",
      "address": "play.example.com",
      "minecraftVersion": "1.20.1",
      "modLoader": { "type": "fabric", "version": "0.15.11" },
      "java": { "minVersion": "17", "recommendedVersion": "17", "minRamMb": 4096 },
      "modules": [
        {
          "id": "mods/a",
          "path": "mods/a.jar",
          "url": "https://example.com/pack/mods/a.jar",
          "hash": "9b2f4c1e7a0d35f86b1c9e2a4d7f0b3c6e8a1d5f2b9c4e7a0d3f6b1c8e5a2d4f",
          "type": "RequiredMod",
          "parentId": null,
          "loadOrder": null
        }
      ],
      "discord": { "details": "Playing DOG'S SERVER" }
    },
    {
      "id": "creative",
      "name": "DOG'S CREATIVE",
      "address": "creative.example.com",
      "port": 25566,
      "minecraftVersion": "1.20.1",
      "modLoader": { "type": "forge", "version": "47.3.0" },
      "java": { "minVersion": "17", "recommendedVersion": "17" },
      "modules": [
        {
          "id": "mods/b",
          "path": "mods/b.jar",
          "url": "https://example.com/pack/mods/b.jar",
          "hash": "c41e8a0f5d2b7c93e6a1f4d08b5c2e7a9f3d6b1e4c8a0d2f7b5e9c3a6d1f4b80",
          "type": "OptionalMod",
          "parentId": null,
          "loadOrder": null
        }
      ],
      "statusUrl": "https://status.example.com/creative"
    }
  ]
}
```

## 파일 호스팅

각 모듈을 **정식 인증서로 HTTPS 제공**하기만 하면 어떤 호스트든 됩니다. 런처는 리디렉션을
따라가므로, 서명된 임시 URL로 넘기는 호스트도 문제없습니다. 자체 서명 인증서는 거부됩니다.

**정적 웹 루트** — 파일이 경로 접두사 아래에 있는 경우:

    ManifestTool.exe generate ./pack --base-url https://files.example.com/pack

**Seafile 공유 폴더** — 배포 폴더를 한 번 공유하고(파일 링크 `/f/<토큰>/`이 아니라 폴더 링크
`/d/<토큰>/`) 그 아래 모든 파일을 통해 접근합니다. 경로가 쿼리 파라미터로 들어가므로 템플릿
형식을 씁니다:

    ManifestTool.exe generate ./pack \
      --url-template "https://seafile.example.com/d/<토큰>/files/?p=/{path}&dl=1"

토큰 하나가 전체 트리를 커버하며, 파일별 링크가 필요 없습니다. 실제 Seafile 인스턴스에서
확인함: 링크가 `/seafhttp/files/<uuid>/<name>`으로 302 리디렉션되어 파일을 반환하고, 생성기가
각 세그먼트를 퍼센트 인코딩하므로 한글 경로도 정상 동작합니다.

`{path}`는 스캔한 폴더 기준 상대경로입니다. 공유한 폴더와 스캔한 폴더의 층이 다르면 그 차이를
템플릿에 넣습니다(예: `?p=/pack/{path}&dl=1`). 확인하려면 생성된 URL 하나를 브라우저 주소창에
붙여넣습니다 — 오류 페이지가 아니라 다운로드가 되어야 맞습니다.

`distribution.json` 자체도 파일 하나이므로, 파일별 공유 링크로 게시하는 게 가장 간단합니다.
그 URL을 `LauncherConfig.ManifestUrl`에 넣습니다.

두 URL 옵션 모두 맞지 않으면 둘 다 생략합니다. 그러면 생성기가 각 모듈에 이미 기록된 URL을
유지하고 해시만 갱신하므로, 손으로 넣은 링크가 재생성 후에도 남습니다. URL은 모듈별로
저장되므로 서로 다른 호스트를 섞어 쓸 수도 있습니다.

## 호환성

- `servers` 배열이 없거나 비어 있는 매니페스트는 단일 서버 형식입니다. 런처는 런처에 빌드된 값
  (`LauncherConfig`의 서버 이름·주소·포트·Minecraft 버전·로더)과 최상위 `modules`·`java`로 서버
  하나를 만들며, id는 `default`가 됩니다.
- `servers`와 최상위 `modules`를 함께 쓰면 거부합니다.
- 잘못된 서버 항목 하나(빈 값이나 형식이 틀린 id, 중복 id, 알 수 없는 로더 type, 필수 필드 누락,
  `http` statusUrl)가 있어도 매니페스트 전체를 거부합니다. 캐시에 남은 옛 매니페스트도 같은 규칙으로
  읽힙니다.
- 새 형식에서는 최상위 `serverId`를 빈 문자열로 둡니다(매니페스트 도구가 그렇게 저장합니다).
  `servers`를 모르는 옛 런처는 이 매니페스트를 거부하므로, 빈 모듈 목록으로 모드를 지우지 않습니다.
  운영자는 새 버전 런처를 먼저 배포한 뒤 매니페스트를 새 형식으로 바꿔야 합니다.
- 플레이어 설정: 선택한 서버 id(`selectedServerId`)와 서버별로 끈 선택 모듈
  (`disabledOptionalModuleIdsByServer`)을 저장합니다. 옛 설정의 끈 모듈 목록은 첫 실행 때
  `default` 서버로 옮겨집니다. 저장된 id가 목록에 없으면 첫 서버가 선택됩니다.
- 명령줄: `generate --existing`은 `servers`가 있는 매니페스트를 거부합니다(도구 창에서 편집).
  `diff`는 `servers`가 있으면 변경 항목 앞에 `[id]`를 붙입니다.

## 매니페스트 도구

인자 없이 `ManifestTool.exe`를 실행하면 창이 열립니다(폴더 선택, URL 템플릿, 이전 매니페스트
대비 변경 내역, 입력값 기억). 인자를 주면 위의 명령줄로 동작하며, 자동화용으로 계속 사용할 수
있습니다. 두 경로 모두 같은 생성 코드를 호출합니다.

운영자용 단계별 안내: [OPERATOR_GUIDE.ko.md](OPERATOR_GUIDE.ko.md).
