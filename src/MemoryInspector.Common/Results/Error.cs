namespace MemoryInspector.Common;

/// <summary>
/// Describes an operation failure without coupling domain code to a UI framework.
/// </summary>
public sealed record Error
{
    public static Error None { get; } = new(ErrorCode.None, string.Empty);

    public Error(
        ErrorCode code,
        string message,
        Exception? exception = null,
        Error? cause = null)
    {
        if (code == ErrorCode.None)
        {
            if (!string.IsNullOrEmpty(message) || exception is not null || cause is not null)
            {
                throw new ArgumentException(
                    "ErrorCode.None cannot contain failure details.",
                    nameof(code));
            }
        }
        else if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException(
                "A failure error must contain a user-readable message.",
                nameof(message));
        }

        Code = code;
        Message = message;
        Exception = exception;
        Cause = cause;
    }

    public ErrorCode Code { get; }

    public string Message { get; }

    /// <summary>
    /// Gets the original exception for diagnostic logging. UI code should display
    /// <see cref="Message"/> instead of exception details.
    /// </summary>
    public Exception? Exception { get; }

    public Error? Cause { get; }

    public Error WithCause(Error cause)
    {
        Guard.NotNull(cause);

        if (cause.Code == ErrorCode.None)
        {
            throw new ArgumentException(
                "A failure cannot be caused by Error.None.",
                nameof(cause));
        }

        return new Error(Code, Message, Exception, cause);
    }

    public IEnumerable<Error> EnumerateChain()
    {
        for (Error? current = this;
             current is not null && current.Code != ErrorCode.None;
             current = current.Cause)
        {
            yield return current;
        }
    }

    public string ToDisplayMessage()
    {
        return string.Join(
            " → ",
            EnumerateChain()
                .Select(error => error.Message)
                .Distinct(StringComparer.Ordinal));
    }
}
