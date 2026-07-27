# Mecha Chameleon

Unity 6로 만드는 로컬 멀티플레이 카멜레온 숨바꼭질 게임이다. 한 플레이어가 호스트가 되고 같은 컴퓨터 또는 LAN의 다른 플레이어가 참가하는 완성 가능한 vertical slice를 현재 목표로 한다.

## 문서

작업 전에는 반드시 `AGENTS.md`부터 읽는다.

- `AGENTS.md`: 작업 규칙과 문서 우선 원칙
- `GDD.md`: 게임 규칙, 화면 흐름, 현재 및 미래 범위
- `ARCHITECTURE.md`: NGO 권한, 로컬 방, UI와 코드 구조
- `ART_STYLE.md`: 집 내부, 캐릭터, 소품, 재질과 조명 기준
- `TEST.md`: 자동 및 수동 멀티플레이 검증

## 현재 기술

- Unity 6000.1.1f1
- Netcode for GameObjects 2.4.3
- Unity Transport 2.6.0
- Multiplayer Play Mode 1.5.0
- Built-in Render Pipeline
- Unity Test Framework 1.6.0

## 현재 구현

- localhost 호스트 및 클라이언트
- 3D Lobby와 Hunter Choice Platform
- Lobby, Hide/Paint, Hunt, Result 라운드
- 30초 숨기와 60초 사냥 타이머
- 하이더 이동, 점프, 벽 오르기, 자세 변경
- RGB 색상 휠과 UV brush painting
- 헌터 1인칭 사격, shot ray, 명중과 승패
- 집 내부 Hiding Room, 가구, 벽지, baked lighting

Home, Create Room, Join Room, Room, Options의 실제 uGUI 흐름과 LAN 방 목록은 다음 구현 범위다.

## 실행

1. Unity Hub에서 이 폴더를 연다.
2. `Assets/Scenes/Mvp.unity`를 연다.
3. Play를 누른다.
4. 현재 개발 UI에서는 `Host Local`로 시작한다.
5. 두 번째 플레이어는 Multiplayer Play Mode 또는 standalone build에서 `Join Local`을 사용한다.

로컬 전송 포트는 UDP `7778`이다.
