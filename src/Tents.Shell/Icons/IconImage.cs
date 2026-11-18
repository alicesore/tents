namespace Tents.Shell.Icons;

/// <summary>A decoded icon: top-down, premultiplied BGRA (<c>Pbgra32</c>), stride <c>Width * 4</c>.</summary>
/// <param name="Width">Pixels. Can be smaller than the requested size for a non-square thumbnail.</param>
/// <param name="Height">Pixels.</param>
/// <param name="Pixels">Exactly <c>Width * Height * 4</c> bytes. Treat as immutable.</param>
/// <param name="SourceAlpha">What the shell's bitmap held before conversion.</param>
public sealed record IconImage(int Width, int Height, byte[] Pixels, AlphaKind SourceAlpha)
{
    public int Stride => Width * 4;
}
