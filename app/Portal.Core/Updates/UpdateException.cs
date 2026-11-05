namespace Portal.Core.Updates;

/// <summary>An update step failed; the message is meant for the user.</summary>
public sealed class UpdateException : Exception
{
    /// <summary>Creates the exception with a user-facing message.</summary>
    public UpdateException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a user-facing message and its cause.</summary>
    public UpdateException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
