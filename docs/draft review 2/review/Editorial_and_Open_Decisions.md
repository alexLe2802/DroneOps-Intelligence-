# Internal preparation notes

These notes are separate from the SRS, SDD and presentation.

## Section 5.4 Open Decisions and Assumptions

5.4 Open Decisions and Assumptions
Resolve these items with the corresponding flow owner and supervisor before final SRS approval. Items explicitly awaiting confirmation remain tracked until resolved.
IDTopicCurrent specification / Confirmation neededOwnerD-01Account provisioning and role scopePropose manager-created accounts; Operator accesses owned/assigned missions, Manager accesses managed operations. No public registration or separate Administrator role. Confirm reset/MFA/session rules.ThongLQ / NghiaHQD-02Management and assignment constraintsPropose active operator/UAV required at submission; preserve referenced records; prevent deactivation while flight active. Confirm scheduling conflicts and mandatory fields.NghiaHQD-03Mission states and edit/approval policyConfirmed 19 October 2026: no mission versioning. Every eligible pre-flight edit creates a Manager review request with proposed content stored separately. Approval updates the same mission; rejection preserves current content and prior Approved status. Current mission status and pending request state are separate. In Progress/Completed edit restrictions remain. Pending-request concurrency and no-self-approval details still require confirmation; stale requests must not overwrite current data.NghiaHQD-04Spatial validation semanticsConfirm coordinate/altitude reference, units, minimum route shape, boundary touch/intersection rules, inclusive/exclusive geofences and how restriction revisions invalidate validation. No real regulatory thresholds are assumed.NghiaHQD-05Telemetry and flight lifecycleConfirm supported MAVLink messages, mapping/association, sampling/load targets, timestamps, deduplication, completion-event source, reconnection and late-arrival policy. Never infer completion from disconnect alone.KhanhGNGD-06Incident policyConfirm rule values/severity/hysteresis, communication-loss interval, duplicate suppression, alert recipients/channels and incident state permissions. Proposed in-app alerts and Open/Acknowledged/Resolved workflow.KhanhGNGD-07Report and retention policyConfirm report fields/formulas, retry authorization, exports, retention periods, purge rules and historical immutability. The specification assumes web viewing and preserved evidence, no committed PDF/CSV export.DucDDAD-08AI provider and request policySelect one configured provider/model, request schema/context limits, retention, timeout, usage budget and result handling. Provider output is advisory, immutable as received; new context creates a new assessment.ThongLQD-09Dashboard analytics and mobile coverageConfirm metric formulas, date/time handling and whether Operator mobile includes planning as well as monitoring. Web screen design is proposed, not evidence of an implemented UI.DucDDA / ThongLQD-10Quality and deployment baselineApprove measurable target load/latency, authentication/availability, backup recovery and browser/device coverage in section 4.2 before acceptance testing.All flow owners

## Section 5.6 Screenshot Insertion Checklist

5.6 Screenshot Insertion Checklist
Các đường dẫn dưới đây là menu đề xuất cho web, không phải URL đã kiểm chứng. Chèn ảnh thật khi giao diện đã có; nếu dùng mockup thì ghi rõ “Mockup”. Dùng dữ liệu demo nhất quán giữa mission, telemetry, incident và report. Sau khi chèn ảnh, xóa ghi chú biên tập tiếng Việt khỏi bản nộp tiếng Anh.
SlotScreen / Web navigationReview 2 layout reference / acceptance evidenceSHOT-01SCR-01 — Login page before authenticationLabelled design mockup included in section 3. Runtime screenshot and acceptance evidence remain to be captured.SHOT-02SCR-02 — DashboardLabelled design mockup included in section 3. Runtime screenshot and acceptance evidence remain to be captured.SHOT-03SCR-03 — Management > UAVsLabelled design mockup included in section 3. Runtime screenshot and acceptance evidence remain to be captured.SHOT-04SCR-04 — Management > OperatorsLabelled design mockup included in section 3. Runtime screenshot and acceptance evidence remain to be captured.SHOT-05SCR-05 — Management > Operational Areas & RestrictionsLabelled design mockup included in section 3. Runtime screenshot and acceptance evidence remain to be captured.SHOT-06SCR-06 — MissionsLabelled design mockup included in section 3. Runtime screenshot and acceptance evidence remain to be captured.SHOT-07SCR-07 — Missions > New Mission or Details > EditLabelled design mockup included in section 3. Runtime screenshot and acceptance evidence remain to be captured.SHOT-08SCR-08 — Missions > select missionLabelled design mockup included in section 3. Runtime screenshot and acceptance evidence remain to be captured.SHOT-09SCR-09 — Approvals > pending missionLabelled design mockup included in section 3. Runtime screenshot and acceptance evidence remain to be captured.SHOT-10SCR-10 — Live Monitoring > select missionLabelled design mockup included in section 3. Runtime screenshot and acceptance evidence remain to be captured.SHOT-11SCR-11 — IncidentsLabelled design mockup included in section 3. Runtime screenshot and acceptance evidence remain to be captured.SHOT-12SCR-12 — Incidents > select incidentLabelled design mockup included in section 3. Runtime screenshot and acceptance evidence remain to be captured.SHOT-13SCR-13 — Mission HistoryLabelled design mockup included in section 3. Runtime screenshot and acceptance evidence remain to be captured.SHOT-14SCR-14 — Mission History > select flightLabelled design mockup included in section 3. Runtime screenshot and acceptance evidence remain to be captured.SHOT-15SCR-15 — Mission Details or Live Monitoring > AI AssessmentLabelled design mockup included in section 3. Runtime screenshot and acceptance evidence remain to be captured.SHOT-16SCR-16 — Dashboard > AnalyticsLabelled design mockup included in section 3. Runtime screenshot and acceptance evidence remain to be captured.



