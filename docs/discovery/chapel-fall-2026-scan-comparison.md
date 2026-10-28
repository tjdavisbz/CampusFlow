# Fall 2026 chapel scan comparison (local, read only)

The supplied schedule contains 53 Fall 2026 events (term 268), and the existing attendance summary contains 1,000 rows per event. The CBORD extract contains scans through October 23, 2026, so it cannot validate later events. The scan rows have not been matched to the 1,000-student attendance roster; these are **directional counts**, not an exact student-by-student reconciliation. No database data was changed.

For each event, scans were grouped by student ID and distinct campus-local timestamp. The legacy early, late, and checkout *time* windows were applied to both `Chapel Attendance` and `HCC Attendance`; the checkout flag was ignored, as in the supplied current procedure. The replacement matcher requires a distinct later timestamp for checkout and quarantines a one-timestamp overlap rather than silently crediting it.

| Event (local) | Legacy present | Scan-derived present | Key observation |
| --- | ---: | ---: | --- |
| Aug 26, 10:15 a.m. | 698 | 8 | 687 scanned IDs had an early scan but no qualifying later checkout timestamp in the supplied extract. |
| Aug 26, 7:00 p.m. | 630 | 34 | 588 scanned IDs had an early scan but no qualifying later checkout timestamp. |
| Aug 27, 9:30 a.m. | 627 | 28 | 593 early-scan-only IDs. |
| Aug 28, 10:15 a.m. | 627 | 8 | 609 early-scan-only IDs. |
| Sep 2, 10:15 a.m. | 599 | 573 | Counts are closer, but still not reconciled by student or by manual adjustments. |
| Sep 4, 10:15 a.m. | 586 | 40 | Only 94 IDs have scans in the event window in the supplied extract, mostly `HCC Attendance`. |
| Sep 29, 9:30 a.m. | 991 | 359 | This is a `DormDevo` event; the legacy population procedure can premark eligible students present independently of CBORD scans. |
| Oct 1, 9:30 a.m. | 464 | 14 | 438 early-scan-only IDs. |
| Oct 21, 10:15 a.m. | 541 | 529 | Recent sample is comparatively close, but not identical. |
| Oct 22, 9:30 a.m. | 453 | 442 | Recent sample is comparatively close, but not identical. |
| Oct 23, 10:15 a.m. | 476 | 473 | Recent sample is comparatively close, but not identical. |

Some future Tuesday `DormDevo` events are already marked present for about 991 of the 1,000 students while ordinary future events remain pending. That confirms the attendance table contains decisions made by the population/excuse process before any scan arrives. The extract also lacks enough qualifying checkout timestamps to reproduce many older non-`DormDevo` present counts. This does **not** prove why those legacy rows are present: the source scan table may differ from the supplied extract, and staff corrections or other procedures may have contributed.

## Migration decision

1. Migrate historical `ChapelAttendance` decisions as authoritative records. Do not rebuild them from this CBORD extract.
2. Use new CBORD files prospectively, after validating the student-ID mapping, scheduled-event roster, and a few recent event decisions against row-level legacy results. Keep manual staff changes and recurring/bonus excuses from being overwritten by a retry.
3. Preserve `DormDevo` population rules separately from scan matching. Do not interpret its high present count as proof of scan attendance.
4. Keep remote SFTP deletion disabled until an import is durable, reconciled, and archived.

The local scan parser and matcher are tested, but neither is connected to an attendance-writing pipeline. No import or deployment has occurred.

## October 23 student-matched reconciliation

The user ran `chapel-recent-reconciliation.sql` against the database's retained raw scans. All 474 roster students with a distinct, chronological early-and-exit scan pair are marked Present in the legacy attendance table. Two other students are marked Present without such a pair; their reason is not identifiable from the aggregate result. Six Absent rows have a qualifying late pair and a `Late` comment; 15 Absent rows have an early scan only and a `Scan In Only` comment. Four more Absent/`Late` rows have an overlapping scan timestamp but not a distinct later exit, so the safer replacement matcher will quarantine them for review. The remaining Absent and Excused rows have no qualifying pair (apart from one Excused early-scan-only row). No roster row lacks a StudentID.

The database's retained scans produce 474 Present candidates, one more than the separately supplied CSV extract (473). This confirms the two scan sources are not byte-identical for this event. It does not change the decision to migrate the existing attendance table as authoritative history.

The domain record now distinguishes a new scheduled Pending row from legacy history, and accepts one CBORD decision idempotently. It refuses to overwrite legacy, staff, or conflicting prior import decisions. This safety rule is tested locally; no attendance-writing pipeline is enabled yet.
