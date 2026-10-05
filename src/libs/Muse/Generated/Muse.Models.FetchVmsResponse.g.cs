
#nullable enable

namespace Muse
{
    /// <summary>
    ///
    /// </summary>
    public sealed partial class FetchVmsResponse
    {
        /// <summary>
        ///
        /// </summary>
        [global::System.Text.Json.Serialization.JsonPropertyName("vm_list")]
        public global::System.Collections.Generic.IList<global::Muse.MuseVm>? VmList { get; set; }

        /// <summary>
        ///
        /// </summary>
        [global::System.Text.Json.Serialization.JsonPropertyName("error_title")]
        public string? ErrorTitle { get; set; }

        /// <summary>
        ///
        /// </summary>
        [global::System.Text.Json.Serialization.JsonPropertyName("backend_error_code")]
        public string? BackendErrorCode { get; set; }

        /// <summary>
        /// Additional properties that are not explicitly defined in the schema
        /// </summary>
        [global::System.Text.Json.Serialization.JsonExtensionData]
        public global::System.Collections.Generic.IDictionary<string, object> AdditionalProperties { get; set; } = new global::System.Collections.Generic.Dictionary<string, object>();

        /// <summary>
        /// Initializes a new instance of the <see cref="FetchVmsResponse" /> class.
        /// </summary>
        /// <param name="vmList"></param>
        /// <param name="errorTitle"></param>
        /// <param name="backendErrorCode"></param>
#if NET7_0_OR_GREATER
        [global::System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
#endif
        public FetchVmsResponse(
            global::System.Collections.Generic.IList<global::Muse.MuseVm>? vmList,
            string? errorTitle,
            string? backendErrorCode)
        {
            this.VmList = vmList;
            this.ErrorTitle = errorTitle;
            this.BackendErrorCode = backendErrorCode;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="FetchVmsResponse" /> class.
        /// </summary>
        public FetchVmsResponse()
        {
        }

    }
}