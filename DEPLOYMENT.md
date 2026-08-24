# Mecha Chameleon Deployment Guide

## 1. 목적

Development, Staging, Production이 같은 코드와 `Mvp` 씬을 사용하면서도 서로의 네트워크 환경과 산출물을 섞지 않게 한다. 모든 빌드는 `Assets/Editor/DeploymentBuild.cs`를 통해 생성한다.

## 2. 환경 매트릭스

| 환경 | 네트워크 | 빌드 define | 디버그 | Cloud Project | Git 상태 | 기본 출력 |
| --- | --- | --- | --- | --- | --- | --- |
| Development | localhost/LAN | `MECHA_DEVELOPMENT` | Development + script debugging | 불필요 | dirty 허용 | `Builds/development/MechaChameleon.app` |
| Staging | MPS Sessions + Relay | `MECHA_STAGING` | Development 로그 포함 | 필수 | dirty 허용, manifest 기록 | `Builds/staging/MechaChameleon.app` |
| Production | MPS Sessions + Relay | `MECHA_PRODUCTION` | release + LZ4HC | 필수 | clean 필수 | `Builds/production/MechaChameleon.app` |

Editor의 `Mecha Chameleon > Environment` 메뉴는 Play Mode 테스트용이다. 실제 빌드 환경은 `DeploymentBuild`가 넣는 compile define이 결정하므로 EditorPrefs 상태에 영향받지 않는다.

전역 `Scripting Define Symbols`에 `MECHA_*`를 직접 추가하지 않는다. 빌드 사전 검증이 전역 환경 define을 발견하면 실패한다.

## 3. 기본 명령

프로젝트 루트에서 실행한다. 같은 프로젝트를 Unity Editor에서 열어 둔 상태로 실행하지 않는다.

```bash
# 전체 자동 테스트
scripts/unity-test.sh all

# 빌드 전 사전 조건만 확인
scripts/unity-build.sh development validate
scripts/unity-build.sh staging validate
scripts/unity-build.sh production validate

# 실제 macOS 산출물 생성
scripts/unity-build.sh development build
scripts/unity-build.sh staging build
scripts/unity-build.sh production build
```

Unity 위치가 다르면 환경 변수로 한 번만 지정한다.

```bash
UNITY_PATH="/path/to/Unity" scripts/unity-build.sh development build
```

기본 출력 경로를 바꿔야 할 때는 Unity 인자를 그대로 전달한다.

```bash
scripts/unity-build.sh staging build -mechaBuildPath "/tmp/MechaChameleon-Staging.app"
```

Unity Editor에서는 `Mecha Chameleon > Build > macOS`와 `Mecha Chameleon > Build > Validate` 메뉴가 같은 코드를 호출한다.

## 4. 산출물 추적

성공한 앱과 같은 디렉터리에 `build-manifest.json`을 생성한다.

- environment와 network mode
- Unity 및 application version
- Git commit과 branch
- dirty 여부
- Cloud Project ID (온라인 환경만)
- UTC build time, 출력 경로, 크기, 소요 시간

`Builds/`와 `Logs/`는 Git에 넣지 않는다. 배포하거나 QA에 전달할 때 앱과 manifest를 항상 한 쌍으로 보관한다.

## 5. 개발과 승격 흐름

### Development

1. 기능 구현 후 `scripts/unity-test.sh all`을 통과한다.
2. `scripts/unity-build.sh development build`로 LAN용 앱을 만든다.
3. Editor + 앱 또는 앱 두 개로 localhost/LAN 흐름을 확인한다.

### Staging

1. Unity Dashboard에 정확히 `staging` 이름의 environment를 만든다.
2. Username/Password Authentication provider를 활성화하고 Project Settings > Services에서 Cloud Project를 연결한다.
3. 전체 테스트 후 Staging build를 만든다.
4. 서로 다른 username/password 계정 두 개로 가입, 세션 복구, 로그아웃, 재로그인을 확인한다.
5. 두 계정으로 방 생성, 검색, 참가와 전체 라운드를 검증한다.
6. diagnostics 로그와 `build-manifest.json`을 테스트 결과와 함께 보관한다.

Cloud Project가 연결되지 않은 동안 Staging validation/build가 실패하는 것이 정상이다. Editor Play Mode에서는 미연결 안내 UI까지 확인할 수 있다.

### Production

1. 검증된 Staging commit을 사용한다.
2. `ProjectSettings/ProjectSettings.asset`의 application version과 Company Name을 확정한다.
3. 모든 변경을 commit하고 Git working tree를 깨끗하게 만든다.
4. 전체 테스트와 Production validation을 통과한다.
5. Production build를 생성하고 manifest의 commit, environment, dirty=false를 확인한다.
6. 코드 서명, notarization, 업로드는 배포 채널이 정해진 뒤 해당 자동화를 연결한다.

Staging과 Production은 서로 다른 UGS environment를 사용한다. Staging Session이 Production 목록에 보이면 배포 오류로 간주한다.

## 6. 실패 진단

- Unity 실행/컴파일 전체 로그: `Logs/<environment>-<mode>.log`
- 자동 테스트 결과: `Logs/editmode-results.xml`, `Logs/playmode-results.xml`
- `Unity Cloud Project is not linked`: Services에서 프로젝트를 연결한 뒤 Unity를 다시 시작한다.
- Development build의 `[ServicesCore]` 미연결 warning은 설치된 UGS 패키지의 공통 preprocess 경고다. Development는 UGS를 호출하지 않으므로 빌드 성공 시 무시할 수 있다.
- global environment define 오류: Player Settings의 Scripting Define Symbols에서 모든 `MECHA_*`를 제거한다.
- Production dirty 오류: `git status --short`를 확인하고 필요한 변경을 commit한다.
- Company Name 오류: 임시 기본값 `DefaultCompany`를 실제 값으로 바꾼다.
- Unity lock 오류: 열려 있는 같은 프로젝트 Editor를 종료한 뒤 CLI를 다시 실행한다.
- Unity 6000.1.1f1에서는 `BuildOptions.DetailedBuildReport`가 scene asset 집계 중 native crash를 일으킬 수 있어 사용하지 않는다. 기본 BuildReport summary와 별도 manifest만 유지한다.

## 7. 현재 의도적 범위 밖

- GitHub Actions/Unity Build Automation
- 자동 코드 서명과 Apple notarization
- Steam/App Store 업로드
- staged rollout과 자동 rollback
- dedicated server build

이 항목은 Cloud Project와 실제 배포 채널이 결정된 뒤 추가한다. 현재는 한 컴퓨터에서 동일한 명령으로 재현 가능한 환경별 앱 생성이 기준선이다.
