-- ====================================================================
-- RLC Load Bank HMI — PostgreSQL schema v1  (확정 설계)
-- 대상 DB: DB_RLC (사전 생성 완료)
-- 연결 문자열: 환경변수 RLC_DB_CONN (Npgsql 형식) — 코드/DB에 저장하지 않음
--
-- 확정 원칙
--   1. 상태(status)는 주기 스냅샷이 아닌 "전이 이벤트"로 저장
--   2. 계측값은 1분 집계(avg/min/max)로 압축 저장
--      (720샘플/분 → 1row = 720:1 손실 압축, float4·smallint 콤팩트 타입)
--   3. 장비 식별은 device_type + unit_id + panel_no 를 각 row에 비정규화
--      (기록 시점의 매핑을 보존 — 장비 마스터 테이블 없음, IP/port는 appconfig)
--   4. 모든 테이블명은 tb_ 접두사
--   5. 모든 시각은 timestamptz
--
-- 이 스크립트는 멱등(IF NOT EXISTS) — 앱 EnsureSchema에서 반복 실행 가능.
-- ====================================================================

-- ── 스키마 버전 관리 ────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS tb_schema_version (
    version     integer PRIMARY KEY,
    applied_ts  timestamptz NOT NULL DEFAULT now(),
    description text
);
INSERT INTO tb_schema_version (version, description)
VALUES (1, 'initial schema: sessions, events, 1-min aggregates')
ON CONFLICT (version) DO NOTHING;

-- ── 앱 실행 세션 (이력 조회의 기준 축) ──────────────────────────────
CREATE TABLE IF NOT EXISTS tb_app_session (
    id          bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    started_ts  timestamptz NOT NULL,
    ended_ts    timestamptz,                 -- 정상 종료 시 UPDATE (NULL = 비정상 종료/실행 중)
    app_version text,
    panels_used smallint[] NOT NULL DEFAULT '{}'   -- 세션 중 연결됐던 판넬 누적 {1,3} 등
);
COMMENT ON TABLE tb_app_session IS 'HMI 앱 실행 단위. 데이터 공백이 앱 꺼짐인지 장비 단절인지 구분하는 축.';

-- ── 장비 연결/해제 이벤트 (pnl·isem·gimac status) ───────────────────
CREATE TABLE IF NOT EXISTS tb_connection_event (
    id          bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    ts          timestamptz NOT NULL,
    session_id  bigint REFERENCES tb_app_session(id),
    device_type text NOT NULL CHECK (device_type IN ('PLC','GIMAC','ISEM')),
    unit_id     smallint NOT NULL,           -- PLC는 판넬 번호와 동일
    panel_no    smallint CHECK (panel_no BETWEEN 1 AND 3),  -- 기록 시점의 연관 판넬
    connected   boolean NOT NULL,
    detail      text                         -- 단절/실패 사유 등
);
CREATE INDEX IF NOT EXISTS ix_tb_connection_event_ts     ON tb_connection_event (ts DESC);
CREATE INDEX IF NOT EXISTS ix_tb_connection_event_device ON tb_connection_event (device_type, unit_id, ts DESC);
COMMENT ON TABLE tb_connection_event IS '장비 연결 상태 전이. 특정 시점 상태 = 그 시점 이전의 마지막 row.';

-- ── MC 단위 상태 변화 (판넬별 MC status) ────────────────────────────
CREATE TABLE IF NOT EXISTS tb_mc_event (
    id         bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    ts         timestamptz NOT NULL,         -- 이벤트 확정 시각 (fb_ts 우선, 없으면 cmd_ts)
    session_id bigint REFERENCES tb_app_session(id),
    panel_no   smallint NOT NULL CHECK (panel_no BETWEEN 1 AND 3),
    mc_tag     text NOT NULL,                -- 예: 'P1_R_RN_01' (FB/CMD 접미사 제외)
    action     text NOT NULL CHECK (action IN ('ON','OFF')),
    mode       text NOT NULL CHECK (mode IN ('MANUAL','AUTO','LOCAL')),  -- LOCAL = 현장조작 감지(FB만 변화)
    cmd_ts     timestamptz,                  -- CMD 발행 시각 (LOCAL이면 NULL)
    fb_ts      timestamptz,                  -- FB 확인 시각 (fb_ts - cmd_ts = 응답 지연)
    confirmed  boolean NOT NULL,             -- false = CMD/FB 불일치 (spec §5.3)
    detail     text
);
CREATE INDEX IF NOT EXISTS ix_tb_mc_event_ts       ON tb_mc_event (ts DESC);
CREATE INDEX IF NOT EXISTS ix_tb_mc_event_panel_ts ON tb_mc_event (panel_no, ts DESC);
CREATE INDEX IF NOT EXISTS ix_tb_mc_event_mismatch ON tb_mc_event (ts DESC) WHERE NOT confirmed;
COMMENT ON TABLE tb_mc_event IS 'MC 1개 단위의 확정된 상태 변화. confirmed=false row가 CMD/FB 불일치 분석의 원천.';

