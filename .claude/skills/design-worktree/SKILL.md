---
name: design-worktree
description: 현재 git 상태와 변경 파일을 분석해 기능별 worktree 구조를 설계하고
git worktree add 명령어까지 생성한다. 병렬 개발을 시작하기 전 worktree를
설계할 때 사용한다.
---

## 도메인 특화 규칙

- Worktree 분리 기준: **기능별** (View + ViewModel + Service를 하나의 기능으로 묶음)
- Worktree 디렉토리: `../RLC-{feature}` (프로젝트 루트 상위에 생성)
- Branch 네이밍: `feature/{feature-name}` (kebab-case)
- 기능 분류 기준:

  | 기능 | 핵심 파일 |
  |------|-----------|
  | protocol   | PlcProtocol.cs, ModbusPlcService.cs, RLC_IO_Protocol_Map.xlsx |
  | rlc-status | RlcStatusView.xaml*, RlcStatusViewModel.cs, PanelDiagram* |
  | metering   | MeteringView.xaml*, MeteringViewModel.cs |
  | history    | OperationHistoryView.xaml*, OperationHistoryViewModel.cs |
  | auto-mode  | AutoOperationService.cs |

- 공유 파일 (충돌 위험): App.xaml.cs, MainWindow.xaml.cs, ServiceHub.cs
- Merge 우선순위: protocol → 기능 worktree들 → develop → main

---

## 절차

1. **현재 작업 상태 분석**
   - `git status`로 변경 파일 목록 수집
   - `git branch -a`로 기존 브랜치 확인
   - `git log --oneline -10`으로 최근 커밋 맥락 파악

2. **기능별 파일 분류**
   - 변경 파일을 도메인 특화 규칙 분류표에 매핑
   - 공유 파일(App.xaml.cs 등) 별도 표시
   - 분류 불가 파일은 가장 근접한 기능에 배치

3. **병렬 / 순차 작업 구분**
   - 병렬 가능: 서로 다른 기능, 공유 파일 없음
   - 순차 필수: protocol 변경 포함 시 먼저 develop merge 필요
   - 공유 파일 수정 worktree는 나머지보다 먼저 merge 권장

4. **Worktree 설계 출력**
   ```
   ┌──────────────┬────────────────────┬──────────────────┐
   │ 기능         │ Branch             │ 디렉토리         │
   ├──────────────┼────────────────────┼──────────────────┤
   │ protocol     │ feature/protocol   │ ../RLC-protocol  │
   │ rlc-status   │ feature/rlc-status │ ../RLC-rlcstatus │
   │ metering     │ feature/metering   │ ../RLC-metering  │
   └──────────────┴────────────────────┴──────────────────┘

   ⚠ 공유 파일: App.xaml.cs → protocol worktree 담당
   ⚠ Merge 순서: protocol → rlc-status, metering → develop → main
   ```

5. **Claude 역할 정의**
   ```
   [protocol]   PlcProtocol.cs · ModbusPlcService.cs · Excel 동기화
                규칙: 태그 네이밍, Modbus 주소 체계 준수
   [rlc-status] RlcStatusView · RlcStatusViewModel · PanelDiagram
                규칙: FB 피드백 기반 UI, MVVM 바인딩
   [metering]   MeteringView · MeteringViewModel · SciChart 트렌드
                규칙: SciChart v9, 500ms 폴링 주기
   [history]    OperationHistoryView · OperationHistoryViewModel
                규칙: DB 연동(Npgsql), 페이징 처리
   [auto-mode]  AutoOperationService
                규칙: rlc-status merge 후 시작, RlcStatusViewModel 의존
   ```

6. **사용자 확인** — 설계안 검토 후 수정 요청을 받는다.

7. **git 명령어 생성**
   ```powershell
   # main 최신화 확인
   git checkout main
   git pull origin main

   # Worktree 생성
   git worktree add ../RLC-protocol   feature/protocol
   git worktree add ../RLC-rlcstatus  feature/rlc-status
   git worktree add ../RLC-metering   feature/metering
   git worktree add ../RLC-history    feature/history
   git worktree add ../RLC-automode   feature/auto-mode
   ```

8. **산출물 저장 여부 확인** — 결과를 파일로 저장할지 묻는다.

---

## 원칙

- main 최신화 확인 후 worktree를 생성한다
- 공유 파일은 반드시 한 worktree에서만 담당한다
- protocol worktree는 다른 worktree보다 먼저 develop에 merge한다
- auto-mode는 RlcStatusViewModel 의존성으로 rlc-status merge 후 시작 권장

---

## 품질 체크리스트

- [ ] git 명령으로 현재 브랜치 상태를 실제 확인했는가
- [ ] 공유 파일 담당 worktree를 명시했는가
- [ ] Merge 순서를 명시했는가
- [ ] 각 worktree의 Claude 역할을 정의했는가
- [ ] git worktree add 명령어를 생성했는가

---

## 산출물

터미널 출력 (기본).
파일 저장 원할 시: `design-worktree-result-{YYYYMMDD}.md`
