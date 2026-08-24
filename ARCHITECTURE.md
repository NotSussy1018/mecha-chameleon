# Mecha Chameleon Architecture

## 1. 기술 기준선

- Unity: 6000.1.1f1
- 렌더러: Built-in Render Pipeline
- 네트워크: Netcode for GameObjects 2.4.3
- 전송: Unity Transport 2.6.0
- 온라인 세션: Multiplayer Services SDK 1.1.4 + Relay + Authentication 3.5.1
- 입력: 현재 Legacy `Input` API
- UI: uGUI 디자인 Canvas와 비활성화된 기존 IMGUI 프로토타입
- 테스트: Unity Test Framework 1.6.0
- 로컬 멀티클라이언트: Multiplayer Play Mode 1.5.0
- 실행 씬: `Assets/Scenes/Mvp.unity`

패키지 버전은 문제가 해결되거나 필요한 기능이 명확하지 않으면 올리지 않는다.

## 2. 현재 런타임 구성

```text
Mvp Scene
├── App
│   ├── RoomConnector
│   └── MvpHud
├── NetworkManager
│   └── UnityTransport
├── RoundManager (NetworkObject)
│   └── ChameleonRoundManager
├── Lobby geometry and spawns
├── Hiding Room geometry and spawns
└── Overview Camera

Network-spawned ChameleonPlayer
├── NetworkObject
├── CharacterController
├── ClientNetworkTransform
├── ChameleonPlayer
├── ChameleonPaint
├── Visual Root
│   ├── Head
│   ├── Body
│   └── Gun
└── Player Camera
```

### 모듈러 모놀리스 경계

프로젝트는 하나의 Unity 애플리케이션과 `Mvp` 씬을 유지하되, 독립적으로 늘어나는 콘텐츠만 작은 feature assembly로 분리한다.

```text
MechaChameleon (application)
├── MechaChameleon.Rooms
│   └── RoomModule
└── MechaChameleon.Poses
    ├── PoseId
    ├── PoseDefinition
    └── PoseCatalog
```

- application assembly는 NGO, 라운드, 플레이어, UI와 연결 흐름을 조정한다.
- Rooms module은 한 맵의 content root, 로비/하이더/헌터 spawn과 Hunter Platform만 소유한다.
- Poses module은 안정적인 byte network ID, 입력 키, visual transform과 이동 collision volume만 소유한다.
- feature module은 `RoomConnector`, `ChameleonRoundManager`, `ChameleonPlayer` 또는 UI를 참조하지 않는다.
- 새 범용 DI, service locator, event bus 또는 module loader는 추가하지 않는다. Unity serialized reference와 asmdef가 현재 module 경계다.

씬에 플레이어를 미리 두지 않는다. 호스트 또는 클라이언트 연결 뒤 `ChameleonRoundManager`가 등록된 네트워크 프리팹을 생성한다.

## 3. 코드 소유권

### `RoomConnector`

현재 책임:

- Development의 localhost/LAN `7778` 호스트 및 참가
- Staging/Production의 MPS Session 생성/검색/참가와 Relay player-host 연결
- 방 이름, 선택적 비밀번호, 최대 인원을 메모리에 유지
- Development에서는 NGO Connection Approval, 온라인에서는 MPS Session 비밀번호와 잠금으로 참가를 검증
- 연결 성공, 실패, 퇴장 상태를 UI event로 전달
- NGO 시작과 종료
- 연결 상태와 접속 인원

`DeploymentEnvironmentSettings`는 EditorPrefs 또는 빌드 define으로 환경을 선택한다. Development는 온라인 서비스를 초기화하지 않는다. Staging/Production은 `UgsBootstrap`이 해당 UGS environment를 초기화하고 username/password 세션을 요구한다. Release 빌드는 환경 define이 없으면 Production으로 간주한다.

### `UgsBootstrap`

- Unity Services 초기화, username/password 가입과 로그인, cached session 복구, 로그아웃을 소유한다.
- 온라인 room API는 `EnsureReadyAsync`를 거쳐 인증되지 않은 호출을 거부한다.
- Editor profile은 프로젝트 경로의 stable hash를 사용해 재시작 후 세션을 복구하고 Multiplayer Play Mode clone과 계정 저장소를 분리한다.
- Unity Authentication SDK가 저장하는 세션 토큰만 사용한다. 게임 코드는 비밀번호를 저장하거나 로그에 기록하지 않는다.
- Development 경로에서는 Unity Services와 Authentication을 초기화하지 않는다.

