﻿
namespace UACloudTwin
{
    using Azure;
    using Azure.DigitalTwins.Core;
    using Azure.Identity;
    using Microsoft.Extensions.Logging;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using Opc.Ua;
    using System;
    using System.Collections.Generic;
    using System.IO;
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

        public void Login(string instanceUrl)
        {
            _client = new DigitalTwinsClient(new Uri(instanceUrl), new DefaultAzureCredential());

            // call an ADT method to verify login worked
            _client.GetModels();
        }

        public void UploadTwinModels()
        {
            // upload our models on a seperate thread as this takes a while
            while (!_modelsUploaded)
            {
                Thread.Sleep(5000);

                try
                {
                    // read our ISA95 models
                    List<string> models = new List<string>();
                    List<string> modelIds = new List<string>();

                    IEnumerable<string> files = Directory.EnumerateFiles(Path.Combine(Directory.GetCurrentDirectory(), "ISA95"), "*.json");
                    foreach (string dtdlFilePath in files)
                    {
                        // extract model definition
                        string modelDefinition = System.IO.File.ReadAllText(dtdlFilePath);
                        models.Add(modelDefinition);

                        // extract model ID
                        JObject elements = JsonConvert.DeserializeObject<JObject>(modelDefinition);
                        string modelId = elements.First.Next.First.ToString();
                        modelIds.Add(modelId);
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
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Exception {ex.Message} uploading models!");
                }
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
                BasicDigitalTwin publisherTwin = new()
                {
                    Id = DTDLEscapeAndTruncateString(publisherName),
                    Metadata =
                    {
                        ModelId = "dtmi:digitaltwins:isa95:Area;1"
                    },
                    Contents =
                    {
                        { "tags", new Dictionary<string, object> {{ "$metadata", new {} }} },
                        { "equipmentLevel", "Area" }
                    }
                };

                CreateTwinIfRequired(publisherTwin);

                BasicDigitalTwin twin = new()
                {
                    Id = DTDLEscapeAndTruncateString(assetName),
                    Metadata =
                    {
                        ModelId = "dtmi:digitaltwins:opcua:nodeset;1"
                    },
                    Contents =
                    {
                        { "tags", new Dictionary<string, object> {{ "$metadata", new {} }} },
                        { "OPCUAApplicationURI", uaApplicationURI },
                        { "OPCUANamespaceURI", uaNamespaceURI },
                        { "equipmentLevel", "Work Center" }
                    }
                };

                CreateTwinIfRequired(twin, publisherTwin.Id);
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
                    Id = DTDLEscapeAndTruncateString(telemetryName),
                    Contents =
                    {
                        { "tags", new Dictionary<string, object> {{ "$metadata", new {} }} },
                        { "equipmentLevel", "Work Unit" },
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

                if (CreateTwinIfRequired(twin, assetName))
                {
                    // update twin
                    var updateTwinData = new JsonPatchDocument();
                    try
                    {
                        string[] parts = telemetryName.Split('_');

                        if (parts.Length > 1)
                        {
                            updateTwinData.AppendReplace("/OPCUADisplayName", parts[1]);
                        }

                        if (parts.Length > 2)
                        {
                            updateTwinData.AppendReplace("/OPCUANodeId", parts[2]);
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

                        _client.UpdateDigitalTwinAsync(DTDLEscapeAndTruncateString(telemetryName), updateTwinData).GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Error updating node twin: {DTDLEscapeAndTruncateString(telemetryName)} {ex} {JsonConvert.SerializeObject(updateTwinData)}");
                    }
                }
            });
        }

        private void CreateContainsRelationshipIfRequired(string childId, string parentId)
        {
            if (_modelsUploaded && !string.IsNullOrEmpty(childId) && !string.IsNullOrEmpty(parentId) && TwinExists(parentId))
            {
                // serialize access to contains relationship check and creation to avoid duplicates due to race conditions
                lock (_containsLock)
                {
                    try
                    {
                        bool relationshipExists = false;

                        Pageable<BasicRelationship> existingRelationships = _client.GetRelationships<BasicRelationship>(DTDLEscapeAndTruncateString(parentId));

                        foreach (BasicRelationship existingRelationship in existingRelationships)
                        {
                            if ((existingRelationship.TargetId == DTDLEscapeAndTruncateString(childId)) && (existingRelationship.Name == "contains"))
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
                            Id = DTDLEscapeAndTruncateString(id),
                            SourceId = DTDLEscapeAndTruncateString(parentId),
                            TargetId = DTDLEscapeAndTruncateString(childId),
                            Name = "contains"
                        };

                        try
                        {
                            _client.CreateOrReplaceRelationship(DTDLEscapeAndTruncateString(parentId), DTDLEscapeAndTruncateString(id), relationship);
                        }
                        catch (RequestFailedException ex)
                        {
                            _logger.LogError($"Error creating contains relationship: {DTDLEscapeAndTruncateString(parentId)} {DTDLEscapeAndTruncateString(childId)} {ex} {JsonConvert.SerializeObject(relationship)}");
                        }
                    }
                }
            }
        }

        private bool TwinExists(string id)
        {
            try
            {
                Response<BasicDigitalTwin> twin = _client.GetDigitalTwin<BasicDigitalTwin>(DTDLEscapeAndTruncateString(id));
                return twin != null;
            }
            catch (RequestFailedException)
            {
                return false;
            }
        }

        private bool CreateTwinIfRequired(BasicDigitalTwin metaData, string parent = null)
        {
            if (_modelsUploaded)
            {
                // create only if it doesn't exist yet
                if (!TwinExists(metaData.Id))
                {
                    // lock this twin during creation to avoid race conditions
                    lock (_createLock)
                    {
                        if (!TwinExists(metaData.Id))
                        {
                            try
                            {
                                _client.CreateOrReplaceDigitalTwin(metaData.Id, metaData);
                            }
                            catch (RequestFailedException ex)
                            {
                                _logger.LogError($"Error creating twin: {DTDLEscapeAndTruncateString(metaData.Id)} {ex} {JsonConvert.SerializeObject(metaData)}");
                                return false;
                            }
                        }
                    }
                }

                if (!string.IsNullOrEmpty(parent))
                {
                    CreateContainsRelationshipIfRequired(metaData.Id, parent);
                }

                return true;
            }
            else
            {
                return false;
            }
        }

        private string DTDLEscapeAndTruncateString(string input)
        {
            string escapedString = input.Replace(":", "_").Replace(";", "_").Replace(".", "_").Replace("/", "_").Replace("\\", "_").Replace("=", "_");

            if (escapedString.Length >= 128)
            {
                return escapedString.Substring(0, 127);
            }
            else
            {
                return escapedString;
            }
        }
    }
}
