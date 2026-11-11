START TRANSACTION;
DROP INDEX "IX_AppStudentHousingSelections_TenantId_StudentProfileId_TermN~";

CREATE UNIQUE INDEX "IX_AppStudentHousingSelections_TenantId_StudentProfileId_TermN~" ON "AppStudentHousingSelections" ("TenantId", "StudentProfileId", "TermName") WHERE "IsDeleted" = false;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261111022402_AllowMealPlanReselectionAfterReset', '10.0.9');

COMMIT;

