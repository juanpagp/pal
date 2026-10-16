namespace PAL;

/// <summary>
/// A PAL infrastructure error: the operation failed in the windowing,
/// graphics or text pipeline, and the message states what happened and what
/// to do about it (which backend, which native dependency, which builder
/// option was involved).
/// </summary>
public sealed class PALException : Exception
{
    /// <summary>Creates the exception with an actionable message.</summary>
    public PALException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and inner cause.</summary>
    public PALException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
