START TRANSACTION;
DROP INDEX "IX_AppBillApprovals_TenantId_UserId_ExternalTermId";

CREATE UNIQUE INDEX "IX_AppBillApprovals_TenantId_UserId_ExternalTermId" ON "AppBillApprovals" ("TenantId", "UserId", "ExternalTermId") WHERE "IsDeleted" = false;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261111232327_AllowBillReapprovalAfterReset', '10.0.9');

COMMIT;

