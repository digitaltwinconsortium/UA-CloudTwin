
namespace UACloudTwin.Models
{
    using Opc.Ua;
    using System.Collections.Generic;

    public class OpcUaPubSubMessageModel
    {
        public List<Message> Messages { get; set; }
    }

    public class Message
    {
        public Dictionary<string, DataValue> Payload { get; set; }
    }
}
