using Microsoft.EntityFrameworkCore;
using Volo.Abp.AuditLogging.EntityFrameworkCore;
using Volo.Abp.BackgroundJobs.EntityFrameworkCore;
using Volo.Abp.BlobStoring.Database.EntityFrameworkCore;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Volo.Abp.FeatureManagement.EntityFrameworkCore;
using Volo.Abp.Identity;
using Volo.Abp.Identity.EntityFrameworkCore;
using Volo.Abp.PermissionManagement.EntityFrameworkCore;
using Volo.Abp.SettingManagement.EntityFrameworkCore;
using Volo.Abp.OpenIddict.EntityFrameworkCore;
using Volo.Abp.TenantManagement;
using Volo.Abp.TenantManagement.EntityFrameworkCore;
using CampusFlow.Students;
using CampusFlow.BillApprovals;
using CampusFlow.CourseSelections;
using CampusFlow.Housing;

namespace CampusFlow.EntityFrameworkCore;

[ReplaceDbContext(typeof(IIdentityDbContext))]
[ReplaceDbContext(typeof(ITenantManagementDbContext))]
[ConnectionStringName("Default")]
public class CampusFlowDbContext :
    AbpDbContext<CampusFlowDbContext>,
    ITenantManagementDbContext,
    IIdentityDbContext
{
    public DbSet<StudentProfile> StudentProfiles { get; set; }
    public DbSet<AgreementTemplate> AgreementTemplates { get; set; }
    public DbSet<PaymentPlanPolicy> PaymentPlanPolicies { get; set; }
    public DbSet<BillApproval> BillApprovals { get; set; }
    public DbSet<BillApprovalArtifact> BillApprovalArtifacts { get; set; }
    public DbSet<CourseSelectionPolicy> CourseSelectionPolicies { get; set; }
    public DbSet<AdvisorAssignment> AdvisorAssignments { get; set; }
    public DbSet<CourseReview> CourseReviews { get; set; }
    public DbSet<CourseReviewSubmission> CourseReviewSubmissions { get; set; }
    public DbSet<CourseSelectionOperation> CourseSelectionOperations { get; set; }
    public DbSet<CourseSectionAttendanceTypeMapping> CourseSectionAttendanceTypeMappings { get; set; }
    public DbSet<MealPlanConfiguration> MealPlanConfigurations { get; set; }
    public DbSet<StudentHousingSelection> StudentHousingSelections { get; set; }

    #region Entities from the modules

    /* Notice: We only implemented IIdentityProDbContext and ISaasDbContext
     * and replaced them for this DbContext. This allows you to perform JOIN
     * queries for the entities of these modules over the repositories easily. You
     * typically don't need that for other modules. But, if you need, you can
     * implement the DbContext interface of the needed module and use ReplaceDbContext
     * attribute just like IIdentityProDbContext and ISaasDbContext.
     *
     * More info: Replacing a DbContext of a module ensures that the related module
     * uses this DbContext on runtime. Otherwise, it will use its own DbContext class.
     */

    // Identity
    public DbSet<IdentityUser> Users { get; set; }
    public DbSet<IdentityRole> Roles { get; set; }
    public DbSet<IdentityClaimType> ClaimTypes { get; set; }
    public DbSet<OrganizationUnit> OrganizationUnits { get; set; }
    public DbSet<IdentitySecurityLog> SecurityLogs { get; set; }
    public DbSet<IdentityLinkUser> LinkUsers { get; set; }
    public DbSet<IdentityUserDelegation> UserDelegations { get; set; }
    public DbSet<IdentitySession> Sessions { get; set; }

    // Tenant Management
    public DbSet<Tenant> Tenants { get; set; }
    public DbSet<TenantConnectionString> TenantConnectionStrings { get; set; }

    #endregion

    public CampusFlowDbContext(DbContextOptions<CampusFlowDbContext> options)
        : base(options)
    {

    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        /* Include modules to your migration db context */

        builder.ConfigurePermissionManagement();
        builder.ConfigureSettingManagement();
        builder.ConfigureBackgroundJobs();
        builder.ConfigureAuditLogging();
        builder.ConfigureFeatureManagement();
        builder.ConfigureIdentity();
        builder.ConfigureOpenIddict();
        builder.ConfigureTenantManagement();
        builder.ConfigureBlobStoring();

        /* Configure your own tables/entities inside here */

        builder.Entity<StudentProfile>(b =>
        {
            b.ToTable(CampusFlowConsts.DbTablePrefix + "StudentProfiles", CampusFlowConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.ExternalStudentId).IsRequired().HasMaxLength(64);
            b.Property(x => x.StudentId).IsRequired().HasMaxLength(64);
            b.Property(x => x.Email).IsRequired().HasMaxLength(256);
            b.Property(x => x.FirstName).IsRequired().HasMaxLength(128);
            b.Property(x => x.PreferredName).HasMaxLength(128);
            b.Property(x => x.LastName).IsRequired().HasMaxLength(128);
            b.HasOne<IdentityUser>()
                .WithOne()
                .HasForeignKey<StudentProfile>(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => x.UserId).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.Provider, x.ExternalStudentId }).IsUnique();
        });

        builder.Entity<AgreementTemplate>(b =>
        {
            b.ToTable(CampusFlowConsts.DbTablePrefix + "AgreementTemplates", CampusFlowConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Name).IsRequired().HasMaxLength(160);
            b.Property(x => x.ContentHtml).IsRequired();
            b.Property(x => x.AllowedMergeFieldsJson).IsRequired();
            b.HasIndex(x => new { x.TenantId, x.Name, x.Version }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.IsPublished, x.EffectiveFrom });
        });

        builder.Entity<PaymentPlanPolicy>(b =>
        {
            b.ToTable(CampusFlowConsts.DbTablePrefix + "PaymentPlanPolicies", CampusFlowConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Name).IsRequired().HasMaxLength(160);
            b.Property(x => x.EnrollmentFee).HasPrecision(18, 2);
            b.Property(x => x.PartTimeBalanceDivisor).HasPrecision(18, 2);
            b.Property(x => x.ResidentialMinimumPayment).HasPrecision(18, 2);
            b.Property(x => x.StandardMinimumPayment).HasPrecision(18, 2);
            b.HasIndex(x => new { x.TenantId, x.Name, x.Version }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.IsPublished, x.EffectiveFrom });
        });

        builder.Entity<BillApproval>(b =>
        {
            b.ToTable(CampusFlowConsts.DbTablePrefix + "BillApprovals", CampusFlowConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.ExternalStudentId).IsRequired().HasMaxLength(64);
            b.Property(x => x.StudentId).IsRequired().HasMaxLength(64);
            b.Property(x => x.ExternalTermId).IsRequired().HasMaxLength(64);
            b.Property(x => x.TermCode).IsRequired().HasMaxLength(32);
            b.Property(x => x.TermName).IsRequired().HasMaxLength(160);
            b.Property(x => x.ChargesTotal).HasPrecision(18, 2);
            b.Property(x => x.CreditsTotal).HasPrecision(18, 2);
            b.Property(x => x.AnticipatedAidTotal).HasPrecision(18, 2);
            b.Property(x => x.RemainingBalance).HasPrecision(18, 2);
            b.Property(x => x.PaymentPlanFee).HasPrecision(18, 2);
            b.Property(x => x.SourceIp).HasMaxLength(64);
            b.Property(x => x.UserAgent).HasMaxLength(1024);
            b.Property(x => x.ReviewSnapshotJson).IsRequired();
            b.HasIndex(x => new { x.TenantId, x.UserId, x.ExternalTermId }).IsUnique();
            b.HasOne<StudentProfile>().WithMany().HasForeignKey(x => x.StudentProfileId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<AgreementTemplate>().WithMany().HasForeignKey(x => x.AgreementTemplateId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<BillApprovalArtifact>(b =>
        {
            b.ToTable(CampusFlowConsts.DbTablePrefix + "BillApprovalArtifacts", CampusFlowConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.PdfFileName).HasMaxLength(256);
            b.Property(x => x.PdfSha256).HasMaxLength(64);
            b.Property(x => x.PdfBlobName).HasMaxLength(512);
            b.Property(x => x.ElementsDocumentTrackingId).HasMaxLength(64);
            b.Property(x => x.LastError).HasMaxLength(4000);
            b.HasIndex(x => x.BillApprovalId).IsUnique();
            b.HasOne<BillApproval>().WithOne().HasForeignKey<BillApprovalArtifact>(x => x.BillApprovalId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<CourseSelectionPolicy>(b =>
        {
            b.ToTable(CampusFlowConsts.DbTablePrefix + "CourseSelectionPolicies", CampusFlowConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Name).IsRequired().HasMaxLength(160);
            b.Property(x => x.AttendanceTypeMappingsJson).IsRequired();
            b.Property(x => x.EligibleTermRulesJson).IsRequired();
            b.HasIndex(x => new { x.TenantId, x.Name, x.Version }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.IsPublished, x.EffectiveFrom });
        });

        builder.Entity<AdvisorAssignment>(b =>
        {
            b.ToTable(CampusFlowConsts.DbTablePrefix + "AdvisorAssignments", CampusFlowConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.AttendanceType).IsRequired().HasMaxLength(160);
            b.Property(x => x.ExternalAdvisorId).IsRequired().HasMaxLength(64);
            b.Property(x => x.AdvisorEmail).IsRequired().HasMaxLength(256);
            b.Property(x => x.AdvisorDisplayName).IsRequired().HasMaxLength(256);
            b.HasIndex(x => new { x.TenantId, x.AttendanceType, x.EffectiveFrom });
            b.HasIndex(x => new { x.TenantId, x.AdvisorEmail, x.IsActive });
        });

        builder.Entity<CourseSectionAttendanceTypeMapping>(b =>
        {
            b.ToTable(CampusFlowConsts.DbTablePrefix + "CourseSectionAttendanceTypeMappings", CampusFlowConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.AttendanceType).IsRequired().HasMaxLength(160);
            b.HasIndex(x => new { x.TenantId, x.SectionStart, x.SectionEnd, x.AttendanceType });
            b.HasIndex(x => new { x.TenantId, x.IsActive, x.EffectiveFrom });
        });

        builder.Entity<CourseReview>(b =>
        {
            b.ToTable(CampusFlowConsts.DbTablePrefix + "CourseReviews", CampusFlowConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.ExternalStudentId).IsRequired().HasMaxLength(64);
            b.Property(x => x.ExternalTermId).IsRequired().HasMaxLength(64);
            b.Property(x => x.ExternalCourseOfferingId).IsRequired().HasMaxLength(64);
            b.Property(x => x.ExternalCourseRegistrationId).IsRequired().HasMaxLength(64);
            b.Property(x => x.AttendanceType).IsRequired().HasMaxLength(160);
            b.Property(x => x.CourseSnapshotJson).IsRequired();
            b.Property(x => x.ExternalAdvisorId).HasMaxLength(64);
            b.Property(x => x.AdvisorEmail).HasMaxLength(256);
            b.Property(x => x.AdvisorComment).HasMaxLength(4000);
            b.Property(x => x.LastRemovalError).HasMaxLength(4000);
            b.HasOne<StudentProfile>().WithMany().HasForeignKey(x => x.StudentProfileId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.TenantId, x.ExternalCourseRegistrationId }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.ExternalAdvisorId, x.ExternalTermId, x.NeedsReview });
            b.HasIndex(x => new { x.TenantId, x.StudentProfileId, x.ExternalTermId, x.NeedsReview });
        });

        builder.Entity<CourseReviewSubmission>(b =>
        {
            b.ToTable(CampusFlowConsts.DbTablePrefix + "CourseReviewSubmissions", CampusFlowConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.ExternalTermId).IsRequired().HasMaxLength(64);
            b.Property(x => x.AdvisorEmail).IsRequired().HasMaxLength(256);
            b.Property(x => x.OverallComment).HasMaxLength(4000);
            b.Property(x => x.DecisionsSnapshotJson).IsRequired();
            b.Property(x => x.LastEmailError).HasMaxLength(4000);
            b.HasOne<StudentProfile>().WithMany().HasForeignKey(x => x.StudentProfileId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.TenantId, x.StudentProfileId, x.ExternalTermId, x.SubmittedAt });
        });

        builder.Entity<CourseSelectionOperation>(b =>
        {
            b.ToTable(CampusFlowConsts.DbTablePrefix + "CourseSelectionOperations", CampusFlowConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.ExternalStudentId).IsRequired().HasMaxLength(64);
            b.Property(x => x.ExternalTermId).IsRequired().HasMaxLength(64);
            b.Property(x => x.ExternalCourseOfferingId).IsRequired().HasMaxLength(64);
            b.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(128);
            b.Property(x => x.CourseSnapshotJson).IsRequired();
            b.Property(x => x.ExternalCourseRegistrationId).HasMaxLength(64);
            b.Property(x => x.LastError).HasMaxLength(4000);
            b.HasOne<StudentProfile>().WithMany().HasForeignKey(x => x.StudentProfileId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.Status, x.LastAttemptAt });
        });

        builder.Entity<MealPlanConfiguration>(b =>
        {
            b.ToTable(CampusFlowConsts.DbTablePrefix + "MealPlanConfigurations", CampusFlowConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.ExternalMealPlanName).IsRequired().HasMaxLength(160);
            b.Property(x => x.DisplayName).IsRequired().HasMaxLength(160);
            b.Property(x => x.Description).IsRequired().HasMaxLength(2000);
            b.Property(x => x.HousingChoicesJson).IsRequired();
            b.Property(x => x.EligibleAttendanceTypesJson).IsRequired();
            b.Property(x => x.DisplayPrice).HasPrecision(18, 2);
            b.HasIndex(x => new { x.TenantId, x.ExternalMealPlanName }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.IsActive, x.SortOrder });
        });

        builder.Entity<StudentHousingSelection>(b =>
        {
            b.ToTable(CampusFlowConsts.DbTablePrefix + "StudentHousingSelections", CampusFlowConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.ExternalStudentId).IsRequired().HasMaxLength(64);
            b.Property(x => x.TermName).IsRequired().HasMaxLength(160);
            b.Property(x => x.MealPlanName).IsRequired().HasMaxLength(160);
            b.Property(x => x.LastSyncError).HasMaxLength(4000);
            b.HasOne<StudentProfile>().WithMany().HasForeignKey(x => x.StudentProfileId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.TenantId, x.StudentProfileId, x.TermName }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.SyncedToStudentInformationSystem, x.SubmittedAt });
        });

        //builder.Entity<YourEntity>(b =>
        //{
        //    b.ToTable(CampusFlowConsts.DbTablePrefix + "YourEntities", CampusFlowConsts.DbSchema);
        //    b.ConfigureByConvention(); //auto configure for the base class props
        //    //...
        //});
    }
}
