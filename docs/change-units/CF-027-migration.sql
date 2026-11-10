START TRANSACTION;
CREATE TABLE "AppMealPlanTaxCharges" (
    "Id" uuid NOT NULL,
    "TenantId" uuid,
    "StudentUid" integer NOT NULL,
    "TermId" integer NOT NULL,
    "AssignmentId" integer NOT NULL,
    "MealPlanId" integer NOT NULL,
    "BaseAmount" numeric(18,2) NOT NULL,
    "TaxAmount" numeric(18,2) NOT NULL,
    "Reference" character varying(80) NOT NULL,
    "State" character varying(40) NOT NULL,
    "ExternalEntryId" character varying(80),
    "ExtraProperties" text NOT NULL,
    "ConcurrencyStamp" character varying(40) NOT NULL,
    "CreationTime" timestamp without time zone NOT NULL,
    "CreatorId" uuid,
    "LastModificationTime" timestamp without time zone,
    "LastModifierId" uuid,
    CONSTRAINT "PK_AppMealPlanTaxCharges" PRIMARY KEY ("Id")
);

CREATE UNIQUE INDEX "IX_AppMealPlanTaxCharges_TenantId_AssignmentId" ON "AppMealPlanTaxCharges" ("TenantId", "AssignmentId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261110041612_AddMealPlanTaxCharges', '10.0.9');

COMMIT;

