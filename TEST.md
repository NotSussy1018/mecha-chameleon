# Mecha Chameleon Test Guide

## 1. 테스트 목표

이 프로젝트의 핵심 위험은 단일 플레이어 동작보다 호스트와 클라이언트 사이의 상태 불일치다. 기능 변경은 로컬 호스트, 두 번째 플레이어, 라운드 전환을 함께 검증한다.

## 2. 현재 자동 테스트

### EditMode

파일: `Assets/Tests/Editor/MvpSceneTests.cs`

현재 16개:

1. Mvp 씬에 NetworkManager, RoomConnector, RoundManager, HUD와 네트워크 프리팹 등록이 있는지
2. 씬에 미리 배치된 ChameleonPlayer가 없는지
3. 플레이어 프리팹의 Head/Body UV paint collider가 올바른지
4. PaintStroke UV와 RGB 압축이 허용 오차 안인지
5. 전체 UI, 생성 sprite와 font, 4개 빈 room slot, 8개 빈 player slot, discovery component가 씬에 있는지
6. 플레이어 prefab이 유일한 ID와 입력 키를 가진 기본 Pose Catalog를 사용하는지
7. Mvp scene의 기본 house room이 완전한 `RoomModule` contract로 등록됐는지
8. Development/Staging/Production 문자열과 온라인 서비스 사용 여부
9. MPS 비밀번호 최소 길이 경계
10. 방 비밀번호 잠금과 진행 중 참가 잠금이 복사 과정에서도 분리되는지
11. Unity Authentication username 허용 문자와 길이 검증
12. Unity Authentication password 길이와 문자군 검증

### PlayMode

파일: `Assets/Tests/PlayMode/LocalHostTests.cs`

현재 17개:

1. diagnostics가 Play session의 검색 가능한 프로세스별 파일을 실제로 기록하는지
2. 로컬 호스트 시작과 owned player 생성
3. Create Room UI 입력값, 잠금 상태, 실제 room name, host 한 명만 표시
4. 1인 연습 하이더와 Hunt 시작
5. Lobby에서 즉시 Hunt 시작하는 개발 경로
6. Hunter Choice Platform 역할 선택
7. Hide/Hunt 단계별 스폰 이동
8. 헌터 사격, 하이더 탈락, Result 표시 후 Lobby 자동 복귀와 상태 초기화
9. 자세 회전, 위치, 기본 controller 크기
10. 연습 하이더가 Local player를 덮어쓰지 않음
11. 낙하 후 복귀
12. 페인트 초기화, 색상, 브러시, 카메라 orbit, persistence, Hunt 페인팅
13. loopback/LAN 중복 광고를 하나의 로컬 방으로 합치고 기존 3초 만료 시간을 넘겨 유지
14. Home에서 미리 발견한 방이 Join Room의 실제 room row에 표시됨
15. 호스트가 생성한 방에 입장한 뒤에도 LAN 광고가 3초 만료 시간을 넘어 유지됨
16. LAN 참가 시 방 광고에 포함된 Unity Transport 포트를 실제 접속에 사용함
17. Join Room 오류 메시지가 자동 목록 갱신 후에도 유지되고 새로고침/화면 재진입 시 초기화됨

직전 검증 기준선은 EditMode `16/16`, PlayMode `16/16` 통과다. 새 Join Room 회귀 테스트를 포함한 PlayMode `17/17` 재검증은 대기 중이다.

## 3. Unity Test Runner 실행

Unity Editor:

1. `Window > General > Test Runner`
2. EditMode 탭에서 `Run All`
3. PlayMode 탭에서 `Run All`
4. 실패 항목의 assertion과 Console 로그를 함께 확인

CLI는 같은 프로젝트를 Unity Editor에서 열고 있지 않을 때만 사용한다. 기본 전체 검증은 다음 한 줄이다.

```bash
scripts/unity-test.sh all
```