## Section Review 2 baseline notes and requirement clarification

Review 2 baseline notes and requirement clarification
This draft follows the supplied Report 3 section structure. The registration form is preserved byte-for-byte. The mission-update decision of 19 October supersedes earlier mission-version requirements in this SRS; it does not edit or certify a change to the signed registration form. The final SRS baseline requires team/supervisor confirmation of the open items below.
IDClarification / acceptance conditionMF1-CR-01One current mission ID. Every accepted pre-flight update is reviewed, regardless of prior approval. Proposed content is staged separately. Approval atomically applies all metadata and ordered waypoints; rejection preserves current content and prior Approved status.MF1-CR-02Reject malformed input before staging. Accepted updates remain visible in review even when validation fails/is incomplete; current passing evidence is required for approval. Reject requires reason; stale or superseded decisions return conflict.MF1-CR-03No mission-version API, version ledger or expectedVersion. Review request ID links proposed content, validation, decision and history. Multiple pending requests, self-approval and editing permissions beyond Operator scope require explicit confirmation.MF2-CR-01Show source timestamp, receive timestamp, units and freshness separately. Unknown/null values display unavailable. Reconnect reloads authorized latest snapshot and durable alerts; do not promise lossless SSE replay without persisted cursor support.MF2-CR-02Communication-loss detection requires a timer independent of new packets. Freeze incident evidence; deduplicate one abnormal episode; acknowledgement and manager-only resolution retain actor/time/reason.AI-SCOPE-01Registration requires Top-k similarity evidence (k-NN/vector retrieval); current SRS/source emphasize external AI API advice. This is an unresolved scope discrepancy, not an approved deletion. Until supervisors decide, retain retrieval as a required gap and do not claim it is implemented.GATE-01Mobile scope, quantitative latency/load targets, repeat-flight identity and retention remain confirmation items. Review 2 SRS finalization requires explicit owners and decisions; draft values are not measured results.RequirementDesignPlanned acceptance evidenceUC-05–12SDD 3.1; planning ERD; mission/update states; sequences 1–3TC-M01–M12; Demo A–CUC-13–16SDD 3.2; operations ERD; ingestion/incident sequencesTC-T01–T08 and TC-I01–I06; Demo DUC-17–18 / UC-22SDD 3.3; report relationshipsTC-H01–H04; history/report sampleUC-19–21SDD 3.4; AI sequence; registered-scope noteTC-A01–A05; advisory/provider failure; retrieval decision


## Source/methodology

SourceUse in this SRSSRC-01 — Report1_Project Introduction.docxTen feature definitions, target users and scope exclusions. FE-09, product vision and current user direction establish external AI API scope.SRC-02 — Mainflows, Actors.docxFour mainflow names and participation.SRC-03 — Existing context and swimlane .drawio / PNG filesSystem boundary and four operational workflows, reused without altering the user-edited sources.SRC-04 — Report2_Project_Management_Plan.docx and Project ScheduleFour-member ownership and council-aligned requirements/design delivery; these do not prove implementation is complete.SRC-05 — Supplied Report 3 templateSection structure, actor/UC tables, screen flow/authorization, non-screen functions, ERD, function details, interfaces, quality attributes and appendices.