-- ── 운전(투입) 동작 로그 — 동작 완료마다 1 row ──────────────────────
-- op_type 표준값:
--   MC_ON / MC_OFF      : 수동모드 MC 1개 투입/개방 완료
--   AUTO_STEP           : 자동모드 스텝 1개 완료 (detail: 목표/실측 용량, 소요 ms)
--   AUTO_COMPLETE       : 자동모드 계획 전체 종료
--   C_SEQ_STEP          : C부하 시퀀스 단계 (저항경로MC→SCR→직결MC, spec §8; detail: 단계·FB시각)
--   MODE_CHANGE         : Local/Remote 전환 (detail: {"from":"LOCAL","to":"REMOTE"})
--   ALL_OFF             : 전체 개방 (공통정지 포함 — detail에 사유)
CREATE TABLE IF NOT EXISTS tb_operation_event (
    id             bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    ts             timestamptz NOT NULL,     -- 동작 완료 시각
    session_id     bigint REFERENCES tb_app_session(id),
    panel_no       smallint CHECK (panel_no BETWEEN 1 AND 3),   -- NULL = 시스템 공통
    mode           text NOT NULL CHECK (mode IN ('MANUAL','AUTO','SYSTEM')),
    op_type        text NOT NULL,
    load_type      text CHECK (load_type IN ('R','L','C','MIXED')),
    phase          text CHECK (phase IN ('RN','SN','TN','3P')), -- PNL-1 개별상 구분
    target         text,                     -- mc_tag 또는 스텝 설명
    applied_r_kw   numeric(7,2),             -- 동작 직후 판넬 투입 용량 스냅샷
    applied_l_kvar numeric(7,2),
    applied_c_kvar numeric(7,2),
    result         text NOT NULL,            -- 'OK' | 'FB_TIMEOUT' | 'INTERLOCK_BLOCKED' | ...
    detail         jsonb                     -- 가변 정보 (자동 스텝 파라미터, C-seq 단계 등)
);
CREATE INDEX IF NOT EXISTS ix_tb_operation_event_ts       ON tb_operation_event (ts DESC);
CREATE INDEX IF NOT EXISTS ix_tb_operation_event_panel_ts ON tb_operation_event (panel_no, ts DESC);
CREATE INDEX IF NOT EXISTS ix_tb_operation_event_type_ts  ON tb_operation_event (op_type, ts DESC);
COMMENT ON TABLE tb_operation_event IS '투입 용량 및 동작 상태 로그. 기존 operation_history를 대체·확장.';

-- ── 알람/보호 이벤트 (발생~해제 = 1 row) ────────────────────────────
-- alarm_type 표준값: EMG | MCCB_TRIP | OVR | OCR | HT | DOOR
--                    CMD_FB_MISMATCH | COMM_LOST | FAN_FAIL | PWR_380_FAIL
CREATE TABLE IF NOT EXISTS tb_alarm_event (
    id         bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    session_id bigint REFERENCES tb_app_session(id),
    panel_no   smallint CHECK (panel_no BETWEEN 1 AND 3),   -- NULL = 시스템 공통
    alarm_type text NOT NULL,
    raised_ts  timestamptz NOT NULL,
    cleared_ts timestamptz,                  -- 해제 시 UPDATE (NULL = 활성)
    detail     text,
    CHECK (cleared_ts IS NULL OR cleared_ts >= raised_ts)
);
CREATE INDEX IF NOT EXISTS ix_tb_alarm_event_raised  ON tb_alarm_event (raised_ts DESC);
CREATE INDEX IF NOT EXISTS ix_tb_alarm_event_type    ON tb_alarm_event (alarm_type, raised_ts DESC);
CREATE INDEX IF NOT EXISTS ix_tb_alarm_event_active  ON tb_alarm_event (alarm_type) WHERE cleared_ts IS NULL;
COMMENT ON TABLE tb_alarm_event IS '알람 에피소드. CMD/FB 불일치는 tb_mc_event(confirmed=false)가 원천이고 여기엔 UI 알람 단위로 기록.';

