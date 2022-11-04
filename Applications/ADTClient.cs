
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
    using System.Threading.Tasks;
    using UACloudTwin.Interfaces;

    public class ADTClient : IDigitalTwinClient
    {
        private readonly ILogger<ADTClient> _logger;

        private DigitalTwinsClient _client;

        private bool _modelsUploaded = false;

        private object _containsLock = new object();

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
            _ = Task.Run(() =>
            {
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
                                metadata = _client.GetModel(modelIds[i]);
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
                    Response<DigitalTwinsModelData[]> response = _client.CreateModels(models);

                    _modelsUploaded = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Exception {ex.Message} uploading models!");
                }
            });
        }

        public void AddAsset(string assetName, string uaApplicationURI, string uaNamespaceURI, string publisherName)
        {
            if (!string.IsNullOrEmpty(assetName) && !string.IsNullOrEmpty(publisherName))
            {
                _ = Task.Run(() =>
                {
                    BasicDigitalTwin publisherTwin = new()
                    {
                        Id = DTDLEscapeString(publisherName),
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
                        Id = DTDLEscapeString(assetName),
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
        }

        public void UpdatePublishedNodes(string assetName, string publisherName, Dictionary<string, DataValue> publishedNodes)
        {
            if (!string.IsNullOrEmpty(assetName))
            {
                foreach (string publishedNodeId in publishedNodes.Keys)
                {
                    DataValue publishedNode = publishedNodes[publishedNodeId];

                    try
                    {
                        if (publishedNode?.Value != null)
                        {
                            // Update twin for each published node
                            UpdateNode(assetName, publishedNodeId, publishedNode.Value.ToString());
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogInformation($"Cannot add item {publishedNodeId}: {ex.Message}");
                    }
                }
            }
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

                        Pageable<BasicRelationship> existingRelationships = _client.GetRelationships<BasicRelationship>(DTDLEscapeString(parentId));

                        foreach (BasicRelationship existingRelationship in existingRelationships)
                        {
                            if ((existingRelationship.TargetId == DTDLEscapeString(childId)) && (existingRelationship.Name == "contains"))
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
                            Id = DTDLEscapeString(id),
                            SourceId = DTDLEscapeString(parentId),
                            TargetId = DTDLEscapeString(childId),
                            Name = "contains"
                        };

                        try
                        {
                            _client.CreateOrReplaceRelationship(DTDLEscapeString(parentId), DTDLEscapeString(id), relationship);
                        }
                        catch (RequestFailedException ex)
                        {
                            _logger.LogError("Error creating contains relationship: {parent} {child} {ex} {relationship}", DTDLEscapeString(parentId), DTDLEscapeString(childId), ex, JsonConvert.SerializeObject(relationship));
                        }
                    }
                }
            }
        }

        private void UpdateNode(string assetName, string publishedNodeId, string value)
        {
            if (!string.IsNullOrEmpty(publishedNodeId) && (!string.IsNullOrEmpty(assetName)))
            {
                _ = Task.Run(() =>
                {
                    BasicDigitalTwin twin = new()
                    {
                        Id = DTDLEscapeString(publishedNodeId),
                        Metadata =
                        {
                            ModelId = "dtmi:digitaltwins:opcua:node;1"
                        },
                        Contents =
                        {
                            { "tags", new Dictionary<string, object> {{ "$metadata", new {} }} },
                            { "equipmentLevel", "Work Unit" },
                            { "OPCUADisplayName","" },
                            { "OPCUANodeId","" },
                            { "OPCUANodeValue","" }
                        }
                    };

                    if (CreateTwinIfRequired(twin, assetName))
                    {
                        // update twin
                        var updateTwinData = new JsonPatchDocument();
                        try
                        {
                            string[] parts = publishedNodeId.Split('_');

                            updateTwinData.AppendReplace("/OPCUADisplayName", parts[2]);
                            updateTwinData.AppendReplace("/OPCUANodeId", parts[4]);
                            updateTwinData.AppendReplace("/OPCUANodeValue", value);

                            _client.UpdateDigitalTwinAsync(DTDLEscapeString(publishedNodeId), updateTwinData).GetAwaiter().GetResult();
                        }
                        catch (RequestFailedException ex)
                        {
                            _logger.LogError("Error updating node twin: {publishedNodeId} {ex} {twin}", DTDLEscapeString(publishedNodeId), ex, JsonConvert.SerializeObject(updateTwinData));
                        }
                    }
                });
            }
        }

        private bool TwinExists(string id)
        {
            try
            {
                Response<BasicDigitalTwin> twin = _client.GetDigitalTwin<BasicDigitalTwin>(DTDLEscapeString(id));
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
                    try
                    {
                        _client.CreateOrReplaceDigitalTwin(metaData.Id, metaData);

                        if (!string.IsNullOrEmpty(parent))
                        {
                            CreateContainsRelationshipIfRequired(metaData.Id, parent);
                        }

                        return true;
                    }
                    catch (RequestFailedException ex)
                    {
                        _logger.LogError("Error creating twin: {id} {ex} {twin}", DTDLEscapeString(metaData.Id), ex, JsonConvert.SerializeObject(metaData));
                        return false;
                    }
                }
                else
                {
                    if (!string.IsNullOrEmpty(parent))
                    {
                        CreateContainsRelationshipIfRequired(metaData.Id, parent);
                    }

                    return true;
                }
            }
            else
            {
                return false;
            }
        }

        private string DTDLEscapeString(string input)
        {
            return input.Replace(":", "").Replace(";", "_").Replace(".", "_").Replace("/", "").Replace("=", "");
        }
    }
}
