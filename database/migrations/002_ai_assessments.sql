BEGIN;

CREATE TABLE IF NOT EXISTS droneops.ai_assessments (
    id uuid PRIMARY KEY,
    mission_ref varchar(80) NOT NULL,
    mission_version integer NOT NULL CHECK (mission_version > 0),
    requested_by uuid NOT NULL REFERENCES droneops.accounts(id),
    provider varchar(40) NOT NULL,
    model varchar(120) NOT NULL,
    context_hash varchar(64) NOT NULL CHECK (length(context_hash) = 64),
    context_snapshot jsonb NOT NULL,
    status varchar(20) NOT NULL CHECK (status IN ('Pending', 'Available', 'Failed')),
    result jsonb,
    error_code varchar(60),
    created_at timestamptz NOT NULL DEFAULT now(),
    completed_at timestamptz
);

CREATE INDEX IF NOT EXISTS ix_ai_assessments_mission
    ON droneops.ai_assessments(mission_ref, mission_version, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_ai_assessments_requester
    ON droneops.ai_assessments(requested_by, created_at DESC);

ALTER TABLE droneops.ai_assessments ENABLE ROW LEVEL SECURITY;
REVOKE ALL ON droneops.ai_assessments FROM PUBLIC;

COMMIT;
