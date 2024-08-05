
namespace UACloudTwin.Models
{
    using Newtonsoft.Json;
    using System.Collections.Generic;

    public class DTDL
    {
        [JsonProperty("@context")]
        public string context { get; set; }

        [JsonProperty("@id")]
        public string id { get; set; }

        [JsonProperty("@type")]
        public string type { get; set; }

        public string displayName { get; set; }

        public string description { get; set; }

        public string comment { get; set; }

        public List<Content> contents { get; set; }

        public List<string> extends { get;set; }

        public object schemas { get; set; }
    }

    public class Content
    {
        [JsonProperty("@type")]
        public string type { get; set; }

        [JsonProperty("@id")]
        public string id { get; set; }

        public bool writable { get; set; }

        public object schema { get; set; }

        public string name { get; set; }

        public string displayName { get; set; }

        public string description { get; set; }

        public string comment { get; set; }

        public string target { get; set; }
    }

    public class FlattenedModel
    {
        public string context { get; set; }

        public string id { get; set; }

        public string type { get; set; }

        public string displayname { get; set; }

        public string description { get; set; }

        public string comment { get; set; }

        public string contenttype { get; set; }

        public string contentid { get; set; }

        public bool writable { get; set; }

        public object schema { get; set; }

        public string contentname { get; set; }

        public string contentdisplayname { get; set; }

        public string contentdescription { get; set; }

        public string contentcomment { get; set; }

        public string target { get; set; }

        public string extends { get; set; }

        public object schemas { get; set; }
    }
}
