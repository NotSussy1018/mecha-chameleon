# Mecha Chameleon Game Design Document

## 1. 문서 상태

- 대상 빌드: Development LAN + Staging online multiplayer vertical slice
- 엔진: Unity 6000.1.1f1
- 기준 씬: `Assets/Scenes/Mvp.unity`
- 네트워크 범위: Development는 같은 컴퓨터/LAN, Staging과 Production은 MPS Sessions + Relay
- 이 문서에서 `현재`는 코드에 구현된 상태, `목표`는 다음 검증에 포함할 상태, `미래`는 이번 범위 밖을 뜻한다.

## 2. 한 줄 설명

작은 카멜레온들이 집 안의 색과 형태에 맞게 몸을 칠하고 자세를 바꿔 숨으면, 한 명의 헌터가 제한 시간 안에 이들을 찾아 사격하는 짧은 로컬 멀티플레이 숨바꼭질 게임이다.

## 3. 디자인 기둥

### 직접 만드는 위장

하이더는 자신의 몸에 직접 색을 칠하고 자세를 바꾼다. 숨는 재미는 스킨 선택이 아니라 방을 관찰하고 즉석에서 위장을 만드는 데서 나온다.

### 짧고 명확한 라운드

숨기 30초, 사냥 60초를 기본으로 한다. 역할, 남은 시간, 승패가 항상 읽혀야 한다.

### 장난감 같은 집

맵은 비어 있는 테스트장이 아니라 다양한 벽지, 바닥, 가구가 있는 밝은 실내다. 가구는 장식인 동시에 실제 은신처다.

### 가벼운 파티 플레이

Development에서는 계정 설정 없이 LAN으로, Staging/Production에서는 username/password 계정으로 로그인한 뒤 Relay 방을 만들고 다른 플레이어가 참가할 수 있어야 한다.

## 4. 대상 경험

- 2명 이상 권장, 개발용 1인 연습 모드 지원
- 한 라운드 약 90초와 짧은 결과 표시
- 키보드와 마우스 우선
- 저사양 컴퓨터에서도 안정적인 실내 플레이
- 경쟁 랭크보다 웃긴 위장, 발견, 추격 순간을 우선

## 5. 전체 흐름

```text
Staging / Production
  -> Restore Session
  -> Sign In / Create Account
  -> Home

Home
  -> Create Room -> Room
  -> Join Room -> Password -> Room
  -> Options

Room
  -> Lobby
  -> Hide/Paint (30s)
  -> Hunt (60s)
  -> Result
  -> Lobby

Leave Room / End Game
  -> Home
```

네트워크 접속 전에는 3D 플레이어를 생성하지 않는다. 호스트 또는 참가에 성공하면 Room의 3D 로비로 들어간다.

## 6. 화면 정의

### 6.0 Account Access

상태: **Staging/Production username/password 가입, 로그인, 세션 복구, 로그아웃 현재**

- Development에서는 이 화면을 건너뛰고 Home으로 이동한다.
- Staging/Production에서는 유효한 Unity Authentication 세션이 있어야 Home과 온라인 방 기능을 사용할 수 있다.
- `Sign In`과 `Create Account`를 한 화면의 탭으로 전환한다.
- username은 3~20자의 영문자, 숫자, `.`, `-`, `@`, `_`만 허용한다.
- password는 8~30자이며 대문자, 소문자, 숫자, 특수문자를 각각 하나 이상 포함한다.
- 가입 시 password 확인 입력이 일치해야 한다.
- 성공한 세션은 Unity Authentication의 세션 토큰으로 다음 실행에서 복구한다.
- `Sign Out`은 로컬 세션 credential을 지우고 Account Access로 돌아간다.
- 비밀번호 원문은 UI 입력 이외의 게임 상태, 로그, diagnostics, room/session data에 남기지 않는다.

### 6.1 Home

상태: **환경별 화면 전환과 연결 기능 현재**

첫 실행 화면이다. 배경으로 집 내부를 보여줄 수 있지만 캐릭터 조작과 라운드 HUD는 비활성화한다.

필수 명령:

- `Create Room`
- `Join Room`
- `Options`
- Staging/Production의 현재 username과 `Sign Out`
- 게임 종료는 데스크톱 빌드에서 작은 종료 아이콘 또는 Options 안에 둔다.

### 6.2 Create Room

상태: **LAN 및 MPS 방 생성과 입력 검증 현재**

필수 입력:

- Room Name
- Room Password
- Create
- Back

규칙:

- 방 이름은 비어 있을 수 없으며 앞뒤 공백을 제거한다.
- 비밀번호는 비워서 공개 방을 만들 수 있다.
- Development 비밀번호는 로컬 파티의 실수 방지용이다. Staging/Production 비밀번호는 MPS Session의 8~32자 규칙을 따른다.
- 생성 성공 시 사용자는 호스트, 방장, 로비 플레이어가 된다.
- 실패 시 입력값을 보존하고 화면 안에 원인을 표시한다.