### 빌드와 배포 경계

- `DeploymentEnvironmentMenu`: Editor Play Mode에서 사용할 환경만 선택한다.
- `DeploymentBuild`: 빌드 사전 조건, 환경 define, macOS BuildPlayer 호출, manifest 생성을 소유한다.
- `scripts/unity-build.sh`: 사람이든 에이전트든 같은 `DeploymentBuild` 진입점을 호출한다.
- 빌드는 EditorPrefs를 읽지 않고 `MECHA_DEVELOPMENT`, `MECHA_STAGING`, `MECHA_PRODUCTION` 중 하나를 `extraScriptingDefines`로 주입한다.
- 환경별 별도 씬, 프리팹, NetworkManager를 만들지 않는다. 동일 코드와 콘텐츠에 환경 define만 다르게 적용한다.
- 업로드, 코드 서명, notarization은 배포 대상이 정해질 때 추가한다. 현재 자동화는 검증된 로컬 산출물 생성까지 담당한다.

상세 절차와 승격 규칙은 `DEPLOYMENT.md`를 따른다.

### `LocalRoomDiscovery`

- 호스트가 UDP `47779`로 0.75초마다 작은 JSON 방 정보를 broadcast한다. 재전송은 창 포커스나 Unity frame update가 멈춰도 유지되는 경량 timer를 사용한다.
- 같은 컴퓨터 검색을 위해 loopback에도 같은 정보를 보낸다.
- 다른 플레이어 창에 포커스가 있어도 광고와 NGO가 계속 갱신되도록 background 실행을 활성화한다.
- Home부터 `ReuseAddress`로 discovery port를 수신하고 방 목록을 미리 캐시한다.
- `RoomId`로 broadcast와 loopback 중복을 합치고 3초간 새 정보가 없으면 방을 제거한다.
- 방 이름, 잠금 여부, 인원, 최대 인원만 광고하며 비밀번호 원문은 포함하지 않는다.

### `ChameleonRoundManager`

- 호스트 권한 라운드 상태 머신
- 플레이어 스폰과 연결 해제 추적
- 역할 선택
- Lobby, Hiding Room, Hunter 스폰 이동
- 타이머와 승패
- 연습용 하이더
- 서버가 선택한 `RoomModule`과 active room index

`GamePhase`의 순서는 `Lobby -> Paint -> Hunt -> Result -> Lobby`다. UI가 임의로 phase 값을 쓰지 않고 호스트 명령을 호출한다.

### `ChameleonPlayer`

- 로컬 입력과 카메라
- 이동, 점프, 벽 오르기와 매달리기
- 자세와 자세별 벽 충돌
- 추락 복귀
- 역할, 생존, 기본 색상 네트워크 상태
- 서버 권한 사격과 명중 ray 피드백
- 로컬 런타임 총 모델
- `PoseCatalog`의 입력, visual transform과 collision 적용

### `RoomModule`

- stable `RoomId`와 표시 이름
- 선택적으로 켜고 끄는 room content root
- Lobby/Hider/Hunter spawn
- Hunter Choice Platform transform과 판정 크기

새 방은 `GameObject > Mecha Chameleon > Room Module`로 기본 hierarchy를 만든 뒤 `Content` 아래에 맵 geometry를 배치한다. 이 메뉴는 생성한 module을 현재 `ChameleonRoundManager.roomModules`에 자동 등록한다. `RoomId`는 빌드 사이에서 바꾸지 않으며, 같은 scene 안의 모든 room에서 유일해야 한다. 현재 UI에는 map selection을 노출하지 않는다.

### `PoseCatalog`

`Assets/Content/Poses/DefaultPoseCatalog.asset`이 현재 자세의 단일 데이터 원본이다. 새 자세는 catalog에 다음 값을 추가한다.

- 다른 자세와 겹치지 않는 byte network ID
- 표시 이름과 입력 키
- Visual Root position과 Euler rotation
- 벽 접근을 제한해야 할 때만 collision center와 half extents

catalog 순서는 UI 표시 순서일 뿐 network identity가 아니다. 기존 ID의 의미를 바꾸거나 재사용하지 않는다. `ChameleonPlayer`에 pose별 switch를 추가하지 않는다.

