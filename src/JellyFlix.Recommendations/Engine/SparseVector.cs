using System;
using System.Collections.Generic;
using System.Linq;

namespace JellyFlix.Recommendations.Engine;

/// <summary>
/// A sparse vector keyed by feature key.
/// </summary>
public sealed class SparseVector
{
    private readonly Dictionary<string, double> _values;

    /// <summary>
    /// Initializes a new instance of the <see cref="SparseVector"/> class.
    /// </summary>
    public SparseVector()
    {
        _values = new Dictionary<string, double>(StringComparer.Ordinal);
    }

    private SparseVector(Dictionary<string, double> values)
    {
        _values = values;
    }

    /// <summary>
    /// Gets the non-zero entries.
    /// </summary>
    public IReadOnlyDictionary<string, double> Values => _values;

    /// <summary>
    /// Gets a value indicating whether the vector has no entries.
    /// </summary>
    public bool IsEmpty => _values.Count == 0;

    /// <summary>
    /// Gets the value for a key, or 0.
    /// </summary>
    /// <param name="key">The feature key.</param>
    public double this[string key] => _values.GetValueOrDefault(key);

    /// <summary>
    /// Adds <paramref name="scale"/> times <paramref name="other"/> to this vector.
    /// </summary>
    /// <param name="other">The vector to add.</param>
    /// <param name="scale">The factor.</param>
    public void Add(SparseVector other, double scale)
    {
        foreach (var (key, value) in other._values)
        {
            _values[key] = _values.GetValueOrDefault(key) + (value * scale);
        }
    }

    /// <summary>
    /// Sets a value.
    /// </summary>
    /// <param name="key">The feature key.</param>
    /// <param name="value">The value.</param>
    public void Set(string key, double value) => _values[key] = value;

    /// <summary>
    /// Computes the dot product. Iterates the smaller vector.
    /// </summary>
    /// <param name="other">The other vector.</param>
    /// <returns>The dot product.</returns>
    public double Dot(SparseVector other)
    {
        var (small, large) = _values.Count <= other._values.Count ? (_values, other._values) : (other._values, _values);
        var sum = 0.0;
        foreach (var (key, value) in small)
        {
            if (large.TryGetValue(key, out var otherValue))
            {
                sum += value * otherValue;
            }
        }

        return sum;
    }

    /// <summary>
    /// Returns a unit-length copy, or an empty vector if this one has no length.
    /// </summary>
    /// <returns>The normalized vector.</returns>
    public SparseVector Normalized()
    {
        var length = Math.Sqrt(_values.Values.Sum(v => v * v));
        if (length < 1e-12)
        {
            return new SparseVector();
        }

        return new SparseVector(_values.ToDictionary(kv => kv.Key, kv => kv.Value / length, StringComparer.Ordinal));
    }
}
