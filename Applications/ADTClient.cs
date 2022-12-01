
namespace UACloudTwin
{
    using Azure;
    using Azure.DigitalTwins.Core;
    using Azure.Identity;
    using Extensions;
    using Microsoft.Extensions.Logging;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using Opc.Ua;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.Compression;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using UACloudTwin.Interfaces;

    public class ADTClient : IDigitalTwinClient
    {
        private readonly ILogger<ADTClient> _logger;

        private DigitalTwinsClient _client;

        private bool _modelsUploaded = false;

        private object _containsLock = new object();

        private object _createLock = new object();

        public ADTClient(ILogger<ADTClient> logger)
        {
            _logger = logger;
        }

        public bool Ready { get; set; } = false;

        public void Login(string instanceUrl)
        {
            _client = new DigitalTwinsClient(new Uri(instanceUrl), new DefaultAzureCredential());

            // call an ADT method to verify login worked
            _client.GetModels();
        }

        public void UploadTwinModels()
        {
            string baseModelsDirectory = "ISA95BaseModels";
            List<string> models = new List<string>();
            List<string> modelIds = new List<string>();

            // read the ISA95 models from the DTC's manufacturing ontologies repo
            HttpClient webClient = new HttpClient();
            HttpResponseMessage responseMessage = webClient.Send(new HttpRequestMessage(HttpMethod.Get, "https://github.com/digitaltwinconsortium/ManufacturingOntologies/archive/refs/heads/main.zip"));
            File.WriteAllBytes(Path.Combine(Directory.GetCurrentDirectory(), baseModelsDirectory + ".zip"), responseMessage.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult());
            webClient.Dispose();

            // unzip and read the models
            ZipFile.ExtractToDirectory(baseModelsDirectory + ".zip", baseModelsDirectory, true);
            RetrieveModelsFromDirectory(Path.Combine(baseModelsDirectory, "ManufacturingOntologies-main", "Ontologies", "ISA95", "CommonObjectModels"), models, modelIds);
            RetrieveModelsFromDirectory(Path.Combine(baseModelsDirectory, "ManufacturingOntologies-main", "Ontologies", "ISA95", "EquipmentHierarchy"), models, modelIds);
            RetrieveModelsFromDirectory(Path.Combine(baseModelsDirectory, "ManufacturingOntologies-main", "Ontologies", "ISA95", "Extensions"), models, modelIds);

            // read our own ISA95 models
            RetrieveModelsFromDirectory(Path.Combine(Directory.GetCurrentDirectory(), "ISA95"), models, modelIds);

            // upload the models
            while (!_modelsUploaded)
            {
                try
                {
                    if ((_client == null) && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ADT_HOSTNAME")))
                    {
                        Login(Environment.GetEnvironmentVariable("ADT_HOSTNAME"));
                    }

                    // delete existing models if they already exist
                    int numTriesRemaining = modelIds.Count;
                    while ((numTriesRemaining > 0) && (modelIds.Count > 0))
                    {
                        for (int i = 0; i < modelIds.Count; i++)
                        {
                            Response<DigitalTwinsModelData> metadata = null;
                            try
                            {
                                metadata = _client?.GetModel(modelIds[i]);
                            }
                            catch (RequestFailedException)
                            {
                                // model doesn't exist
                                modelIds.Remove(modelIds[i]);
                                i--;
                            }

                            if (metadata != null)
                            {
                                try
                                {
                                    _client.DeleteModel(modelIds[i]);
                                    modelIds.Remove(modelIds[i]);
                                    i--;
                                }
                                catch (RequestFailedException)
                                {
                                    // do nothing, since this could be due to a dependent model still not deleted,
                                    // so we will try to delete those first until numTriesRemaining is zero)
                                }
                            }
                        }

                        numTriesRemaining--;
                    }

                    // upload all models at once to make sure relationship checks succeed
                    Response<DigitalTwinsModelData[]> response = _client?.CreateModels(models);
                    if (response != null)
                    {
                        _logger.LogInformation("Digital twin models uploaded!");

                        _modelsUploaded = true;
                        Ready = true;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error uploading models: {ex.Message}");
                }

                Thread.Sleep(5000);
            }
        }

        private void RetrieveModelsFromDirectory(string baseModelsDirectory, List<string> models, List<string> modelIds)
        {
            foreach (string dtdlFilePath in Directory.EnumerateFiles(Path.Combine(Directory.GetCurrentDirectory(), baseModelsDirectory), "*.json"))
            {
                // extract model definition
                string modelDefinition = File.ReadAllText(dtdlFilePath);
                models.Add(modelDefinition);

                // extract model ID
                JObject elements = JsonConvert.DeserializeObject<JObject>(modelDefinition);
                string modelId = elements.First.Next.First.ToString();
                modelIds.Add(modelId);
            }
        }

        public void AddAsset(string assetName, string uaApplicationURI, string uaNamespaceURI, string publisherName)
        {
            if (string.IsNullOrEmpty(assetName) || string.IsNullOrEmpty(publisherName))
            {
                throw new ArgumentException();
            }

            _ = Task.Run(() =>
            {
                // create area twin for publisher
                BasicDigitalTwin publisherTwin = new()
                {
                    Id = publisherName.GetDeterministicHashCode().ToString(),
                    Metadata =
                    {
                        ModelId = "dtmi:digitaltwins:isa95:Area;1"
                    },
                    Contents =
                    {
                        { "tags", new Dictionary<string, object> {{ "$metadata", new {} }} },
                        { "equipmentLevel", "Area" },
                        { "equipmentID", publisherName },
                    }
                };

                CreateTwinIfRequired(publisherTwin, publisherName.GetDeterministicHashCode(), 0);

                // create nodeset twin for asset
                BasicDigitalTwin assetTwin = new()
                {
                    Id = assetName.GetDeterministicHashCode().ToString(),
                    Metadata =
                    {
                        ModelId = "dtmi:digitaltwins:opcua:nodeset;1"
                    },
                    Contents =
                    {
                        { "tags", new Dictionary<string, object> {{ "$metadata", new {} }} },
                        { "OPCUAApplicationURI", uaApplicationURI },
                        { "OPCUANamespaceURI", uaNamespaceURI },
                        { "equipmentLevel", "Work Center" },
                        { "equipmentID", assetName },
                    }
                };

                CreateTwinIfRequired(assetTwin, assetName.GetDeterministicHashCode(), publisherName.GetDeterministicHashCode());
            });
        }

        public void UpdateAssetTelemetry(string assetName, string telemetryName, BuiltInType telemetryType, DataValue telemetryValue)
        {
            if (string.IsNullOrEmpty(assetName) || string.IsNullOrEmpty(telemetryName) || (telemetryType == BuiltInType.Null) || (telemetryValue == null))
            {
                throw new ArgumentException();
            }

            _ = Task.Run(() =>
            {
                BasicDigitalTwin twin = new()
                {
                    Id = telemetryName.GetDeterministicHashCode().ToString(),
                    Contents =
                    {
                        { "tags", new Dictionary<string, object> {{ "$metadata", new {} }} },
                        { "equipmentLevel", "Work Unit" },
                        { "equipmentID", telemetryName },
                        { "OPCUADisplayName", string.Empty },
                        { "OPCUANodeId", string.Empty }
                    }
                };

                // map from OPC UA built-in types to DTDL primitive types
                // see https://reference.opcfoundation.org/v104/Core/docs/Part6/5.1.2/
                // and https://github.com/Azure/opendigitaltwins-dtdl/blob/master/DTDL/v2/dtdlv2.md#schemas
                switch (telemetryType)
                {
                    case BuiltInType.Boolean: twin.Metadata.ModelId = "dtmi:digitaltwins:opcua:node:boolean;1"; twin.Contents.Add("OPCUANodeValue", false); break;
                    case BuiltInType.DateTime: twin.Metadata.ModelId = "dtmi:digitaltwins:opcua:node:datetime;1"; twin.Contents.Add("OPCUANodeValue", DateTime.MinValue); break;
                    case BuiltInType.String: twin.Metadata.ModelId = "dtmi:digitaltwins:opcua:node:string;1"; twin.Contents.Add("OPCUANodeValue", string.Empty); break;
                    case BuiltInType.LocalizedText: twin.Metadata.ModelId = "dtmi:digitaltwins:opcua:node:string;1"; twin.Contents.Add("OPCUANodeValue", string.Empty); break;
                    case BuiltInType.SByte: twin.Metadata.ModelId = "dtmi:digitaltwins:opcua:node:integer;1"; twin.Contents.Add("OPCUANodeValue", 0); break;
                    case BuiltInType.Int16: twin.Metadata.ModelId = "dtmi:digitaltwins:opcua:node:integer;1"; twin.Contents.Add("OPCUANodeValue", 0); break;
                    case BuiltInType.Int32: twin.Metadata.ModelId = "dtmi:digitaltwins:opcua:node:integer;1"; twin.Contents.Add("OPCUANodeValue", 0); break;
                    case BuiltInType.Int64: twin.Metadata.ModelId = "dtmi:digitaltwins:opcua:node:long;1"; twin.Contents.Add("OPCUANodeValue", (long) 0); break;
                    case BuiltInType.Integer: twin.Metadata.ModelId = "dtmi:digitaltwins:opcua:node:integer;1"; twin.Contents.Add("OPCUANodeValue", 0); break;
                    case BuiltInType.Number: twin.Metadata.ModelId = "dtmi:digitaltwins:opcua:node:integer;1"; twin.Contents.Add("OPCUANodeValue", 0); break;
                    case BuiltInType.StatusCode: twin.Metadata.ModelId = "dtmi:digitaltwins:opcua:node:integer;1"; twin.Contents.Add("OPCUANodeValue", 0); break;
                    case BuiltInType.Byte: twin.Metadata.ModelId = "dtmi:digitaltwins:opcua:node:integer;1"; twin.Contents.Add("OPCUANodeValue", 0); break;
                    case BuiltInType.UInt16: twin.Metadata.ModelId = "dtmi:digitaltwins:opcua:node:integer;1"; twin.Contents.Add("OPCUANodeValue", 0); break;
                    case BuiltInType.UInt32: twin.Metadata.ModelId = "dtmi:digitaltwins:opcua:node:integer;1"; twin.Contents.Add("OPCUANodeValue", 0); break;
                    case BuiltInType.UInt64: twin.Metadata.ModelId = "dtmi:digitaltwins:opcua:node:long;1"; twin.Contents.Add("OPCUANodeValue", (long) 0); break;
                    case BuiltInType.UInteger: twin.Metadata.ModelId = "dtmi:digitaltwins:opcua:node:integer;1"; twin.Contents.Add("OPCUANodeValue", 0); break;
                    case BuiltInType.Float: twin.Metadata.ModelId = "dtmi:digitaltwins:opcua:node:float;1"; twin.Contents.Add("OPCUANodeValue", 0.0f); break;
                    case BuiltInType.Double: twin.Metadata.ModelId = "dtmi:digitaltwins:opcua:node:double;1"; twin.Contents.Add("OPCUANodeValue", 0.0); break;
                    case BuiltInType.Variant: twin.Metadata.ModelId = "dtmi:digitaltwins:opcua:node:string;1"; twin.Contents.Add("OPCUANodeValue", string.Empty); break;
                    case BuiltInType.DataValue: twin.Metadata.ModelId = "dtmi:digitaltwins:opcua:node:string;1"; twin.Contents.Add("OPCUANodeValue", string.Empty); break;
                    default: twin.Metadata.ModelId = "dtmi:digitaltwins:opcua:node:string;1"; twin.Contents.Add("OPCUANodeValue", string.Empty); break;
                }

                if (CreateTwinIfRequired(twin, telemetryName.GetDeterministicHashCode(), assetName.GetDeterministicHashCode()))
                {
                    // update twin
                    var updateTwinData = new JsonPatchDocument();
                    try
                    {
                        string[] parts = telemetryName.Split(';');

                        if (parts.Length > 1)
                        {
                            updateTwinData.AppendReplace("/OPCUADisplayName", parts[2]);
                        }

                        if (parts.Length > 2)
                        {
                            updateTwinData.AppendReplace("/OPCUANodeId", parts[1] + parts[3]);
                        }

                        // make sure we add the right value type
                        if (twin.Metadata.ModelId == "dtmi:digitaltwins:opcua:node:boolean;1")
                        {
                            updateTwinData.AppendReplace("/OPCUANodeValue", bool.Parse(telemetryValue.Value.ToString()));
                        }

                        if (twin.Metadata.ModelId == "dtmi:digitaltwins:opcua:node:datetime;1")
                        {
                            updateTwinData.AppendReplace("/OPCUANodeValue", DateTime.Parse(telemetryValue.Value.ToString()));
                        }

                        if (twin.Metadata.ModelId == "dtmi:digitaltwins:opcua:node:string;1")
                        {
                            // since string is our fallback/catchall, we need to check for nulls
                            if (!string.IsNullOrEmpty(telemetryValue?.Value?.ToString()))
                            {
                                updateTwinData.AppendReplace("/OPCUANodeValue", telemetryValue.Value.ToString());
                            }
                        }

                        if (twin.Metadata.ModelId == "dtmi:digitaltwins:opcua:node:integer;1")
                        {
                            updateTwinData.AppendReplace("/OPCUANodeValue", int.Parse(telemetryValue.Value.ToString()));
                        }

                        if (twin.Metadata.ModelId == "dtmi:digitaltwins:opcua:node:long;1")
                        {
                            updateTwinData.AppendReplace("/OPCUANodeValue", long.Parse(telemetryValue.Value.ToString()));
                        }

                        if (twin.Metadata.ModelId == "dtmi:digitaltwins:opcua:node:float;1")
                        {
                            updateTwinData.AppendReplace("/OPCUANodeValue", float.Parse(telemetryValue.Value.ToString()));
                        }

                        if (twin.Metadata.ModelId == "dtmi:digitaltwins:opcua:node:double;1")
                        {
                            updateTwinData.AppendReplace("/OPCUANodeValue", double.Parse(telemetryValue.Value.ToString()));
                        }

                        updateTwinData.AppendReplace("/$metadata/OPCUANodeValue/sourceTime", telemetryValue.SourceTimestamp.ToString("o"));

                        _client.UpdateDigitalTwinAsync(telemetryName.GetDeterministicHashCode().ToString(), updateTwinData).GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Error updating node twin: {telemetryName.GetDeterministicHashCode()} {ex} {JsonConvert.SerializeObject(updateTwinData)}");
                    }
                }
            });
        }

        private void CreateContainsRelationshipIfRequired(uint childId, uint parentId)
        {
            if (_modelsUploaded && TwinExists(parentId))
            {
                // serialize access to contains relationship check and creation to avoid duplicates due to race conditions
                lock (_containsLock)
                {
                    try
                    {
                        bool relationshipExists = false;

                        Pageable<BasicRelationship> existingRelationships = _client.GetRelationships<BasicRelationship>(parentId.ToString());

                        foreach (BasicRelationship existingRelationship in existingRelationships)
                        {
                            if ((existingRelationship.TargetId == childId.ToString()) && (existingRelationship.Name == "contains"))
                            {
                                relationshipExists = true;
                                break;
                            }
                        }

                        if (!relationshipExists)
                        {
                            throw new RequestFailedException("Relationship doesn't exist!");
                        }
                    }
                    catch (RequestFailedException)
                    {
                        string id = Guid.NewGuid().ToString();
                        BasicRelationship relationship = new()
                        {
                            Id = id,
                            SourceId = parentId.ToString(),
                            TargetId = childId.ToString(),
                            Name = "contains"
                        };

                        try
                        {
                            _client.CreateOrReplaceRelationship(parentId.ToString(), id, relationship);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"Error creating contains relationship: {parentId} {childId} {ex} {JsonConvert.SerializeObject(relationship)}");
                        }
                    }
                }
            }
        }

        private bool TwinExists(uint id)
        {
            try
            {
                Response<BasicDigitalTwin> twin = _client.GetDigitalTwin<BasicDigitalTwin>(id.ToString());
                return twin != null;
            }
            catch (RequestFailedException)
            {
                return false;
            }
        }

        private bool CreateTwinIfRequired(BasicDigitalTwin metaData, uint id, uint parentId)
        {
            if (_modelsUploaded)
            {
                // create only if it doesn't exist yet
                if (!TwinExists(id))
                {
                    // lock this twin during creation to avoid race conditions
                    lock (_createLock)
                    {
                        if (!TwinExists(id))
                        {
                            try
                            {
                                _client.CreateOrReplaceDigitalTwin(metaData.Id, metaData);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError($"Error creating twin: {id} {ex} {JsonConvert.SerializeObject(metaData)}");
                                return false;
                            }
                        }
                    }
                }

                if (parentId != 0)
                {
                    CreateContainsRelationshipIfRequired(id, parentId);
                }

                return true;
            }
            else
            {
                return false;
            }
        }
    }
}