### `ChameleonPaint`

- Head/Body 런타임 페인트 텍스처
- 로컬 브러시 예측
- 압축된 `PaintStroke`의 서버 승인 및 `NetworkList` 복제
- 브러시 외곽선과 페인트 카메라 회전
- 색상, 브러시 크기, 라운드별 초기화

현재 네트워크 제한:

- 텍스처 크기 128
- 라운드당 최대 450 strokes
- 클라이언트 전송 최대 약 15 strokes/second
- 서버 승인 최대 약 20 strokes/second
- UV와 RGB를 byte 단위로 전송

### `GameUiController`

현재 책임:

- Login, Home, Create Room, Join Room, Room, Options, Password, Game HUD, Result 화면 전환
- 온라인 시작 시 cached session 복구, 가입/로그인 입력과 로그아웃을 `UgsBootstrap`에 연결
- Create 입력을 실제 로컬 host 시작에 연결
- 발견된 방 행, 잠금 modal, Room 이름과 접속 플레이어 행을 실제 상태에 연결
- host 전용 Start와 End Game, 공통 Leave Room 실행
- Home 배경과 3D Room 배경 전환
- Room 안과 밖에서 서로 다른 Options 항목 표시
- phase와 서버 시간 기반 HUD 타이머, 로컬 역할, 결과 표시
- 하이더 paint panel, 색상 휠, brush size, clear command 연결
- Unity Editor에서 `F1`부터 `F8`까지 화면별 디자인 미리보기

Graphics와 volume control의 실제 설정 적용, paint brightness와 hunter shot feedback은 다음 기능 단계다.

### `MvpHud`

기존 `OnGUI` 기반 기능 검증 UI다. 연결, 라운드 디버그 버튼, HUD, 색상 휠이 한 클래스에 있다. 새 UI 디자인과 겹치지 않도록 현재 Mvp 씬에서는 component를 유지한 채 비활성화했다.

목표:

- `GameUiController`가 만든 uGUI에 실제 화면 흐름과 HUD 상태를 연결한다.
- 색상 휠의 색 계산과 페인트 API는 재사용할 수 있지만 `MvpHud`를 더 큰 상태 관리자처럼 확장하지 않는다.
- uGUI의 기능 연결이 끝난 뒤 IMGUI component 제거를 별도로 승인받는다.

### `ChameleonSceneBuilder`

씬, 재질, 조명, 플레이어 프리팹, 네트워크 프리팹 목록을 생성하는 Editor 도구다.

`Mecha Chameleon/Build MVP Scene`은 `Mvp.unity`를 새로 만들기 때문에 파괴적인 개발 도구로 취급한다. 기존 씬 수정을 보존해야 하는 일반 작업에서는 실행하지 않는다.

### `UiDesignBuilder`

- `Mecha Chameleon/Build UI Design` 메뉴로 현재 Mvp 씬의 `Game UI Canvas`만 재생성한다.
- Login, Home, Create Room, Join Room, Room, Options, Password, Game HUD, Result를 만든다.
- 생성 배경과 투명 나무 UI sprite의 importer 설정을 적용한다.
- 메뉴 화면에는 `Assets/UI/Fonts/LilitaOne-Regular.ttf`, 게임 HUD와 Result에는 built-in runtime font를 할당한다.
- 전체 Mvp 씬과 플레이어 프리팹은 재생성하지 않는다.

## 4. 네트워크 권한

호스트가 서버이자 방장이다.

| 상태/행동 | 입력 주체 | 최종 권한 |
| --- | --- | --- |
| 이동과 카메라 | 소유 클라이언트 | 소유 클라이언트, transform 복제 |
| 라운드 phase와 타이머 | 방장 UI | 서버 |
| 역할 선택 | 플랫폼 위치 | 서버 |
| 자세 | 소유 클라이언트 요청 | 서버 NetworkVariable |
| 페인트 stroke | 소유 클라이언트 요청 | 서버 검증 후 NetworkList |
| 사격 | 헌터 클라이언트 요청 | 서버 raycast |
| 생존과 승패 | 서버 명중 및 타이머 | 서버 |
| 방 종료 | 방장 | 호스트 서버 |

클라이언트 RPC 또는 입력값에는 다음을 검증한다.

- 요청자가 해당 NetworkObject의 owner인지
- 현재 phase에서 허용된 행동인지
- 역할과 생존 상태가 맞는지
- 입력 범위와 전송 빈도가 허용되는지

