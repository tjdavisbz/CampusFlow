using System;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Uow;
using Volo.Abp.AuditLogging.EntityFrameworkCore;
using Volo.Abp.BackgroundJobs.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.PostgreSql;
using Volo.Abp.FeatureManagement.EntityFrameworkCore;
using Volo.Abp.Identity.EntityFrameworkCore;
using Volo.Abp.OpenIddict.EntityFrameworkCore;
using Volo.Abp.Modularity;
using Volo.Abp.PermissionManagement.EntityFrameworkCore;
using Volo.Abp.SettingManagement.EntityFrameworkCore;
using Volo.Abp.BlobStoring.Database.EntityFrameworkCore;
using Volo.Abp.TenantManagement.EntityFrameworkCore;
using Volo.Abp.Studio;
using CampusFlow.StudentInformationSystems;

namespace CampusFlow.EntityFrameworkCore;

[DependsOn(
    typeof(CampusFlowDomainModule),
    typeof(AbpPermissionManagementEntityFrameworkCoreModule),
    typeof(AbpSettingManagementEntityFrameworkCoreModule),
    typeof(AbpEntityFrameworkCorePostgreSqlModule),
    typeof(AbpBackgroundJobsEntityFrameworkCoreModule),
    typeof(AbpAuditLoggingEntityFrameworkCoreModule),
    typeof(AbpFeatureManagementEntityFrameworkCoreModule),
    typeof(AbpIdentityEntityFrameworkCoreModule),
    typeof(AbpOpenIddictEntityFrameworkCoreModule),
    typeof(AbpTenantManagementEntityFrameworkCoreModule),
    typeof(BlobStoringDatabaseEntityFrameworkCoreModule)
    )]
public class CampusFlowEntityFrameworkCoreModule : AbpModule
{
    public override void PreConfigureServices(ServiceConfigurationContext context)
    {
        // https://www.npgsql.org/efcore/release-notes/6.0.html#opting-out-of-the-new-timestamp-mapping-logic
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

        CampusFlowEfCoreEntityExtensionMappings.Configure();
    }

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddTransient<
            IStudentInformationSystemStudentLookup,
            ThesisElementsStudentLookup>();
        context.Services.AddTransient<
            IStudentInformationSystemAdvisorLookup,
            ThesisElementsAdvisorLookup>();
        context.Services.AddTransient<
            IStudentInformationSystemTermLookup,
            ThesisElementsTermLookup>();
        context.Services.AddTransient<
            IStudentInformationSystemDegreeAuditLookup,
            ThesisElementsDegreeAuditLookup>();
        context.Services.AddTransient<
            IStudentInformationSystemBillingLookup,
            ThesisElementsBillingLookup>();
        context.Services.AddTransient<
            IStudentInformationSystemScheduleLookup,
            ThesisElementsScheduleLookup>();
        context.Services.AddTransient<
            IStudentInformationSystemCourseSelectionLookup,
            ThesisElementsCourseSelectionLookup>();
        context.Services.AddTransient<
            IStudentInformationSystemCourseRegistrationService,
            ThesisElementsCourseRegistrationService>();
        context.Services.AddTransient<
            IStudentInformationSystemFinancialAidLookup,
            ThesisElementsFinancialAidLookup>();
        context.Services.AddTransient<
            IStudentInformationSystemFinancialAidDecisionService,
            ThesisElementsFinancialAidDecisionService>();
        context.Services.AddTransient<
            IStudentInformationSystemPaymentPlanLookup,
            ThesisElementsPaymentPlanLookup>();
        context.Services.AddTransient<
            IStudentInformationSystemDocumentTrackingService,
            ThesisElementsDocumentTrackingService>();
        context.Services.AddTransient<
            IStudentInformationSystemMealPlanService,
            ThesisElementsMealPlanService>();
        context.Services.AddTransient<
            IStudentInformationSystemPaymentPostingService,
            ThesisElementsPaymentPostingService>();
        context.Services.AddMemoryCache();

        context.Services.AddAbpDbContext<CampusFlowDbContext>(options =>
        {
                /* Remove "includeAllEntities: true" to create
                 * default repositories only for aggregate roots */
            options.AddDefaultRepositories(includeAllEntities: true);
        });

        if (AbpStudioAnalyzeHelper.IsInAnalyzeMode)
        {
            return;
        }

        Configure<AbpDbContextOptions>(options =>
        {
            /* The main point to change your DBMS.
             * See also CampusFlowDbContextFactory for EF Core tooling. */

            options.UseNpgsql();

        });
        
    }
}
