
#nullable enable

namespace Muse
{
    /// <summary>
    ///
    /// </summary>
    public sealed partial class RefreshDeviceTokenResponse
    {
        /// <summary>
        ///
        /// </summary>
        [global::System.Text.Json.Serialization.JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        /// <summary>
        ///
        /// </summary>
        [global::System.Text.Json.Serialization.JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        /// <summary>
        ///
        /// </summary>
        [global::System.Text.Json.Serialization.JsonPropertyName("payload")]
        public global::Muse.DeviceTokenPair? Payload { get; set; }

        /// <summary>
        /// Additional properties that are not explicitly defined in the schema
        /// </summary>
        [global::System.Text.Json.Serialization.JsonExtensionData]
        public global::System.Collections.Generic.IDictionary<string, object> AdditionalProperties { get; set; } = new global::System.Collections.Generic.Dictionary<string, object>();

        /// <summary>
        /// Initializes a new instance of the <see cref="RefreshDeviceTokenResponse" /> class.
        /// </summary>
        /// <param name="accessToken"></param>
        /// <param name="refreshToken"></param>
        /// <param name="payload"></param>
#if NET7_0_OR_GREATER
        [global::System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
#endif
        public RefreshDeviceTokenResponse(
            string? accessToken,
            string? refreshToken,
            global::Muse.DeviceTokenPair? payload)
        {
            this.AccessToken = accessToken;
            this.RefreshToken = refreshToken;
            this.Payload = payload;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RefreshDeviceTokenResponse" /> class.
        /// </summary>
        public RefreshDeviceTokenResponse()
        {
        }

    }
}