## 5. 현재 UI 디자인 구조

단일 `Canvas` 아래에 전체 화면 panel과 게임 HUD를 둔다.

```text
Canvas
├── LoginPanel
├── HomePanel
├── CreateRoomPanel
├── JoinRoomPanel
├── RoomPanel
├── GameHud
├── OptionsPanel
├── PasswordModal
└── ResultOverlay
```

현재 코드 분리:

- `GameUiController`: 화면, modal, 실제 room data 표시와 명령
- `UiDesignBuilder`: Canvas와 화면별 계층 생성
- `UgsBootstrap`: 온라인 계정 세션과 Unity Services 초기화
- `RoomConnector`: 실제 연결
- `LocalRoomDiscovery`: LAN 방 광고와 목록

기능 연결 시 panel 또는 binding 코드는 네트워크 로직을 직접 구현하지 않는다. `RoomConnector`와 `ChameleonRoundManager`의 공개 명령을 호출하고 상태를 표시한다. 화면이 충분히 작으면 panel마다 별도 MonoBehaviour를 만들지 않고 `GameUiController` 안에서 유지한다.

별도의 범용 UI 프레임워크, 라우터 패키지, 의존성 주입 컨테이너는 만들지 않는다.

### UI 상태

```csharp
public enum UiScreen
{
    Home,
    CreateRoom,
    JoinRoom,
    Room
}
```

Options와 Password는 현재 화면 위의 modal 상태로 둔다. enum이나 상태 클래스는 실제 구현할 때 한 곳에만 정의한다.

## 6. 방 모델

필요한 최소 데이터:

```text
RoomListing
- RoomId
- IsOnline
- RoomName
- HostAddress
- Port
- IsLocked (password required)
- IsJoinLocked (match in progress)
- PlayerCount
- MaxPlayers
- LastSeenAt
```

- `MaxPlayers`는 현재 코드의 8을 표시할 수 있지만 사용자 선택은 미래 범위다.
- 비밀번호 원문을 방 검색 broadcast에 포함하지 않는다.
- 비밀번호를 장기 저장하지 않는다.
- 방 이름과 비밀번호 길이에 작은 상한을 둔다.

## 7. 환경별 방 검색

Development:

1. 호스트가 게임 전송 포트 `7778`과 다른 discovery UDP `47779`에서 작은 방 정보를 주기적으로 broadcast한다.
2. Join Room 화면이 같은 포트에서 응답을 수신한다.
3. `HostAddress + Port`를 방의 런타임 식별자로 사용한다.
4. 일정 시간 새 광고가 없으면 목록에서 제거한다.
5. 참가 시 Unity Transport의 connection address와 port를 해당 방 광고 값으로 설정한다.

주의:

- 같은 컴퓨터 테스트에서는 loopback 방도 목록에 나타나야 한다.
- discovery는 편의 기능이지 신뢰 경계가 아니다.
- JSON이나 고정된 작은 패킷처럼 디버깅 가능한 형식을 사용한다.
- 방 검색 때문에 Firebase나 데이터베이스를 추가하지 않는다.

Staging/Production:

1. 공개 MPS Session을 최대 20개 조회한다.
2. 빈 슬롯이 있고 참가 잠금이 해제됐으며 `protocol=1`인 세션만 표시한다.
3. 선택한 Session ID와 MPS 비밀번호로 참가하고 MPS의 NGO Relay network handler가 호스트/클라이언트를 시작한다.
4. 라운드 시작 시 Session을 잠그고 Lobby 복귀 시 잠금을 해제한다.
5. NGO host migration은 현재 지원하지 않는다. player host가 나가면 Session과 라운드를 종료한다.

Unity Cloud Project가 연결되지 않았으면 `UgsBootstrap`은 UGS 호출 전에 사용자에게 연결 위치를 포함한 오류를 반환한다. Development 동작에는 영향을 주지 않는다.

## 8. 비밀번호 연결 승인

비밀번호 방은 NGO Connection Approval을 사용한다.

```text
Client -> connection payload(room password)
Host -> compare with current room config
Host -> approve or reject with a user-facing reason
```

