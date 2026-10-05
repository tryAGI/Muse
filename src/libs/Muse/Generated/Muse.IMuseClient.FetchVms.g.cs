#nullable enable

namespace Muse
{
    public partial interface IMuseClient
    {
        /// <summary>
        /// Fetch VMs accessible to an authorized device.
        /// </summary>
        /// <param name="xApiVersion">
        /// Default Value: 1.0.0
        /// </param>
        /// <param name="requestOptions">Per-request overrides such as headers, query parameters, timeout, retries, and response buffering.</param>
        /// <param name="cancellationToken">The token to cancel the operation with</param>
        /// <exception cref="global::Muse.ApiException"></exception>
        global::System.Threading.Tasks.Task<global::Muse.FetchVmsResponse> FetchVmsAsync(
            string? xApiVersion = default,
            global::Muse.AutoSDKRequestOptions? requestOptions = default,
            global::System.Threading.CancellationToken cancellationToken = default);
        /// <summary>
        /// Fetch VMs accessible to an authorized device.
        /// </summary>
        /// <param name="xApiVersion">
        /// Default Value: 1.0.0
        /// </param>
        /// <param name="requestOptions">Per-request overrides such as headers, query parameters, timeout, retries, and response buffering.</param>
        /// <param name="cancellationToken">The token to cancel the operation with</param>
        /// <exception cref="global::Muse.ApiException"></exception>
        global::System.Threading.Tasks.Task<global::Muse.AutoSDKHttpResponse<global::Muse.FetchVmsResponse>> FetchVmsAsResponseAsync(
            string? xApiVersion = default,
            global::Muse.AutoSDKRequestOptions? requestOptions = default,
            global::System.Threading.CancellationToken cancellationToken = default);
    }
}