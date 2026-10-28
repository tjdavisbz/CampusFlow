# Nelson update routines as admin screens

Each data-changing SSRS routine is implemented as a separate CampusFlow feature branch and promoted independently. The existing conversion queue is `SAGU SSRS Project/Migration/admin-tool-conversion-queue.json` in the Nelson source workspace. A screen belongs under its business area in Admin, has its own permission, previews the affected records, requires confirmation, and writes through the supported Elements API or a CampusFlow domain service. Do not write to the read-only Elements replica or run the RDL's SQL update from Power BI.

`main` deploys production, so a feature branch must not be merged there before non-production verification. For each screen, deploy that branch to non-production, verify the preview against the source routine, perform a reversible test write on a designated non-production record, confirm the audit/result and re-read the record, then merge that same feature branch to `main` when approved for production. Keep database migrations and environment settings separate from the web deployment.

## First new screen: Reconcile Award Codes (CR-0136)

Branch: `codex/admin-award-code-reconciliation`.

The old routine compares `FinancialAward.Code1ID` and `ShowOnBillingStatement` with `FinancialAwardRef` and updates mismatches. The new Financial Aid screen scopes the preview to one term and at most 100 awards per run. It copies those two reference fields through the existing Elements financial-award maintenance API, preserving the other award properties. The update rechecks the preview and the API award before each write. A failure stops the batch and reports how many earlier updates completed. A second preview is needed after the reporting replica catches up.

Writes are disabled by default. Before a non-production test, configure a non-production Elements API and credentials in `StudentInformationSystems:Providers:ThesisElements:Api`, and independently verify the API destination is non-production. Then set `AwardCodeMaintenance:Enabled=true` and `AwardCodeMaintenance:ExpectedApiBaseUrl` to the exact configured API base URL in that non-production App Service. Grant `CampusFlow.Admin.FinancialAid.ReconcileAwardCodes` only to designated testers. Do not enable the write flag while the default production Elements API URL is in use. The production rollout needs a separate destination review and explicit environment setting.

No live Elements write or non-production deployment has been performed from this branch yet. Build verification does not establish that the Elements API accepts these fields or that the non-production API is available.