- 호스트의 메모리에만 현재 비밀번호를 유지한다.
- 승인 전에는 플레이어 NetworkObject를 생성하지 않는다.
- 잘못된 비밀번호, 방이 가득 참, 이미 시작된 방을 구분해 UI에 표시한다.
- 로컬 파티 기능이므로 암호학적 보안을 약속하지 않는다.

## 9. 화면과 네트워크 상태 전환

```text
Launch
  -> Home, NetworkManager stopped

Create success
  -> StartHost
  -> local player spawned
  -> Room panel + 3D Lobby

Join success
  -> StartClient
  -> approval
  -> local player spawned
  -> Room panel + 3D Lobby

Start
  -> Room panel hidden
  -> GameHud active

Result complete
  -> Lobby
  -> Room panel active

Leave / host shutdown / disconnect
  -> NetworkManager.Shutdown
  -> transient state cleared
  -> Home
```

연결 중에는 중복 Create/Join을 막는다. 실패나 취소 후 Unity Transport를 `Shutdown`하여 다음 시도가 같은 포트를 정상적으로 사용할 수 있게 한다.

## 10. 씬과 프리팹

- 한 씬을 유지한다.
- Home 상태에서는 Overview Camera를 사용하고 플레이어 입력을 비활성화한다.
- Room 진입 후 로컬 Player Camera가 활성화된다.
- 맵 지오메트리와 네트워크 매니저는 씬에 남아 있다.
- 각 선택 가능한 맵은 하나의 `RoomModule`로 spawn과 platform contract를 제공한다.
- `ActiveRoomIndex`는 서버만 변경하며 Lobby phase에서만 다른 room을 선택할 수 있다.
- 네트워크 플레이어는 `Assets/Prefabs/ChameleonPlayer.prefab`만 사용한다.
- 네트워크 프리팹 변경 시 `Assets/NetworkPrefabs.asset` 등록과 Editor 테스트를 확인한다.

## 11. 성능 기준

- 저사양 품질에서는 realtime shadow와 pixel light 수를 제한한다.
- 실내 조명은 baked 또는 mixed를 우선하고 Light Probe를 동적 플레이어에 사용한다.
- 가구마다 realtime light를 추가하지 않는다.
- 플레이어 페인트는 GPU RenderTexture 시스템을 추가하지 않고 현재 128 x 128 CPU texture 방식을 유지한다.
- physics query는 `NonAlloc` API와 캐시된 배열을 유지한다.
- UI 목록은 현재 최대 8명과 소수의 방을 대상으로 단순하게 구현한다. 가상화나 복잡한 pooling은 필요 없다.

## 12. 오류 처리

- 사용자에게 무반응 대신 짧고 구체적인 상태를 표시한다.
- 내부 예외 전체를 UI에 노출하지 않는다.
- 개발 로그에는 `[RoomConnector]`, `[LocalRoomDiscovery]`처럼 소유 컴포넌트를 포함한다.
- 연결 실패 후 호스트나 클라이언트가 반쯤 실행된 상태로 남지 않게 정리한다.
- 호스트 연결이 끊기면 클라이언트는 Room에 머물지 않고 Home으로 돌아간다.

### 개발 진단 로그

- Editor와 Development Build는 `Application.persistentDataPath/MechaChameleonDiagnostics`에 프로세스별 `latest-<instance>-<pid>.log`를 기록한다.
- 각 Play session 시작 시 해당 프로세스의 latest 파일을 새로 만들고 모든 줄에 UTC, level, process, instance, session, category, event를 포함한다.
- `network`, `discovery`, `ui`, `round`의 상태 전환만 직접 기록하며 이동, 페인트 stroke 같은 고빈도 payload는 기록하지 않는다.
- Unity warning, error, assertion, exception은 같은 파일에 자동 수집한다.
- 비밀번호 원문과 페인트 payload는 기록하지 않는다.
- `Mecha Chameleon/Open Diagnostics Folder`와 `Print Diagnostics Path` 메뉴로 파일 위치를 바로 확인한다.

## 13. 변경 시 테스트 경계

- 방과 UI: Home부터 Room 입장까지 PlayMode 테스트
- Connection Approval: 공개 방, 올바른 비밀번호, 잘못된 비밀번호
- 권한: 클라이언트 Start/End Game 거부
- 라운드: 기존 10개 PlayMode 테스트 유지
- 씬, 프리팹, diagnostics: 기존 EditMode 테스트 유지

구체적인 실행 방법과 수동 검증은 `TEST.md`를 따른다.
