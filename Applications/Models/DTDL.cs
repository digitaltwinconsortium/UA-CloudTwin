
namespace UACloudTwin.Models
{
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
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

        public string schemas { get; set; }
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

        public Content request { get; set; }

        public Content response { get; set; }

        public string target { get; set; }
    }
}