`edit` 또는 `play` 인자로 한 test platform만 실행할 수 있다. 결과 XML과 전체 Unity 로그는 ignored 디렉터리인 `Logs/`에 생성된다. 다른 Unity 설치를 쓸 때만 `UNITY_PATH`를 지정한다.

## 4. 포트와 테스트 격리

- 로컬 게임 포트는 UDP `7778`이다.
- PlayMode 네트워크 테스트를 동시에 실행하지 않는다.
- Multiplayer Play Mode의 Player 2가 이전 호스트를 유지하는지 확인한다.
- `Failed to bind UDP socket`이 나오면 먼저 `lsof -nP -iUDP:7778`로 실제 사용 프로세스를 확인한다.
- 포트가 비어 있고 이전 테스트 teardown 직후라면 실패 테스트만 한 번 재실행한다.
- 반복 실패를 transient로 무시하지 말고 `NetworkManager`와 `UnityTransport` shutdown 경로를 조사한다.

## 5. 로컬 멀티플레이 수동 설정

권장:

1. `Window > Multiplayer > Multiplayer Play Mode`
2. Player 2를 활성화
3. Main Editor에서 Play
4. 한 창에서 방 생성
5. 다른 창에서 방 목록 또는 개발용 Join Local로 참가

대안:

- Standalone build 한 개와 Unity Editor 한 개를 사용한다.
- 같은 컴퓨터에서는 host address `127.0.0.1`을 사용한다.
- LAN에서는 검색된 host address를 사용한다.

별도 프로세스 smoke build:

1. `LocalSmokeBuild.BuildMac`으로 development build를 만든다.
2. `-mechaSmoke host`를 실행한다.
3. `-mechaSmoke discover`가 이름, 잠금, 인원 광고를 확인한다.
4. `-mechaSmoke client-wrong`이 잘못된 비밀번호 거부를 확인한다.
5. `-mechaSmoke client`가 올바른 비밀번호 참가와 게임 상태 동기화를 확인한다.

## 6. Staging 확인

Cloud Project 연결 전 확인:

1. Unity 메뉴 `Mecha Chameleon > Environment > Staging (MPS Relay)`를 선택한다.
2. Play Mode를 다시 시작한다.
3. Account Access 화면이 나타나고 Home으로 우회하지 않는지 확인한다.
4. 가입 또는 로그인 시 `Unity Cloud Project is not linked`와 `Project Settings > Services` 안내가 화면과 diagnostics 로그에 나타나는지 확인한다.
5. Development 메뉴로 되돌리면 UGS 초기화 없이 기존 LAN 방 생성/검색이 계속 동작하는지 확인한다.

Cloud Project 연결 후 확인:

1. Unity Dashboard에 `staging` environment를 만들고 Username/Password Authentication provider를 활성화한 뒤 Editor를 Cloud Project에 연결한다.
2. 별도 Editor 프로세스 또는 Editor와 standalone build에서 서로 다른 username/password 계정을 만든다.
3. 두 프로세스를 다시 실행해 별도 입력 없이 각자의 세션이 복구되는지 확인한다.
4. 한 프로세스에서 Sign Out 후 Account Access로 돌아가고, 기존 계정으로 다시 Sign In 되는지 확인한다.
5. Player 1이 공개 방과 8자 이상 비밀번호 방을 각각 생성한다.
6. Player 2가 새로고침 후 방을 보고 참가한다.
7. Lobby, Paint, Hunt, Result, Lobby를 거치며 두 창의 인원, 역할, 타이머, 페인트, 명중, 승패가 일치하는지 확인한다.
8. Hunt 중 방이 목록에서 제외되고 Lobby 복귀 후 다시 표시되는지 확인한다.
9. 호스트가 나가면 클라이언트가 Room을 종료하고 Home으로 돌아오는지 확인한다.

Cloud Project가 연결되지 않은 현재 단계에서는 실제 Relay 할당과 두 사용자 접속은 통과 판정을 내리지 않는다.

