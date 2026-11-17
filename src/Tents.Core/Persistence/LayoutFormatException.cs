namespace Tents.Core.Persistence;

/// <summary>The layout file is not valid: not JSON, the wrong shape, or a layout that breaks the model's rules.</summary>
/// <remarks>Messages never contain item ids, because item ids are paths on the user's desktop.</remarks>
public sealed class LayoutFormatException : Exception
{
    public LayoutFormatException()
    {
    }

    public LayoutFormatException(string message)
        : base(message)
    {
    }

    public LayoutFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>The layout file was written by a newer version of Tents, with a schema this version does not know.</summary>
public sealed class UnsupportedLayoutVersionException : Exception
{
    public UnsupportedLayoutVersionException()
    {
    }

    public UnsupportedLayoutVersionException(string message)
        : base(message)
    {
    }

    public UnsupportedLayoutVersionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public UnsupportedLayoutVersionException(int fileVersion, int supportedVersion)
        : base($"The layout file has schema version {fileVersion}; this version of Tents reads up to {supportedVersion}.")
    {
        FileVersion = fileVersion;
        SupportedVersion = supportedVersion;
    }

    /// <summary>The schema version found in the file.</summary>
    public int FileVersion { get; }

    /// <summary>The newest schema version this build can read.</summary>
    public int SupportedVersion { get; }
}
