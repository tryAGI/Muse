namespace Muse;

/// <summary>A malformed, unauthenticated, or unsupported Muse protocol message.</summary>
public sealed class MuseProtocolException : IOException
{
    /// <summary>Creates a protocol failure.</summary>
    public MuseProtocolException() : base("Muse protocol failure.") { }

    /// <summary>Creates a protocol failure with a non-secret diagnostic message.</summary>
    public MuseProtocolException(string message) : base(message) { }

    /// <summary>Creates a protocol failure preserving its cause.</summary>
    public MuseProtocolException(string message, Exception innerException) : base(message, innerException) { }
}