## 7. 필수 수동 시나리오

### A. Home과 방 생성

- 실행 직후 Home만 보이고 플레이어가 생성되지 않는다.
- 빈 방 이름은 생성되지 않는다.
- 공개 방 생성 후 호스트가 Room의 3D 로비에 나타난다.
- 잠긴 방은 목록에 자물쇠로 표시되며 비밀번호 원문은 보이지 않는다.
- 연결 중 Create를 여러 번 눌러도 호스트가 중복 시작되지 않는다.

### B. 방 참가

- Player 2가 방 목록에서 Player 1의 방을 본다.
- 공개 방에 즉시 참가한다.
- 올바른 비밀번호로 잠긴 방에 참가한다.
- 잘못된 비밀번호는 Room으로 넘어가지 않고 오류를 표시한다.
- 사라진 방을 선택하면 Join 화면을 유지한다.
- 참가/검색 오류와 진행 메시지가 자동 목록 갱신으로 사라지지 않는다. 새로고침이나 Join 화면 재진입 시 이전 메시지가 초기화된다. `JoinRoomKeepsFailureVisibleUntilRefresh`가 오류 유지 및 초기화를 검증한다.
- 참가 뒤 두 플레이어 모두 같은 인원과 방 정보를 본다.

### C. Room 권한

- 호스트만 방장으로 표시된다.
- 호스트만 Start와 End Game을 볼 수 있다.
- 클라이언트가 Start 또는 End Game 네트워크 명령을 위조해도 서버가 거부한다.
- 플랫폼 위에 서면 역할 표시가 즉시 HUNTER로 바뀐다.
- 내려오면 HIDER로 바뀐다.

### D. 라운드 흐름

- Start 한 번으로 Hide/Paint가 시작된다.
- 하이더만 Hiding Room으로 이동하고 헌터는 Lobby에 남는다.
- 타이머가 `HIDE 30`부터 감소한다.
- 30초 후 헌터가 Hiding Room에 들어오고 `HUNT 60`이 시작된다.
- Hunt 종료 또는 모든 하이더 탈락 후 각자 올바른 승패를 본다.
- 결과 뒤 모든 플레이어가 Lobby로 돌아간다.
- 다음 Start 전에 paint, pose, alive가 초기화된다.

### E. 플레이어 동작

- WASD와 마우스 회전
- Esc는 커서만 해제하고 Options 또는 다른 화면을 열거나 닫지 않음
- 해제된 커서로 UI 버튼을 누를 때 커서가 다시 잠기거나 게임 입력이 실행되지 않음
- Space 점프
- 벽 근처 Space 유지로 오르기
- Space 해제 후 벽에 매달리기
- 다시 Space로 떨어지기
- 1/2/3 자세에서 캐릭터 크기가 변하지 않음
- 기울기와 눕기 상태로 벽 가까이 이동 가능
- 머리와 몸이 벽을 뚫지 않음
- 낙하 시 현재 역할과 phase에 맞는 위치로 복귀

### F. 페인팅

- Hider만 Hide/Paint와 Hunt에서 P로 진입
- 색상 휠과 brightness가 선택 색과 일치
- cyan brush outline과 실제 도장 크기가 일치
- 작은 크기 3개와 giant brush 순환
- 몸 위 드래그는 paint, 빈 영역 드래그는 camera orbit
- paint mode를 나가도 그림 유지
- Hunt 중에도 그림 추가 가능
- 다른 클라이언트에서 stroke가 같은 위치와 색으로 보임
- Lobby reset 뒤 그림이 흰색으로 초기화
- 어두운 영역에서도 색이 지나치게 탁하거나 검게 보이지 않음

### G. 헌터

- 헌터 카메라는 1인칭
- 마우스 왼쪽과 F가 cursor 방향으로 발사
- 총 모델이 보임
- 매 발사마다 ray가 보이며 이전 ray가 적절히 사라짐
- 빗나감과 명중 상태가 구분됨
- 서버가 살아 있는 Hider에게만 명중을 적용

