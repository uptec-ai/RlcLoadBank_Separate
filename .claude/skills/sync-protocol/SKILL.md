---
name: sync-protocol
description: PlcProtocol.cs의 IoPoint 정의와 RLC_IO_Protocol_Map.xlsx 05_ModbusBitMap을
비교하여 불일치 항목을 보고한다. PlcProtocol.cs 또는 Excel 수정 후 양쪽 동기화 여부를
확인할 때 사용한다.
---

## 도메인 특화 규칙

- 비교 대상: `RLC_LoadBank_SeparateVer/Services/PlcProtocol.cs` ↔
  `Document/RLC_IO_Protocol_Map.xlsx` 시트 `05_ModbusBitMap`
- Excel 읽기: PowerShell COM (`New-Object -ComObject Excel.Application`)
- Active = "Y" 행만 비교 대상으로 한다 (N은 예비점으로 무시)
- 태그 네이밍 규칙:
  - R/L 부하: `P{n}_{R|L}_{RN|SN|TN}_{01-08}` (PNL-1) / `P{n}_{R|L}_{01-08}` (PNL-2/3)
  - C 부하 DI: `P{n}_C{stage}_{RESULT|MC1_FB|MC2_FB|SCR_FB}`
  - C 부하 DO: `P{n}_C{stage}_CMD`
  - 보호 DI: `P{n}_{OVR|OCR|HT|FAN|MCCB_*|EMG|DOOR|PWR_*|CTRL_*|LOC_REM}_FB`
  - 명령 DO: `P{n}_{FAN_*|MCCB_*|RESET}_CMD`
- DI = FC02 ReadInputs / DO = FC05/FC15 Coils (zero-based offset 기준)
- 경로는 `Resolve-ProjectRoot.ps1` 기준 상대경로 사용

---

## 절차

1. **PlcProtocol.cs 읽기**
   - `ForPanel(0~2)` 전체 `PlcIoPoint` 추출
   - 각 포인트에서 Tag, DiAddr, DoAddr, Kind 수집
   - Kind별 분류: McLoad(DI=DO) / StatusFb·CResult·CAlarm(DI only) / CmdDo·CCmdDo(DO only)

2. **Excel 05_ModbusBitMap 읽기**
   - PowerShell COM으로 시트 로드, 헤더(1행) 제외
   - Active = "Y" 행만 수집
   - 각 행에서 Panel, I/O(DI/DO), Zero-based Offset, Tag Name, Description 추출

3. **비교 실행**
   - `(Panel, I/O, Offset)` 키로 코드 포인트 ↔ Excel 행 매칭
   - 감지 항목:
     - **[ERROR] 태그 불일치**: 같은 키인데 Tag Name이 다름
     - **[ERROR] Offset 불일치**: 같은 태그인데 Offset이 다름
     - **[ERROR] 코드 초과**: 코드에 있는데 Excel Active=Y에 없음
     - **[ERROR] Excel 초과**: Excel Active=Y인데 코드에 없음
     - **[WARN]  설명 불일치**: Tag·Offset 일치하나 Description이 다름

4. **결과 출력**
   ```
   [ERROR] 태그 불일치   | PNL-1 DI off=48 | 엑셀: P1_C1_R_MC_FB | 코드: P1_C1_RESULT
   [ERROR] Offset 불일치 | P1_OVR_FB       | 코드: DI=54 | 엑셀: DI=56
   [WARN]  설명 불일치   | P1_C1_RESULT    | 코드: "C부하 STEP1..." | 엑셀: "..."
   ---
   ERROR 2건 / WARN 1건 / OK 142건
   ```

5. **사용자 확인** — 불일치 항목에 대해 추가 조치 여부를 묻는다.

---

## 원칙

- 수정은 하지 않는다 — 보고만 한다
- Active = "N" 행은 비교에서 제외한다
- PNL-M은 PlcProtocol 정의가 없으므로 검사 대상 제외
- Excel COM 객체는 반드시 마지막에 해제한다 (`ReleaseComObject`)

---

## 품질 체크리스트

- [ ] PNL-1/2/3 전체 포인트를 추출했는가
- [ ] Excel Active="N" 행을 제외했는가
- [ ] ERROR / WARN / OK 카운트를 요약 출력했는가
- [ ] Excel COM 객체를 정상 해제했는가
- [ ] PNL-M을 제외했는가

---

## 산출물

터미널 출력 (기본).
파일 저장 원할 시: `sync-protocol-result-{YYYYMMDD}.md`
