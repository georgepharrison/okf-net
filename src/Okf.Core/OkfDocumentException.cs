namespace Okf.Core;

/// <summary>
/// Raised when a document cannot be parsed or fails OKF v0.2 §11 conformance.
/// Ports the reference implementation's <c>OKFDocumentError</c>.
/// </summary>
public class OkfDocumentException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="OkfDocumentException" /> class.</summary>
    public OkfDocumentException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="OkfDocumentException" /> class.</summary>
    /// <param name="message">The error message.</param>
    public OkfDocumentException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="OkfDocumentException" /> class.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying failure.</param>
    public OkfDocumentException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
