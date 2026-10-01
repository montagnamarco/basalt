namespace System.Drawing.Imaging;

/// <summary>
/// The 5x5 matrix GDI+ recolours an image through: a pixel is the row vector
/// [R G B A 1], normalised to 0..1, and the adjusted pixel is that row vector
/// times this matrix: [R' G' B' A' 1] = [R G B A 1] &#215; M. Row 4
/// (<see cref="Matrix40"/>..<see cref="Matrix44"/>) is therefore the constant
/// added to every pixel, and the leading 4x4 block the linear part; column 4
/// of every row is always 0 for a real affine colour transform, but GDI+
/// still carries it (it is the column that keeps the trailing 1 a 1).
/// </summary>
public sealed class ColorMatrix
{
    private readonly float[,] _matrix;

    public ColorMatrix() => _matrix = Identity();

    public ColorMatrix(float[][] newColorMatrix)
    {
        ArgumentNullException.ThrowIfNull(newColorMatrix);
        if (newColorMatrix.Length != 5) throw new ArgumentException("A ColorMatrix needs exactly 5 rows.", nameof(newColorMatrix));

        _matrix = new float[5, 5];

        for (var row = 0; row < 5; row++)
        {
            var source = newColorMatrix[row];
            ArgumentNullException.ThrowIfNull(source);
            if (source.Length != 5) throw new ArgumentException("A ColorMatrix needs exactly 5 columns.", nameof(newColorMatrix));

            for (var column = 0; column < 5; column++) _matrix[row, column] = source[column];
        }
    }

    // The flat form GDI+ added alongside the jagged one: the same 25 numbers,
    // read row after row (so index row*5+column is Matrix<row><column>).
    public ColorMatrix(ReadOnlySpan<float> newColorMatrix)
    {
        if (newColorMatrix.Length != 25) throw new ArgumentException("A ColorMatrix needs exactly 25 elements.", nameof(newColorMatrix));

        _matrix = new float[5, 5];

        for (var row = 0; row < 5; row++)
        {
            for (var column = 0; column < 5; column++) _matrix[row, column] = newColorMatrix[row * 5 + column];
        }
    }

    public float this[int row, int column]
    {
        get => _matrix[row, column];
        set => _matrix[row, column] = value;
    }

    public float Matrix00 { get => _matrix[0, 0]; set => _matrix[0, 0] = value; }
    public float Matrix01 { get => _matrix[0, 1]; set => _matrix[0, 1] = value; }
    public float Matrix02 { get => _matrix[0, 2]; set => _matrix[0, 2] = value; }
    public float Matrix03 { get => _matrix[0, 3]; set => _matrix[0, 3] = value; }
    public float Matrix04 { get => _matrix[0, 4]; set => _matrix[0, 4] = value; }
    public float Matrix10 { get => _matrix[1, 0]; set => _matrix[1, 0] = value; }
    public float Matrix11 { get => _matrix[1, 1]; set => _matrix[1, 1] = value; }
    public float Matrix12 { get => _matrix[1, 2]; set => _matrix[1, 2] = value; }
    public float Matrix13 { get => _matrix[1, 3]; set => _matrix[1, 3] = value; }
    public float Matrix14 { get => _matrix[1, 4]; set => _matrix[1, 4] = value; }
    public float Matrix20 { get => _matrix[2, 0]; set => _matrix[2, 0] = value; }
    public float Matrix21 { get => _matrix[2, 1]; set => _matrix[2, 1] = value; }
    public float Matrix22 { get => _matrix[2, 2]; set => _matrix[2, 2] = value; }
    public float Matrix23 { get => _matrix[2, 3]; set => _matrix[2, 3] = value; }
    public float Matrix24 { get => _matrix[2, 4]; set => _matrix[2, 4] = value; }
    public float Matrix30 { get => _matrix[3, 0]; set => _matrix[3, 0] = value; }
    public float Matrix31 { get => _matrix[3, 1]; set => _matrix[3, 1] = value; }
    public float Matrix32 { get => _matrix[3, 2]; set => _matrix[3, 2] = value; }
    public float Matrix33 { get => _matrix[3, 3]; set => _matrix[3, 3] = value; }
    public float Matrix34 { get => _matrix[3, 4]; set => _matrix[3, 4] = value; }
    public float Matrix40 { get => _matrix[4, 0]; set => _matrix[4, 0] = value; }
    public float Matrix41 { get => _matrix[4, 1]; set => _matrix[4, 1] = value; }
    public float Matrix42 { get => _matrix[4, 2]; set => _matrix[4, 2] = value; }
    public float Matrix43 { get => _matrix[4, 3]; set => _matrix[4, 3] = value; }
    public float Matrix44 { get => _matrix[4, 4]; set => _matrix[4, 4] = value; }

    /// <summary>The coefficient of input channel <paramref name="inputChannel"/> on output channel <paramref name="outputChannel"/> (0=R, 1=G, 2=B, 3=A); row 4 is the constant.</summary>
    internal float Coefficient(int inputChannel, int outputChannel) => _matrix[inputChannel, outputChannel];

    private static float[,] Identity()
    {
        var matrix = new float[5, 5];
        for (var i = 0; i < 5; i++) matrix[i, i] = 1f;
        return matrix;
    }
}
