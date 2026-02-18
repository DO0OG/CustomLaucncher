# CustomLauncher

특정 서버에 쉽게 접속하고 항상 최신 상태를 유지할 수 있도록 설계된 Windows Forms 애플리케이션입니다. Microsoft 계정으로 로그인하고, 자동으로 게임 파일과 모드를 업데이트하며, 사전 구성된 서버로 바로 게임을 시작할 수 있습니다.

![image](https://github.com/user-attachments/assets/6bb45976-5dfa-4c73-afc7-f6ee97e5a058)

## 주요 기능

- **Microsoft 계정 로그인**: Xbox Live를 통한 간편하고 안전한 인증.
- **자동 업데이트**: 런처 시작 시 원격 매니페스트를 확인하여 게임 클라이언트, 모드, 설정 파일을 자동으로 다운로드하고 업데이트합니다.
  - SHA256 해시 비교를 통한 파일 무결성 검사.
  - `.7z` 아카이브를 지원하여 대규모 모드 팩도 효율적으로 업데이트.
- **모드로더 자동 설치**: 필요한 버전의 모드로더(Forge, Fabric 등)가 설치되어 있지 않으면 런처가 자동으로 설치합니다.
- **서버 상태 표시**: 런처 메인 화면에서 대상 서버의 온라인/오프라인 상태를 실시간으로 확인할 수 있습니다.
- **게임 설정**:
  - RAM 할당량(최대/최소 메모리) 설정.
  - 게임 해상도 선택.
  - Minecraft 설치 경로 지정.
- **편의 기능**:
  - 커스텀 UI 및 배경 음악.
  - 게임 파일 다운로드 및 설치 진행률 표시.

## 작동 방식

1.  **로그인**: 사용자는 Microsoft 계정으로 로그인합니다. 인증 정보는 로컬에 안전하게 저장됩니다.
2.  **업데이트 확인**: 런처는 원격 서버에 호스팅된 `manifest.json` 파일을 읽어옵니다.
3.  **파일 비교**: 매니페스트에 정의된 파일 목록과 사용자의 로컬 파일을 비교합니다. 각 파일은 SHA256 해시를 통해 변경 여부를 확인합니다.
4.  **다운로드 및 설치**: 새 파일이거나 변경된 파일이 있으면 원격 URL에서 다운로드합니다. 만약 파일이 `.7z` 아카이브 형식이라면, 자동으로 압축을 해제하여 지정된 경로에 설치합니다. 이 과정에서 런처는 내장된 7-Zip을 사용하므로 사용자 PC에 별도의 압축 프로그램이 필요 없습니다.
5.  **게임 실행**: 모든 업데이트가 완료되면, 사용자는 "게임 시작" 버튼을 클릭하여 사전 설정된 서버 정보와 사용자 설정(RAM, 해상도 등)으로 Minecraft를 실행할 수 있습니다.

## 설정

### 클라이언트 설정

런처의 설정 메뉴(톱니바퀴 아이콘)를 통해 다음을 구성할 수 있습니다.

-   **설치 경로**: Minecraft가 설치될 폴더를 지정합니다.
-   **메모리 설정**: 게임에 할당할 RAM 크기를 MB 단위로 설정합니다.
-   **해상도 설정**: 게임 창의 너비와 높이를 설정합니다.

## 사용된 라이브러리 및 라이선스

이 프로젝트는 Minecraft 런칭 기능을 위해 [CmlLib](https://github.com/CmlLib/CmlLib.Core) 라이브러리를 활용합니다.

-   **CmlLib**: MIT License
    ```
    MIT License

    Copyright (c) 2020 CmlLib

    Permission is hereby granted, free of charge, to any person obtaining a copy
    of this software and associated documentation files (the "Software"), to deal
    in the Software without restriction, including without limitation the rights
    to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
    copies of the Software, and to permit persons to whom the Software is
    furnished to do so, subject to the following conditions:

    The above copyright notice and this permission notice shall be included in all
    copies or substantial portions of the Software.

    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
    IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
    FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
    AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
    OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
    SOFTWARE.
    ```
