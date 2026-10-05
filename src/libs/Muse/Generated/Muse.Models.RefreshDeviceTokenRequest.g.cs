
#nullable enable

namespace Muse
{
    /// <summary>
    ///
    /// </summary>
    public sealed partial class RefreshDeviceTokenRequest
    {
        /// <summary>
        ///
        /// </summary>
        [global::System.Text.Json.Serialization.JsonPropertyName("device_id")]
        [global::System.Text.Json.Serialization.JsonRequired]
        public required string DeviceId { get; set; }

        /// <summary>
        /// Sensitive integration token.
        /// </summary>
        [global::System.Text.Json.Serialization.JsonPropertyName("sdk_token")]
        public string? SdkToken { get; set; }

        /// <summary>
        /// Additional properties that are not explicitly defined in the schema
        /// </summary>
        [global::System.Text.Json.Serialization.JsonExtensionData]
        public global::System.Collections.Generic.IDictionary<string, object> AdditionalProperties { get; set; } = new global::System.Collections.Generic.Dictionary<string, object>();

        /// <summary>
        /// Initializes a new instance of the <see cref="RefreshDeviceTokenRequest" /> class.
        /// </summary>
        /// <param name="deviceId"></param>
        /// <param name="sdkToken">
        /// Sensitive integration token.
        /// </param>
#if NET7_0_OR_GREATER
        [global::System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
#endif
        public RefreshDeviceTokenRequest(
            string deviceId,
            string? sdkToken)
        {
            this.DeviceId = deviceId ?? throw new global::System.ArgumentNullException(nameof(deviceId));
            this.SdkToken = sdkToken;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RefreshDeviceTokenRequest" /> class.
        /// </summary>
        public RefreshDeviceTokenRequest()
        {
        }

    }
}