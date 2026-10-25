namespace Dapper.Data;

/// <summary>Chooses ordinal comparison for dynamic and dictionary column names.</summary>
public enum ColumnNameMatching
{
    /// <summary>Compares names using ordinal, case-sensitive comparison.</summary>
    CaseSensitive = 0,
    /// <summary>Compares names using ordinal comparison ignoring case.</summary>
    CaseInsensitive = 1
}
