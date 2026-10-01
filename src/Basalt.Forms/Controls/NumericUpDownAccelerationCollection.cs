using System.Collections;
using System.ComponentModel;

namespace System.Windows.Forms;

/// <summary>The <see cref="NumericUpDownAcceleration"/> steps of a <see cref="NumericUpDown"/>, in the order they apply.</summary>
[ListBindable(false)]
public class NumericUpDownAccelerationCollection :
    MarshalByRefObject, ICollection<NumericUpDownAcceleration>, IEnumerable<NumericUpDownAcceleration>, IEnumerable
{
    private readonly List<NumericUpDownAcceleration> _items = [];

    public int Count => _items.Count;

    public bool IsReadOnly => false;

    public NumericUpDownAcceleration this[int index] => _items[index];

    public void Add(NumericUpDownAcceleration acceleration)
    {
        ArgumentNullException.ThrowIfNull(acceleration);

        _items.Add(acceleration);
    }

    public void AddRange(params NumericUpDownAcceleration[] accelerations)
    {
        ArgumentNullException.ThrowIfNull(accelerations);

        foreach (var acceleration in accelerations) Add(acceleration);
    }

    public void Clear() => _items.Clear();

    public bool Contains(NumericUpDownAcceleration acceleration) => _items.Contains(acceleration);

    public void CopyTo(NumericUpDownAcceleration[] array, int index) => _items.CopyTo(array, index);

    public bool Remove(NumericUpDownAcceleration acceleration) => _items.Remove(acceleration);

    // Microsoft's collection enumerates only through its interfaces; a public GetEnumerator would add surface.
    IEnumerator<NumericUpDownAcceleration> IEnumerable<NumericUpDownAcceleration>.GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
}
