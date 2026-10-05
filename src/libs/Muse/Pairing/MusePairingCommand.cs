namespace Muse;

/// <summary>One authenticated enrollment command. A null grant denotes a Wi-Fi listing request.</summary>
/// <remarks>Never expose the credentials to the BLE relay or serialize this object into logs.</remarks>
public sealed class MusePairingCommand
{
    internal MusePairingCommand(MuseProvisioningCredentials? credentials) => Credentials = credentials;
    /// <summary>True when the app requests a listing of the relay's already-connected network.</summary>
    public bool IsWifiScan => Credentials is null;
    /// <summary>Present only for decrypted provision_v2. Must be verified and durably stored by the backend.</summary>
    public MuseProvisioningCredentials? Credentials { get; }
    /// <summary>No secret-bearing object representation.</summary>
    public override string ToString() => "MusePairingCommand (redacted)";
}
