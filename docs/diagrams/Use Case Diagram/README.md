# Use Case Diagram — recovered source and SRS figures

The folder was removed in commit `a72b29a` (4 November 2026). Original editable
source and five original PNG exports were recovered byte-for-byte from its parent
commit `9d2078867285a499d3a8d6cb9c112521ae9877a0`. No diagrams were redrawn and no SRS document was modified.

- [Editable draw.io source](use-case-diagram.drawio): five diagram pages.
- [Exact figures embedded in the current SRS](srs-applied/): extracted without
  re-encoding from `docs/reports/Report3_Software Requirement Specification.docx`,
  section 2.2.1 Diagram(s). These are smaller than the original PNG exports.
- The PNG files beside the draw.io file are the original exports from Git.

| SRS figure | Diagram | Exact embedded PNG |
| --- | --- | --- |
| Figure 2 | Shared Management & Access | [shared-management.png](srs-applied/shared-management.png) |
| Figure 3 | MF1 — Mission Planning & Pre-flight Validation | [planning-validation.png](srs-applied/planning-validation.png) |
| Figure 4 | MF2 — Real-Time Monitoring & Incident Management | [monitoring-incidents.png](srs-applied/monitoring-incidents.png) |
| Figure 5 | MF3 — Post-Flight Analysis & Mission History | [post-flight-history.png](srs-applied/post-flight-history.png) |
| Figure 6 | MF4 — AI-Assisted Risk Assessment & Dashboard | [ai-assessment.png](srs-applied/ai-assessment.png) |

SRS SHA-256 at extraction: `4a40231a3d4312c90c7dad5db3186210a5d1a4cb5b224a5548dc138cf6405d82`.

The root document `Report3_Software_Requirement_Specification_revised.docx`
contains the same five embedded image byte streams, with different internal
image filenames. The recovered draw.io is the historical editable source;
embedded raster images alone do not prove that every editable element still
matches the later SRS text.
