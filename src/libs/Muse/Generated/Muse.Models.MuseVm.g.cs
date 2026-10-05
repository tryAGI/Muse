
#nullable enable

namespace Muse
{
    /// <summary>
    ///
    /// </summary>
    public sealed partial class MuseVm
    {
        /// <summary>
        ///
        /// </summary>
        [global::System.Text.Json.Serialization.JsonPropertyName("vm_id")]
        public string? VmId { get; set; }

        /// <summary>
        ///
        /// </summary>
        [global::System.Text.Json.Serialization.JsonPropertyName("vm_name")]
        public string? VmName { get; set; }

        /// <summary>
        ///
        /// </summary>
        [global::System.Text.Json.Serialization.JsonPropertyName("vm_ws_url")]
        public string? VmWsUrl { get; set; }

        /// <summary>
        ///
        /// </summary>
        [global::System.Text.Json.Serialization.JsonPropertyName("vm_url")]
        public string? VmUrl { get; set; }

        /// <summary>
        /// Sensitive session bearer. Never log or put in URLs.
        /// </summary>
        [global::System.Text.Json.Serialization.JsonPropertyName("vm_auth_token")]
        public string? VmAuthToken { get; set; }

        /// <summary>
        ///
        /// </summary>
        [global::System.Text.Json.Serialization.JsonPropertyName("default")]
        public bool? Default { get; set; }

        /// <summary>
        /// Additional properties that are not explicitly defined in the schema
        /// </summary>
        [global::System.Text.Json.Serialization.JsonExtensionData]
        public global::System.Collections.Generic.IDictionary<string, object> AdditionalProperties { get; set; } = new global::System.Collections.Generic.Dictionary<string, object>();

        /// <summary>
        /// Initializes a new instance of the <see cref="MuseVm" /> class.
        /// </summary>
        /// <param name="vmId"></param>
        /// <param name="vmName"></param>
        /// <param name="vmWsUrl"></param>
        /// <param name="vmUrl"></param>
        /// <param name="vmAuthToken">
        /// Sensitive session bearer. Never log or put in URLs.
        /// </param>
        /// <param name="default"></param>
#if NET7_0_OR_GREATER
        [global::System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
#endif
        public MuseVm(
            string? vmId,
            string? vmName,
            string? vmWsUrl,
            string? vmUrl,
            string? vmAuthToken,
            bool? @default)
        {
            this.VmId = vmId;
            this.VmName = vmName;
            this.VmWsUrl = vmWsUrl;
            this.VmUrl = vmUrl;
            this.VmAuthToken = vmAuthToken;
            this.Default = @default;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MuseVm" /> class.
        /// </summary>
        public MuseVm()
        {
        }

    }
}