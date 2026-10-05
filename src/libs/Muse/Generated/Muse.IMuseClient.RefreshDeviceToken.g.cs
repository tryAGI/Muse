#nullable enable

namespace Muse
{
    public partial interface IMuseClient
    {
        /// <summary>
        /// Rotate a device access and refresh token pair.<br/>
        /// Requires a refresh bearer formatted as hatch_refresh:VALUE, never an access token. The sdk_token body field identifies the integration and does not authorize the user.
        /// </summary>
        /// <param name="request"></param>
        /// <param name="requestOptions">Per-request overrides such as headers, query parameters, timeout, retries, and response buffering.</param>
        /// <param name="cancellationToken">The token to cancel the operation with</param>
        /// <exception cref="global::Muse.ApiException"></exception>
        global::System.Threading.Tasks.Task<global::Muse.RefreshDeviceTokenResponse> RefreshDeviceTokenAsync(

            global::Muse.RefreshDeviceTokenRequest request,
            global::Muse.AutoSDKRequestOptions? requestOptions = default,
            global::System.Threading.CancellationToken cancellationToken = default);
        /// <summary>
        /// Rotate a device access and refresh token pair.<br/>
        /// Requires a refresh bearer formatted as hatch_refresh:VALUE, never an access token. The sdk_token body field identifies the integration and does not authorize the user.
        /// </summary>
        /// <param name="request"></param>
        /// <param name="requestOptions">Per-request overrides such as headers, query parameters, timeout, retries, and response buffering.</param>
        /// <param name="cancellationToken">The token to cancel the operation with</param>
        /// <exception cref="global::Muse.ApiException"></exception>
        global::System.Threading.Tasks.Task<global::Muse.AutoSDKHttpResponse<global::Muse.RefreshDeviceTokenResponse>> RefreshDeviceTokenAsResponseAsync(

            global::Muse.RefreshDeviceTokenRequest request,
            global::Muse.AutoSDKRequestOptions? requestOptions = default,
            global::System.Threading.CancellationToken cancellationToken = default);
        /// <summary>
        /// Rotate a device access and refresh token pair.<br/>
        /// Requires a refresh bearer formatted as hatch_refresh:VALUE, never an access token. The sdk_token body field identifies the integration and does not authorize the user.
        /// </summary>
        /// <param name="deviceId"></param>
        /// <param name="sdkToken">
        /// Sensitive integration token.
        /// </param>
        /// <param name="requestOptions">Per-request overrides such as headers, query parameters, timeout, retries, and response buffering.</param>
        /// <param name="cancellationToken">The token to cancel the operation with</param>
        /// <exception cref="global::System.InvalidOperationException"></exception>
        global::System.Threading.Tasks.Task<global::Muse.RefreshDeviceTokenResponse> RefreshDeviceTokenAsync(
            string deviceId,
            string? sdkToken = default,
            global::Muse.AutoSDKRequestOptions? requestOptions = default,
            global::System.Threading.CancellationToken cancellationToken = default);
    }
}