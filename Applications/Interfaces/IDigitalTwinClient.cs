
namespace UACloudTwin.Interfaces
{
    using Opc.Ua;

    public interface IDigitalTwinClient
    {
        void Login(string instanceUrl);

        bool Ready { get; set; }

        void UploadTwinModels();

        void AddAsset(string assetName, string uaApplicationURI, string uaNamespaceURI, string publisherName);

        void UpdateAssetTelemetry(string assetName, string telemetryName, BuiltInType telemetryType, DataValue telemetryValue);
    }
}