미래 항목:

- Map Selection
- Capacity

미래 항목은 구현 전까지 숨기거나 `Coming later` 상태로 비활성화한다. 작동하지 않는 선택값을 저장하지 않는다.

### 6.3 Join Room

상태: **환경별 방 목록과 비밀번호 참가 현재**

필수 요소:

- 현재 환경에서 발견된 방 목록
- 방 이름
- 잠금 여부
- 현재 인원
- 새로고침
- Join
- Back

흐름:

1. Development에서는 LAN 광고를, Staging/Production에서는 MPS Session query를 검색한다.
2. 공개 방은 Join 즉시 접속한다.
3. 잠긴 방은 비밀번호 입력 모달을 연다.
4. 호스트 승인에 성공하면 Room으로 이동한다.
5. 방이 사라졌거나 비밀번호가 틀리면 목록 화면을 유지하고 오류를 표시한다.

방이 없을 때는 빈 목록 상태와 새로고침만 보여준다. 자동 매치메이킹은 제공하지 않는다.

### 6.4 Room

상태: **3D 로비와 실제 room data binding 현재**

현재:

- 실제 3D 로비
- 노란 Hunter Choice Platform
- 네트워크 플레이어 생성
- 플랫폼 위 플레이어를 헌터 후보로 표시
- 호스트가 Start 실행

현재 UI:

- 플레이어 목록
- 각 플레이어의 Hider/Hunter 의사 상태
- 방장 표시
- 방 이름, 잠금 여부, 인원
- 방장에게만 보이는 Start
- Options 열기

역할 규칙:

- 로비에서 Hunter Choice Platform 위에 있는 플레이어는 즉시 `Hunter`로 표시된다.
- 플랫폼에서 내려오면 즉시 `Hider`로 돌아간다.
- 라운드 시작 시 플랫폼 위 후보 중 한 명을 무작위로 헌터로 선택한다.
- 후보가 없으면 접속 플레이어 중 한 명을 무작위로 선택한다.
- 라운드가 시작된 뒤에는 역할이 고정된다.
- 방장은 역할과 별개이며 로컬 호스트다.

### 6.5 Game HUD

상태: **타이머, 역할, 결과, 기본 paint control binding 현재; 밝기와 사격 feedback 목표**

필수 요소:

- 화면 상단 중앙의 큰 타이머
- 화면 오른쪽 위의 `HUNTER` 또는 `HIDER`
- 하이더 페인팅 모드의 색상 휠, 밝기, 브러시 크기, 지우기
- 헌터 사격 결과 피드백
- `YOU WON` 또는 `YOU LOST` 결과 오버레이
- Options 열기

프로토타입 연결과 디버그 버튼은 최종 HUD에서 제거한다.

### 6.6 Options

상태: **디자인 현재, 설정 및 room command 연결 목표**

항상 제공:

- Graphics
- Master Volume
- SFX Volume
- Back

Room에서만 제공:

- Leave Room

호스트에게만 제공:

- End Game

동작:

- `Leave Room`은 자신의 네트워크 연결을 종료하고 Home으로 이동한다.
- `End Game`은 호스트 방을 종료하여 모든 참가자를 Home으로 돌려보낸다.
- 참가 클라이언트에게 `End Game`을 보여주지 않는다.
- Graphics는 기존 Unity Quality Level을 선택한다.
- 사운드는 현재 세션에 즉시 반영한다. 영구 저장은 미래 범위이며 필요 전까지 저장 시스템을 추가하지 않는다.

## 7. 라운드 규칙

### Lobby

- 모든 플레이어가 3D 로비에 있다.
- Hunter Choice Platform으로 헌터 선호를 표시한다.
- 방장만 Start를 누를 수 있다.
- 이전 라운드의 페인팅, 생존 상태, 자세를 초기화한다.

### Hide/Paint

- 기본 시간은 30초다.
- 하이더를 Hiding Room으로 이동한다.
- 헌터는 로비에 남아 Hiding Room을 볼 수 없어야 한다.
- 하이더는 이동, 점프, 벽 오르기, 자세 변경, 몸 칠하기를 할 수 있다.

### Hunt

- 기본 시간은 60초다.
- 헌터를 Hiding Room에 생성한다.
- 헌터는 1인칭 카메라와 총을 사용한다.
- 하이더는 계속 움직이고 자세를 바꾸며 몸을 칠할 수 있다.
- 헌터의 서버 판정 사격이 살아 있는 하이더를 맞히면 해당 하이더가 탈락한다.

### Result

