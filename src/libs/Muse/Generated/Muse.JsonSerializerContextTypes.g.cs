
#nullable enable

#pragma warning disable CS0618 // Type or member is obsolete

namespace Muse
{
    /// <summary>
    ///
    /// </summary>
    public sealed partial class JsonSerializerContextTypes
    {
        /// <summary>
        ///
        /// </summary>
        public global::System.Collections.Generic.Dictionary<string, string>? StringStringDictionary { get; set; }

        /// <summary>
        ///
        /// </summary>
        public global::System.Collections.Generic.Dictionary<string, object>? StringObjectDictionary { get; set; }

        /// <summary>
        /// Runtime object lists used by dynamic JSON payloads such as tool arguments.
        /// </summary>
        public global::System.Collections.Generic.List<object>? ObjectList { get; set; }

        /// <summary>
        ///
        /// </summary>
        public global::System.Text.Json.JsonElement? JsonElement { get; set; }

        /// <summary>
        ///
        /// </summary>
        public global::Muse.MuseVm? Type0 { get; set; }
        /// <summary>
        ///
        /// </summary>
        public string? Type1 { get; set; }
        /// <summary>
        ///
        /// </summary>
        public bool? Type2 { get; set; }
        /// <summary>
        ///
        /// </summary>
        public global::Muse.FetchVmsResponse? Type3 { get; set; }
        /// <summary>
        ///
        /// </summary>
        public global::System.Collections.Generic.IList<global::Muse.MuseVm>? Type4 { get; set; }
        /// <summary>
        ///
        /// </summary>
        public global::Muse.RefreshDeviceTokenRequest? Type5 { get; set; }
        /// <summary>
        ///
        /// </summary>
        public global::Muse.DeviceTokenPair? Type6 { get; set; }
        /// <summary>
        ///
        /// </summary>
        public global::Muse.RefreshDeviceTokenResponse? Type7 { get; set; }

        /// <summary>
        ///
        /// </summary>
        public global::System.Collections.Generic.List<global::Muse.MuseVm>? ListType0 { get; set; }
    }
}