-- ── GIMAC 1분 집계 (원본 500ms → 720:1 압축) ────────────────────────
CREATE TABLE IF NOT EXISTS tb_gimac_agg_1m (
    ts        timestamptz NOT NULL,          -- 분 경계
    unit_id   smallint    NOT NULL,
    panel_no  smallint,
    volt_avg  real, curr_avg real,
    kw_avg    real, kw_min real, kw_max real,   -- kW는 min/max 포함 (델타 차트·부하 스텝 분석)
    kvar_avg  real, kva_avg real, pf_avg real, hz_avg real,
    thd_v_avg real, thd_i_avg real,
    samples   smallint NOT NULL,             -- 집계에 포함된 샘플 수 (결손 판단용)
    PRIMARY KEY (unit_id, ts)
);
CREATE INDEX IF NOT EXISTS ix_tb_gimac_agg_1m_ts ON tb_gimac_agg_1m USING brin (ts);
COMMENT ON TABLE tb_gimac_agg_1m IS 'GIMAC 1분 집계. 앱 시작 시 최근 2h를 읽어 MeteringHistoryService 버퍼 백필.';

-- ── ISEM 1분 집계 ────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS tb_isem_agg_1m (
    ts            timestamptz NOT NULL,
    unit_id       smallint    NOT NULL,
    panel_no      smallint,
    volt_avg      real,
    curr_l1_avg   real, curr_l2_avg real, curr_l3_avg real,
    ground_ma_avg real, ground_ma_max real, -- 접지전류는 max가 보호 관점에서 중요
    kw_avg        real, kw_min real, kw_max real,
    kvar_avg      real, pf_avg real, hz_avg real,
    thd_i_avg     real, thd_v_avg real,
    samples       smallint NOT NULL,
    PRIMARY KEY (unit_id, ts)
);
CREATE INDEX IF NOT EXISTS ix_tb_isem_agg_1m_ts ON tb_isem_agg_1m USING brin (ts);

-- ====================================================================
-- (옵션) raw 테이블 — 시운전/정밀 분석 기간에만 활성화.
-- 500ms 원본은 하루 ~220만 row이므로 보존 3~7일 삭제 잡과 함께 운용할 것.
-- 컬럼은 C# GimacReading / IsemReading 전 필드를 1:1로 두면 됨.
-- CREATE TABLE IF NOT EXISTS tb_gimac_raw ( ts timestamptz, unit_id smallint, ... );
-- CREATE TABLE IF NOT EXISTS tb_isem_raw  ( ts timestamptz, unit_id smallint, ... );
-- ====================================================================

-- ====================================================================
-- 보존정책 (권장 — 앱 시작 시 1회 또는 pg_cron):
--   이벤트 테이블: 무기한 (연 수십만 row 수준으로 작음)
--   1분 집계     : 2년   DELETE FROM tb_gimac_agg_1m WHERE ts < now() - interval '2 years';
--                        DELETE FROM tb_isem_agg_1m  WHERE ts < now() - interval '2 years';
--   raw(옵션)    : 7일
-- ====================================================================

-- ====================================================================
-- (1회성) 기존 operation_history 이관 예시 — panel 문자열 형식 확인 후 실행:
-- INSERT INTO tb_operation_event (ts, panel_no, mode, op_type, target, result)
-- SELECT ts::timestamptz,
--        NULLIF(regexp_replace(COALESCE(panel,''), '\D', '', 'g'), '')::smallint,
--        'MANUAL', 'LEGACY', event, COALESCE(result, 'OK')
-- FROM operation_history;
-- ====================================================================