- 모든 하이더가 제한 시간 전에 탈락하면 헌터 승리다.
- 시간이 끝날 때 한 명 이상의 하이더가 살아 있으면 하이더 승리다.
- 각 플레이어 관점에서 `YOU WON` 또는 `YOU LOST`를 표시한다.
- 짧은 결과 표시 후 플레이어를 Lobby로 돌려보낸다.
- 다음 Start 전에 라운드 상태를 초기화한다.

## 8. 플레이어 기능

현재 구현:

- 이동: `WASD`
- 시점: 마우스 이동
- 커서 해제: `Esc` (`Options`를 열거나 화면을 전환하지 않음)
- Options: 화면의 `Options` 버튼으로만 열기
- 점프: 지상에서 `Space`
- 벽 오르기: 공중에서 벽 가까이 `Space` 유지
- 벽 매달리기: 오른 뒤 `Space` 해제
- 벽에서 떨어지기: 매달린 상태에서 `Space`
- 자세: `1` 서기, `2` 오른쪽으로 크게 기울기, `3` 눕기
- 페인트 모드: 하이더가 `P`
- 페인트 모드 종료: `P`
- 페인트: 몸 위에서 마우스 왼쪽 드래그
- 페인트 카메라: 캐릭터 밖 빈 영역 드래그
- 팔레트 이동: `Z`, `X`
- 브러시 크기: `B`
- 페인트 초기화: `C`
- 헌터 사격: 마우스 왼쪽 또는 `F`
- 낙하 시 현재 단계의 안전한 스폰으로 복귀

페인팅 규칙:

- Head와 Body에 각각 128 x 128 런타임 텍스처를 사용한다.
- 색상 휠로 RGB 색을 선택할 수 있다.
- 작은 브러시부터 몸 전체용 giant brush까지 제공한다.
- Hide/Paint와 Hunt 단계에서 하이더가 사용할 수 있다.
- 페인트 모드를 나가도 현재 라운드의 그림은 유지된다.
- Lobby 초기화 시 그림을 지운다.

## 9. 맵

현재 맵은 두 공간으로 구성된다.

- Lobby: 바닥, 벽, 천장, Hunter Choice Platform
- Hiding Room: 서로 다른 벽지, 나무 바닥, 창문, 소파, 책장, 책상, 침대, 상자, 조명과 기타 가구

가구는 캐릭터가 실제로 몸을 숨길 만큼 커야 한다. 맵과 캐릭터의 비율은 `ART_STYLE.md`를 따른다.

추가 맵은 같은 `Mvp` 씬 안에서 독립된 `RoomModule` 콘텐츠로 만든다. 각 module은 자체 spawn과 Hunter Choice Platform을 가져야 한다. Map Selection UI는 별도 승인 전까지 미래 범위로 유지한다.

새 자세는 `DefaultPoseCatalog` 항목으로 추가한다. 자세는 캐릭터 scale을 바꾸지 않고 Visual Root의 회전과 위치로 표현하며, network ID는 기존 자세와 겹치거나 재사용할 수 없다.

## 10. 이번 빌드 범위

필수:

- Staging/Production username/password 가입, 로그인, 세션 복구, 로그아웃
- Home/Create Room/Join Room/Room/Options UI
- Development localhost/LAN 방 생성과 참가
- Staging MPS Session + Relay 방 생성, 목록 검색, 비밀번호 참가
- Development/Staging/Production 환경 분리와 명확한 서비스 초기화 오류
- 방 이름과 선택적 비밀번호
- 방장 전용 Start 및 End Game
- 기존 3D 로비와 라운드 연결
- UI 상태별 오류 및 빈 상태
- 기존 이동, 역할, 페인팅, 사격, 승패 유지

이번 범위 밖:

- Firebase
- 공개 매치메이킹
- 소셜 로그인, 이메일 로그인과 비밀번호 복구
- 통계, 아바타 등 영구 플레이어 프로필
- 랭크, 보상, 화폐, 상점
- 영구 설정 저장
- 전용 서버
- 서버 권한 안티치트
- 여러 맵
- 사용자 지정 인원
- 매치 재연과 하이라이트

## 11. UX 품질 기준

- Staging/Production에서 로그인하지 않은 사용자가 온라인 방 화면으로 우회할 수 없어야 한다.
- 가입/로그인 실패는 입력 화면을 유지하고 비밀번호를 로그에 남기지 않은 채 이해 가능한 오류를 보여야 한다.
- Home에서 현재 환경의 방을 만들고 다른 실행 창에서 참가하기까지 설명 없이 진행할 수 있어야 한다.
- 참가 실패가 무반응으로 보이지 않아야 한다.
- Start는 방장에게만 보이고 Lobby에서만 작동해야 한다.
- 역할과 타이머는 플레이 중 다른 UI보다 먼저 읽혀야 한다.
- UI가 플레이어 조작과 페인팅 입력을 동시에 먹지 않아야 한다.
- 1280 x 720과 1920 x 1080에서 주요 텍스트와 버튼이 겹치거나 잘리지 않아야 한다.