### H. Options와 종료

- Options는 화면의 Options 버튼으로만 열림
- Options를 열면 커서가 해제되어 버튼을 정상적으로 누를 수 있음
- Graphics 변경이 즉시 품질 단계에 반영
- Master와 SFX volume이 즉시 반영
- Back이 이전 화면으로 돌아감
- 클라이언트 Leave Room 후 Home으로 이동
- 호스트 End Game 후 모든 참가자가 Home으로 이동
- 종료 뒤 같은 포트로 새 방을 만들 수 있음

## 7. 화면 검증

최소 해상도:

- 1280 x 720
- 1920 x 1080

확인:

- Login/Home/Create/Join/Room/Options에서 텍스트 잘림 없음
- 긴 허용 범위의 room name이 목록 밖으로 넘치지 않음
- 타이머, 역할, Options가 겹치지 않음
- 페인트 panel이 캐릭터의 주요 paint area를 막지 않음
- modal 뒤 입력이 3D 캐릭터나 다른 panel로 전달되지 않음
- 저사양 품질에서도 역할과 paint 색을 구분 가능

## 8. 새 기능의 테스트 요구사항

| 변경 | 최소 자동 테스트 |
| --- | --- |
| Home/UI 전환 | 각 화면 전환과 네트워크 시작 전 player 미생성 |
| Account Access | username/password validation, signed-out online room 차단, password 비기록 |
| Create Room | 입력 검증, Host 성공/실패 정리 |
| LAN 목록 | 광고 수신, 중복 제거, timeout 제거 |
| 비밀번호 | 공개, 정답, 오답 connection approval |
| Room 정보 | 호스트와 클라이언트 인원/방장 동기화 |
| Start/End Game | 호스트 허용, 클라이언트 거부 |
| Options | 품질/볼륨 반영, context action 표시 |
| 게임플레이 수정 | 영향받는 기존 PlayMode 시나리오 회귀 |
| Pose Catalog | ID/shortcut 중복, default pose, visual/collision 회귀 |
| Room Module | 고유 RoomId, 모든 spawn/platform reference, host room 선택 권한 |

UI의 색, 정렬, 카메라 가림, 조명은 자동 테스트만으로 완료 처리하지 않는다.

## 9. 버그 수정 완료 기준

- 재현 절차를 먼저 확인했다.
- 원인을 담당 컴포넌트까지 좁혔다.
- 실패를 잡는 테스트를 추가하거나 기존 테스트가 왜 충분한지 기록했다.
- 수정 후 관련 자동 테스트가 통과했다.
- 호스트와 클라이언트 양쪽에서 수동 확인했다.
- 다른 phase와 역할에서 부작용이 없는지 확인했다.

## 10. 진단 로그 확인

Unity 메뉴에서 `Mecha Chameleon > Open Diagnostics Folder`를 연다. 각 프로세스는 `latest-MainEditor-<pid>.log`, `latest-Player_2-<pid>.log`처럼 별도 파일을 사용한다.

우선 다음 문자열을 검색한다.

- `level=ERROR`: exception, assertion, Unity error
- `level=WARN`: Unity warning 또는 이유가 있는 연결 해제
- `category=network`: host, join, approval, disconnect, shutdown
- `category=discovery`: UDP 광고, 수신 heartbeat, 방 만료
- `category=ui`: 화면 전환, 캐시 방 수, 실제 표시 행 수
- `category=round`: player spawn, 역할, phase, hit, result
- `category=auth`: session restore, 가입/로그인 성공 또는 비밀값 없는 실패 종류

한 줄에는 `session=<id>`가 포함되므로 같은 프로세스 파일에서 현재 Play session만 쉽게 구분할 수 있다. 계정과 방 비밀번호 원문, paint stroke payload는 로그에 없어야 한다.
