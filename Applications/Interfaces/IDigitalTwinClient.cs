
namespace UACloudTwin.Interfaces
{
    using Opc.Ua;
    using System.Collections.Generic;

    public interface IDigitalTwinClient
    {
        void Login(string instanceUrl);

        void UploadTwinModels();

        void AddAsset(string assetName, string uaApplicationURI, string uaNamespaceURI, string publisherName);

        void UpdateAssetTelemetry(string assetName, string publisherName, Dictionary<string, DataValue> telemetry);
    }
}