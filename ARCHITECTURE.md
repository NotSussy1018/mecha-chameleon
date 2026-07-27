# Mecha Chameleon Architecture

## 1. 기술 기준선

- Unity: 6000.1.1f1
- 렌더러: Built-in Render Pipeline
- 네트워크: Netcode for GameObjects 2.4.3
- 전송: Unity Transport 2.6.0
- 입력: 현재 Legacy `Input` API
- UI: 현재 IMGUI 프로토타입, 목표 uGUI Canvas
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

씬에 플레이어를 미리 두지 않는다. 호스트 또는 클라이언트 연결 뒤 `ChameleonRoundManager`가 등록된 네트워크 프리팹을 생성한다.

## 3. 코드 소유권

### `RoomConnector`

현재 책임:

- localhost `127.0.0.1:7778` 호스트 및 참가
- NGO 시작과 종료
- 연결 상태와 접속 인원
- 기존 UGS Session/Relay 호스트와 join code 참가

목표 책임:

- UI가 요청한 로컬 방 생성, 참가, 나가기를 실행
- 로컬 방 설정을 호스트 연결 승인에 전달
- 호스트 종료를 모든 클라이언트에 알림

Relay 메서드는 현재 로컬 빌드 UI에서 숨긴다. 제거는 별도 승인 대상이다.

### `ChameleonRoundManager`

- 호스트 권한 라운드 상태 머신
- 플레이어 스폰과 연결 해제 추적
- 역할 선택
- Lobby, Hiding Room, Hunter 스폰 이동
- 타이머와 승패
- 연습용 하이더

`GamePhase`의 순서는 `Lobby -> Paint -> Hunt -> Result -> Lobby`다. UI가 임의로 phase 값을 쓰지 않고 호스트 명령을 호출한다.

### `ChameleonPlayer`

- 로컬 입력과 카메라
- 이동, 점프, 벽 오르기와 매달리기
- 자세와 자세별 벽 충돌
- 추락 복귀
- 역할, 생존, 기본 색상 네트워크 상태
- 서버 권한 사격과 명중 ray 피드백
- 로컬 런타임 총 모델

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

### `MvpHud`

현재 `OnGUI` 기반 기능 검증 UI다. 연결, 라운드 디버그 버튼, HUD, 색상 휠이 한 클래스에 있다.

목표:

- uGUI가 실제 화면 흐름과 HUD를 담당한다.
- 색상 휠의 색 계산과 페인트 API는 재사용할 수 있지만 `MvpHud`를 더 큰 상태 관리자처럼 확장하지 않는다.
- uGUI 대체가 끝난 기능부터 IMGUI를 제거한다.

### `ChameleonSceneBuilder`

씬, 재질, 조명, 플레이어 프리팹, 네트워크 프리팹 목록을 생성하는 Editor 도구다.

`Mecha Chameleon/Build MVP Scene`은 `Mvp.unity`를 새로 만들기 때문에 파괴적인 개발 도구로 취급한다. 기존 씬 수정을 보존해야 하는 일반 작업에서는 실행하지 않는다.

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

## 5. 목표 UI 구조

단일 `Canvas` 아래에 전체 화면 panel과 게임 HUD를 둔다.

```text
Canvas
├── HomePanel
├── CreateRoomPanel
├── JoinRoomPanel
├── RoomPanel
├── GameHud
├── OptionsPanel
├── PasswordModal
└── StatusModal
```

권장 최소 코드 분리:

- `GameUiController`: 현재 UI 화면과 이전 화면, panel 전환
- `HomePanel`, `CreateRoomPanel`, `JoinRoomPanel`, `RoomPanel`, `OptionsPanel`: 해당 화면 입력과 표시
- `GameHud`: 타이머, 역할, 결과, 페인트 UI
- `RoomConnector`: 실제 연결
- `LocalRoomDiscovery`: LAN 방 광고와 목록

panel 클래스는 네트워크 로직을 직접 구현하지 않는다. `RoomConnector`와 `ChameleonRoundManager`의 공개 명령을 호출하고 상태를 표시한다.

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

## 6. 로컬 방 모델

필요한 최소 데이터:

```text
LocalRoomConfig
- RoomName
- Password
- Port

DiscoveredRoom
- HostAddress
- Port
- RoomName
- IsLocked
- PlayerCount
- MaxPlayers
- LastSeenAt
```

- `MaxPlayers`는 현재 코드의 8을 표시할 수 있지만 사용자 선택은 미래 범위다.
- 비밀번호 원문을 방 검색 broadcast에 포함하지 않는다.
- 비밀번호를 장기 저장하지 않는다.
- 방 이름과 비밀번호 길이에 작은 상한을 둔다.

## 7. LAN 방 검색

온라인 방 목록 서비스는 사용하지 않는다.

목표 최소 방식:

1. 호스트가 게임 전송 포트 `7778`과 다른 discovery UDP 포트에서 작은 방 정보를 주기적으로 broadcast한다.
2. Join Room 화면이 같은 포트에서 응답을 수신한다.
3. `HostAddress + Port`를 방의 런타임 식별자로 사용한다.
4. 일정 시간 새 광고가 없으면 목록에서 제거한다.
5. 참가 시 Unity Transport의 connection address를 해당 host address로 설정한다.

주의:

- 같은 컴퓨터 테스트에서는 loopback 방도 목록에 나타나야 한다.
- discovery는 편의 기능이지 신뢰 경계가 아니다.
- JSON이나 고정된 작은 패킷처럼 디버깅 가능한 형식을 사용한다.
- 방 검색 때문에 Firebase, Relay, Lobby Service, 데이터베이스를 추가하지 않는다.
- discovery 기능이 실제 구현되기 전에는 수동 `Join Local` 개발 경로를 유지할 수 있다.

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

## 13. 변경 시 테스트 경계

- 방과 UI: Home부터 Room 입장까지 PlayMode 테스트
- Connection Approval: 공개 방, 올바른 비밀번호, 잘못된 비밀번호
- 권한: 클라이언트 Start/End Game 거부
- 라운드: 기존 10개 PlayMode 테스트 유지
- 씬과 프리팹: 기존 4개 EditMode 테스트 유지

구체적인 실행 방법과 수동 검증은 `TEST.md`를 따른다.

