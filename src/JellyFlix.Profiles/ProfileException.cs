using System;

namespace JellyFlix.Profiles;

/// <summary>
/// Thrown when a profile operation is refused.
/// </summary>
public class ProfileException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProfileException"/> class.
    /// </summary>
    public ProfileException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ProfileException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    public ProfileException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ProfileException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The inner exception.</param>
    public ProfileException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ProfileException"/> class.
    /// </summary>
    /// <param name="error">The error kind.</param>
    /// <param name="message">The message.</param>
    public ProfileException(ProfileError error, string message)
        : base(message)
    {
        Error = error;
    }

    /// <summary>
    /// Gets the error kind.
    /// </summary>
    public ProfileError Error { get; }
}
