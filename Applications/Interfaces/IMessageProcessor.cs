
namespace UACloudTwin.Interfaces
{
    using System;

    public interface IMessageProcessor
    {
        void Clear();

        void ProcessMessage(byte[] payload, DateTime receivedTime, string contentType);
    }
}