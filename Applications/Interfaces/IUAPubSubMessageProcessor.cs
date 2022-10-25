
namespace UACloudTwin.Interfaces
{
    using System;

    public interface IMessageProcessor
    {
        void ProcessMessage(byte[] payload, DateTime receivedTime, string contentType);
    }
}