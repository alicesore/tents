using Windows.Win32.UI.Shell.Common;

namespace Tents.Shell.Interop;

/// <summary>
/// An immutable managed copy of a shell item ID list (PIDL), so items can be kept and passed between threads without
/// owning native memory. Compared by content.
/// </summary>
/// <remarks>
/// An ID list is a sequence of <c>SHITEMID</c> entries, each starting with its own 2-byte size, ended by a 2-byte
/// zero. The copy includes the terminator. See
/// <see href="https://learn.microsoft.com/windows/win32/shell/namespace-intro#item-ids-and-pidls">Item IDs and
/// PIDLs</see>.
/// </remarks>
public sealed class ShellIdList : IEquatable<ShellIdList>
{
    private const int _terminatorSize = sizeof(ushort);

    private readonly byte[] _bytes;

    private ShellIdList(byte[] bytes) => _bytes = bytes;

    /// <summary>The empty list, which names the desktop root itself.</summary>
    public static ShellIdList Empty { get; } = new(new byte[_terminatorSize]);

    /// <summary>Size in bytes, including the terminator.</summary>
    public int Length => _bytes.Length;

    /// <summary>Number of <c>SHITEMID</c> entries. 1 for a direct child of the desktop root.</summary>
    public int ItemCount
    {
        get
        {
            var count = 0;
            for (var offset = 0; ReadSize(_bytes, offset) is var size and > 0; offset += size)
            {
                count++;
            }

            return count;
        }
    }

    internal ReadOnlySpan<byte> Bytes => _bytes;

    /// <summary>The list without its last entry (the parent folder). The parent of the empty list is the empty list.</summary>
    public ShellIdList Parent
    {
        get
        {
            var lastStart = LastEntryOffset();
            return lastStart < 0 ? Empty : FromBytes([.. _bytes.AsSpan(0, lastStart), 0, 0]);
        }
    }

    /// <summary>The last entry alone, as a one-entry list (the child within <see cref="Parent"/>).</summary>
    public ShellIdList LastItem
    {
        get
        {
            var lastStart = LastEntryOffset();
            return lastStart < 0 ? Empty : FromBytes(_bytes.AsSpan(lastStart));
        }
    }

    /// <summary>Copies an ID list from its serialized form. The bytes must hold one complete list.</summary>
    /// <exception cref="ArgumentException">The bytes are not a well-formed ID list.</exception>
    public static ShellIdList FromBytes(ReadOnlySpan<byte> bytes)
    {
        var length = MeasureOrThrow(bytes);
        return new ShellIdList(bytes[..length].ToArray());
    }

    /// <summary>The size of the ID list at the start of <paramref name="bytes"/>, including the terminator.</summary>
    /// <exception cref="ArgumentException">The list is cut off or has an entry smaller than its size field.</exception>
    public static int MeasureOrThrow(ReadOnlySpan<byte> bytes)
    {
        var offset = 0;
        while (true)
        {
            if (offset + _terminatorSize > bytes.Length)
            {
                throw new ArgumentException("The ID list is cut off before its terminator.", nameof(bytes));
            }

            var size = ReadSize(bytes, offset);
            if (size == 0)
            {
                return offset + _terminatorSize;
            }

            if (size < _terminatorSize || offset + size > bytes.Length)
            {
                throw new ArgumentException($"The ID list has a malformed entry at offset {offset}.", nameof(bytes));
            }

            offset += size;
        }
    }

    /// <summary>Copies a native ID list. The caller keeps ownership of <paramref name="pidl"/>.</summary>
    internal static unsafe ShellIdList FromNative(ITEMIDLIST* pidl)
    {
        ArgumentNullException.ThrowIfNull(pidl);
        var start = (byte*)pidl;
        var offset = 0;
        while (*(ushort*)(start + offset) is var size and > 0)
        {
            offset += size;
        }

        return new ShellIdList(new ReadOnlySpan<byte>(start, offset + _terminatorSize).ToArray());
    }

    public bool Equals(ShellIdList? other) => other is not null && _bytes.AsSpan().SequenceEqual(other._bytes);

    public override bool Equals(object? obj) => Equals(obj as ShellIdList);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.AddBytes(_bytes);
        return hash.ToHashCode();
    }

    private int LastEntryOffset()
    {
        var last = -1;
        for (var offset = 0; ReadSize(_bytes, offset) is var size and > 0; offset += size)
        {
            last = offset;
        }

        return last;
    }

    private static ushort ReadSize(ReadOnlySpan<byte> bytes, int offset) => (ushort)(bytes[offset] | (bytes[offset + 1] << 8));
}
