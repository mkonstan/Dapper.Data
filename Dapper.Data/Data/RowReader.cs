using System.Data.Common;

namespace Dapper.Data;

/// <summary>Validates one result schema and converts provider values into ordered rows.</summary>
internal sealed class RowReader
{
    private readonly string[] _names;
    private readonly StringComparer _comparer;

    /// <summary>Rejects duplicate names before reading any row, including empty results.</summary>
    internal RowReader(DbDataReader reader, StringComparer comparer)
    {
        _comparer = comparer;
        _names = new string[reader.FieldCount];
        var names = new HashSet<string>(comparer);
        for (var i = 0; i < _names.Length; i++)
        {
            var name = reader.GetName(i);
            if (!names.Add(name))
                throw new InvalidOperationException($"RowReader: duplicate column '{name}'; alias it to a unique name.");
            _names[i] = name;
        }
    }

    /// <summary>Copies the current row without changing provider value types.</summary>
    internal DbRow ReadRow(DbDataReader reader)
    {
        IDictionary<string, object?> row = new DbRow(_comparer);
        for (var i = 0; i < _names.Length; i++)
        {
            var value = reader.GetValue(i);
            row.Add(_names[i], value is DBNull ? null : value);
        }
        return (DbRow)row;
    }

    /// <summary>Checks SQL at the public boundary before opening a context connection.</summary>
    internal static void ValidateSql(string sql) => ArgumentException.ThrowIfNullOrWhiteSpace(sql);

    /// <summary>Resolves the configured comparison without any culture dependency.</summary>
    internal static StringComparer Comparer(ColumnNameMatching matching) => matching switch
    {
        ColumnNameMatching.CaseSensitive => StringComparer.Ordinal,
        ColumnNameMatching.CaseInsensitive => StringComparer.OrdinalIgnoreCase,
        _ => throw new ArgumentOutOfRangeException(nameof(matching), matching, "Use CaseSensitive or CaseInsensitive.")
    };

    /// <summary>Checks the first/single cardinality after the current result has been consumed.</summary>
    internal static dynamic? Select(DbRow? first, bool many, bool single, bool allowDefault, string member)
    {
        if (first is null && !allowDefault)
            throw new InvalidOperationException($"{member}: the result has no rows; at least one row is required.");
        if (many && single)
            throw new InvalidOperationException($"{member}: the result has more than one row; at most one row is allowed.");
        return first;
    }
}
