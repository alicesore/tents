namespace Tents.Core;

/// <summary>Thrown when a layout or layout set would break one of the model's invariants.</summary>
/// <remarks>Messages never contain item ids, because item ids are paths on the user's desktop.</remarks>
public sealed class InvalidLayoutException : Exception
{
    public InvalidLayoutException()
    {
    }

    public InvalidLayoutException(string message)
        : base(message)
    {
    }

    public InvalidLayoutException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
