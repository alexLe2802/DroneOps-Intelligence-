BEGIN;

-- Mỗi mission chỉ được có một báo cáo tổng hợp sau bay.
-- Migration cố ý dừng nếu dữ liệu cũ đã có report trùng để tránh tự ý xóa lịch sử.
DO $$
BEGIN
    -- Sau migration 007, nhiều revision trên cùng mission là dữ liệu hợp lệ.
    -- Chỉ áp dụng ràng buộc legacy khi lifecycle table chưa tồn tại.
    IF to_regclass('"FlightCompletionEvent"') IS NULL AND EXISTS (
        SELECT 1
        FROM "PostFlightReport"
        GROUP BY "MissionId"
        HAVING COUNT(*) > 1
    ) THEN
        RAISE EXCEPTION 'Duplicate PostFlightReport rows exist. Resolve them before applying migration 006.';
    END IF;
END $$;

DO $$
BEGIN
    IF to_regclass('"FlightCompletionEvent"') IS NULL THEN
        CREATE UNIQUE INDEX IF NOT EXISTS "UX_PostFlightReport_MissionId"
            ON "PostFlightReport" ("MissionId");
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS "IX_Incident_Mission_CreatedAt"
    ON "Incident" ("MissionId", "CreatedAt" DESC);

COMMIT;
