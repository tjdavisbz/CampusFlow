using Volo.Abp.Domain.Entities;

namespace CampusFlow.Reporting;

/// <summary>
/// Texas state-reporting country code used by CBM reports.
/// </summary>
public class TexasCountryCode : AggregateRoot<int>
{
    public string Name { get; private set; } = null!;
    public string Abbreviation { get; private set; } = null!;

    protected TexasCountryCode() { }

    public TexasCountryCode(int code, string name, string abbreviation) : base(code)
    {
        Name = name;
        Abbreviation = abbreviation;
    }

    public void Update(string name, string abbreviation)
    {
        Name = name;
        Abbreviation = abbreviation;
    }
}
