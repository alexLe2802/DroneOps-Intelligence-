# KAN-73 Historical similarity contract

`POST /api/ai/assessments` accepts a mission UUID and a positive mission version.
The backend reloads the mission, telemetry and incidents from the caller's authorized
scope; client-supplied labels, incident counts or telemetry summaries are not trusted.

The retrieval method is `normalized-euclidean-v1`. It ranks at most five authorized
completed flights using recorded route centroid, trusted duration, mean recorded
altitude, UAV identity and distinct incident count. Numeric differences are normalized
against the eligible population range. The deterministic tie-break is mission UUID.

Every stored assessment includes the method and feature-schema version, target feature
snapshot, source mission IDs, source incident IDs, per-result feature values and
similarity scores. Missing features and fewer than five eligible sources remain explicit;
the service never substitutes missing data with zero risk or fabricated matches.

Similarity is explainable decision support, not a calibrated safety probability. It
does not alter mission approval, validation results or detector thresholds. Assessment
history is available only through `GET /api/ai/assessments/mission/{missionId}` after
the same mission-scope authorization check.
