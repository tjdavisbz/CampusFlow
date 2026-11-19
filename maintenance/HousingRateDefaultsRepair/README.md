# CF-053 housing rate metadata repair

Run `preview`, review the returned counts, then `apply` with the exact preview hash.
Runtime settings are supplied only through `CF_HOUSING_IMPORT_SETTINGS` in memory.
The tool changes exact Unreviewed rate placeholders to Standard in both saved baseline
and draft. Special rates, rooms, pending removals/moves, notes and single flags remain.
Private backups contain student information and must never be committed. It makes no
Elements assignment/billing writes and refuses active/review-blocked housing operations.
