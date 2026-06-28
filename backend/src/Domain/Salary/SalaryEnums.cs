namespace JobOfferMatcher.Domain.Salary;

/// <summary>Reporting period of a salary figure (data-model §Salary).</summary>
public enum SalaryPeriod
{
    Hourly,
    Daily,
    Monthly,
    Yearly,
}

/// <summary>Contract basis a salary band is quoted under. <see cref="Unknown"/> when not stated.</summary>
public enum EmploymentBasis
{
    Unknown = 0,
    B2B,
    Permanent,
}

/// <summary>Whether a figure is gross or net. <see cref="Unknown"/> when not stated.</summary>
public enum TaxTreatment
{
    Unknown = 0,
    Gross,
    Net,
}