## Source/methodology

A requirement marked D-xx depends on the decision register in section 5.4. Items identified as requiring confirmation remain subject to the decision register. “Shall” expresses the intended software behavior of this specification. Screen names and paths are proposed navigation labels, not verified application URLs. No application source or running UI was supplied in the project folder.

## Editorial paragraph

The following five views describe the same system boundary. Shared management is not a fifth mainflow. Actor associations express participation, not unrestricted write permissions; section 3.1.3 controls individual actions. UC-11 is included by UC-08. Other runtime dependencies are described in the function specifications rather than misrepresented as UML include relations.

## Editorial paragraph

These four existing user-edited swimlanes are reused as the baseline business workflows. Internal service lanes describe processing responsibility; they do not add external actors. Proposed incident acknowledgement/resolution and lifecycle details below extend these high-level flows for review.

## Editorial paragraph

Layout is illustrated above; replace with a real acceptance-build capture when that screen is integrated.

## Editorial paragraph

Layout is illustrated above; replace with a real acceptance-build capture when that screen is integrated.

## Editorial paragraph

Layout is illustrated above; replace with a real acceptance-build capture when that screen is integrated.

## Editorial paragraph

Layout is illustrated above; replace with a real acceptance-build capture when that screen is integrated.

## Editorial paragraph

Layout is illustrated above; replace with a real acceptance-build capture when that screen is integrated.

## Editorial paragraph

Layout is illustrated above; replace with a real acceptance-build capture when that screen is integrated.

## Editorial paragraph

Layout is illustrated above; replace with a real acceptance-build capture when that screen is integrated.

## Editorial paragraph

Layout is illustrated above; replace with a real acceptance-build capture when that screen is integrated.

## Editorial paragraph

Input validation detail: latitude must be within [-90, 90] degrees and longitude within [-180, 180] degrees; waypoint ordering must be unambiguous. The approved coordinate/altitude reference, valid polygon rules and treatment of boundary intersections remain D-04.

## Editorial paragraph

No application screenshot is required for the raw gateway. Verify ingestion and mapping through integration-test evidence and the visible Live Monitoring screen (SHOT-10). Do not insert API keys, provider tokens or connection secrets into this report.

## Editorial paragraph

Layout is illustrated above; replace with a real acceptance-build capture when that screen is integrated.

## Editorial paragraph

Layout is illustrated above; replace with a real acceptance-build capture when that screen is integrated.

## Editorial paragraph

Layout is illustrated above; replace with a real acceptance-build capture when that screen is integrated.

## Editorial paragraph

Layout is illustrated above; replace with a real acceptance-build capture when that screen is integrated.

## Editorial paragraph

Layout is illustrated above; replace with a real acceptance-build capture when that screen is integrated.

## Editorial paragraph

Layout is illustrated above; replace with a real acceptance-build capture when that screen is integrated.

## Editorial paragraph

Layout is illustrated above; replace with a real acceptance-build capture when that screen is integrated.

## Editorial paragraph

Layout is illustrated above; replace with a real acceptance-build capture when that screen is integrated.

## Editorial paragraph

The following numeric targets are proposed acceptance candidates (D-10), not established commitments from Report 1. Confirm them against the prototype, team capacity and deployment environment before baselining. Test profile P-01 proposes 5 simulated UAVs at 1 normalized sample/second each, 10 concurrent Web sessions, 100 stored flights with 10,000 samples each, and up to 50 waypoints checked against 20 configured polygons of at most 100 vertices each, on an agreed test deployment with external API latency measured separately.

## Section 4. Review decisions, limitations and verification

4. Review decisions, limitations and verification
New logical database/class/sequence diagrams are design proposals where marked. Existing use-case/context/screen-flow and unaffected workflows are retained. Complete source references: be/DroneOps/DroneOps.API/Controllers, Application/Services and Interfaces, Domain/Entities, Persistence/Data/AppDbContext.cs, database/migrations, fe/src/app/portal and API proxy, .github/workflows/ci.yml. This document does not execute SQL, deploy changes or certify test results.

## SDD editorial

Document status: DRAFT FOR TEAM AND SUPERVISOR REVIEW. Requirements, target designs and observed source capabilities are distinguished; no test pass or production deployment is implied.