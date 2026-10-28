# Nelson update routines as admin screens

Each data-changing SSRS routine is implemented as a separate CampusFlow feature branch and promoted independently. The existing conversion queue is `SAGU SSRS Project/Migration/admin-tool-conversion-queue.json` in the Nelson source workspace. A screen belongs under its business area in Admin, has its own permission, previews the affected records, requires confirmation, and writes through the supported Elements API or a CampusFlow domain service. Do not write to the read-only Elements replica or run the RDL's SQL update from Power BI.

`main` deploys production, so a feature branch must not be merged there before non-production verification. For each screen, deploy that branch to non-production, verify the preview against the source routine, perform a reversible test write on a designated non-production record, confirm the audit/result and re-read the record, then merge that same feature branch to `main` when approved for production. Keep database migrations and environment settings separate from the web deployment.

## First new screen: Reconcile Award Codes (CR-0136)

Branch: `codex/admin-award-code-reconciliation`.

The old routine compares `FinancialAward.Code1ID` and `ShowOnBillingStatement` with `FinancialAwardRef` and updates mismatches. The new Financial Aid screen scopes the preview to one term and at most 100 awards per run. It copies those two reference fields through the existing Elements financial-award maintenance API, preserving the other award properties. The update rechecks the preview and the API award before each write. A failure stops the batch and reports how many earlier updates completed. A second preview is needed after the reporting replica catches up.

Deployed to `app-campusflow-nonprod-001` on October 28, 2026 from `codex/admin-award-code-reconciliation` (build commit `cf84149`). The App Service is `Staging` and its Elements API points to `https://app-elm-nonprod-usc-integrationapi.azurewebsites.net`. `AwardCodeMaintenance__Enabled=true` and `AwardCodeMaintenance__ExpectedApiBaseUrl` matches that exact URL; the page's write path remains off if either setting is removed or the URL changes. The production rollout needs a separate destination review and explicit production setting. The Nelson administrator account could open the authenticated screen and its Spring 2027 preview returned no mismatched awards. The deployed binary matched the tested package and `/health-status` returned HTTP 200. All 229 solution tests passed.

A read-only call to the non-production financial-award maintenance API confirmed the live `Code1ID` and `ShowOnBillingStatement` field contract (`Yes`/`No` strings). No live award was changed: the checked terms had no mismatches. The first real write should be checked against a designated non-production test award, followed by a fresh API read and replica preview. This deployment did not change any production configuration or code deployment.
