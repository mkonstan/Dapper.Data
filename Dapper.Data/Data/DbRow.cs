using System.Collections;
using System.Collections.Generic;
using System.Dynamic;

namespace Dapper.Data;

/// <summary>A mutable row whose column order and name comparison survive buffering.</summary>
/// <remarks>Dictionary members are explicit so columns named Count, Keys or Values bind dynamically.</remarks>
internal sealed class DbRow : DynamicObject, IDictionary<string, object?>
{
    private readonly OrderedDictionary<string, object?> _values;

    /// <summary>Creates a row with the result set's name comparer.</summary>
    internal DbRow(StringComparer comparer) => _values = new(comparer);

    /// <summary>Returns false for missing columns so the runtime binder reports a missing member.</summary>
    public override bool TryGetMember(GetMemberBinder binder, out object? result) =>
        _values.TryGetValue(binder.Name, out result);

    /// <summary>Adds or replaces a column using the row's name comparison.</summary>
    public override bool TrySetMember(SetMemberBinder binder, object? value)
    {
        _values[binder.Name] = value;
        return true;
    }

    /// <summary>Lists column names in their current dictionary order.</summary>
    public override IEnumerable<string> GetDynamicMemberNames() => _values.Keys;

    object? IDictionary<string, object?>.this[string key] { get => _values[key]; set => _values[key] = value; }
    ICollection<string> IDictionary<string, object?>.Keys => _values.Keys;
    ICollection<object?> IDictionary<string, object?>.Values => _values.Values;
    int ICollection<KeyValuePair<string, object?>>.Count => _values.Count;
    bool ICollection<KeyValuePair<string, object?>>.IsReadOnly => false;
    void IDictionary<string, object?>.Add(string key, object? value) => _values.Add(key, value);
    bool IDictionary<string, object?>.ContainsKey(string key) => _values.ContainsKey(key);
    bool IDictionary<string, object?>.Remove(string key) => _values.Remove(key);
    bool IDictionary<string, object?>.TryGetValue(string key, out object? value) => _values.TryGetValue(key, out value);
    void ICollection<KeyValuePair<string, object?>>.Add(KeyValuePair<string, object?> item) =>
        ((ICollection<KeyValuePair<string, object?>>)_values).Add(item);
    void ICollection<KeyValuePair<string, object?>>.Clear() => _values.Clear();
    bool ICollection<KeyValuePair<string, object?>>.Contains(KeyValuePair<string, object?> item) =>
        ((ICollection<KeyValuePair<string, object?>>)_values).Contains(item);
    void ICollection<KeyValuePair<string, object?>>.CopyTo(KeyValuePair<string, object?>[] array, int index) =>
        ((ICollection<KeyValuePair<string, object?>>)_values).CopyTo(array, index);
    bool ICollection<KeyValuePair<string, object?>>.Remove(KeyValuePair<string, object?> item) =>
        ((ICollection<KeyValuePair<string, object?>>)_values).Remove(item);
    IEnumerator<KeyValuePair<string, object?>> IEnumerable<KeyValuePair<string, object?>>.GetEnumerator() => _values.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _values.GetEnumerator();
}
