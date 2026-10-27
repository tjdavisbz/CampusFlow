using System;
using System.Globalization;
using System.Linq;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;
namespace CampusFlow.Bibliu;

public class BibliuConfiguration : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public string BillingTermId { get; private set; } = "";
    public string BibliuTermId { get; private set; } = "";
    public string StoreNumber { get; private set; } = "";
    public string InSiteStoreNumber { get; private set; } = "";
    public bool IsEnabled { get; private set; }
    protected BibliuConfiguration() { }
    public BibliuConfiguration(Guid id, Guid tenantId) : base(id) => TenantId = tenantId;
    public void Update(string billingTermId, string bibliuTermId, string storeNumber, string inSiteStoreNumber, bool enabled)
    {
        if (!int.TryParse(billingTermId, out var billing) || billing <= 0 ||
            !int.TryParse(bibliuTermId, out var bibliu) || bibliu <= 0)
            throw new ArgumentException("Choose valid Elements terms.");
        if (!Digits(storeNumber, 3) || !Digits(inSiteStoreNumber, 3))
            throw new ArgumentException("Store numbers must contain exactly three digits.");
        BillingTermId = billing.ToString(CultureInfo.InvariantCulture);
        BibliuTermId = bibliu.ToString(CultureInfo.InvariantCulture);
        StoreNumber = storeNumber;
        InSiteStoreNumber = inSiteStoreNumber;
        IsEnabled = enabled;
    }
    private static bool Digits(string? value, int length) => value?.Length == length && value.All(c => c >= '0' && c <= '9');
}
