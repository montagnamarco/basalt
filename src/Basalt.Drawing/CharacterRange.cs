namespace System.Drawing;

/// <summary>A run of characters in a string: where it starts and how many there are.</summary>
public struct CharacterRange : IEquatable<CharacterRange>
{
    public CharacterRange(int First, int Length)
    {
        this.First = First;
        this.Length = Length;
    }

    public int First { get; set; }

    public int Length { get; set; }

    public static bool operator ==(CharacterRange cr1, CharacterRange cr2) => cr1.Equals(cr2);

    public static bool operator !=(CharacterRange cr1, CharacterRange cr2) => !cr1.Equals(cr2);

    public readonly bool Equals(CharacterRange other) => other.First == First && other.Length == Length;

    public override readonly bool Equals(object? obj) => obj is CharacterRange other && Equals(other);

    public override readonly int GetHashCode() => HashCode.Combine(First, Length);
}
