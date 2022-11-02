
namespace UACloudTwin
{
    using Azure;
    using Azure.DigitalTwins.Core;
    using Microsoft.AspNetCore.SignalR;
    using Microsoft.Extensions.Logging;
    using Newtonsoft.Json;
    using Opc.Ua;
    using Opc.Ua.PubSub;
    using Opc.Ua.PubSub.Encoding;
    using Opc.Ua.PubSub.PublishedData;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using UACloudTwin.Controllers;
    using UACloudTwin.Interfaces;

    public class UAPubSubMessageProcessor : IMessageProcessor
    {
        private StatusHubClient _hubClient;
        private readonly ILogger<UAPubSubMessageProcessor> _logger;
        private Dictionary<string, DataSetReaderDataType> _dataSetReaders;
        private Timer _throughputTimer;
        private int _messagesProcessed = 0;
        private DateTime _currentTimestamp = DateTime.MinValue;
        private string _chartCategory = "OPC UA PubSub Messages Per Second Processed";

        public UAPubSubMessageProcessor(IHubContext<StatusHub> hubContext, ILogger<UAPubSubMessageProcessor> logger)
        {
            _hubClient = new StatusHubClient(hubContext);
            _logger = logger;
            _dataSetReaders = new Dictionary<string, DataSetReaderDataType>();

            // add default dataset readers
            AddUadpDataSetReader("default_uadp", 0, new DataSetMetaDataType(), DateTime.UtcNow);
            AddJsonDataSetReader("default_json", 0, new DataSetMetaDataType(), DateTime.UtcNow);

            _throughputTimer = new Timer(MessagesProcessedCalculation, null, 10000, 10000);

            lock (_hubClient.ChartCategoryEntries)
            {
                if (_hubClient.ChartCategoryEntries.ContainsKey(_chartCategory))
                {
                    _hubClient.ChartCategoryEntries[_chartCategory] = new Tuple<string, string>(_chartCategory, "0");
                }
                else
                {
                    _hubClient.ChartCategoryEntries.Add(_chartCategory, new Tuple<string, string>(_chartCategory, "0"));
                }
            }
        }

        private void MessagesProcessedCalculation(object state)
        {
            string messagesPerSecondProcessed = (_messagesProcessed / 10).ToString();
            string timeStamp = _currentTimestamp.ToString();
            _logger.LogInformation("Processed " + messagesPerSecondProcessed + " messages/second, current message timestamp: " + timeStamp);

            lock (_hubClient.ChartEntries)
            {
                // create a keys array as index from our display names
                List<string> keys = new List<string>();
                foreach (string displayNameAsKey in _hubClient.ChartCategoryEntries.Keys)
                {
                    keys.Add(displayNameAsKey);
                }

                // check if we have to create an initially blank entry first
                if (!_hubClient.ChartEntries.ContainsKey(timeStamp) || (keys.Count != _hubClient.ChartEntries[timeStamp].Length))
                {
                    string[] blankValues = new string[_hubClient.ChartCategoryEntries.Count];
                    for (int i = 0; i < blankValues.Length; i++)
                    {
                        blankValues[i] = "NaN";
                    }

                    if (_hubClient.ChartEntries.ContainsKey(timeStamp))
                    {
                        _hubClient.ChartEntries.Remove(timeStamp);
                    }

                    _hubClient.ChartEntries.Add(timeStamp, blankValues);
                }

                _hubClient.ChartEntries[timeStamp][keys.IndexOf(_chartCategory)] = messagesPerSecondProcessed;
            }

            _messagesProcessed = 0;
        }

        public void Clear()
        {
            lock (_hubClient.TableEntries)
            {
                _hubClient.TableEntries.Clear();
            }
        }

        public void ProcessMessage(byte[] payload, DateTime receivedTime, string contentType)
        {
            _currentTimestamp = receivedTime;
            string message = string.Empty;

            try
            {
                message = Encoding.UTF8.GetString(payload);
                if (message != null)
                {
                    if (((contentType != null) && (contentType == "application/json")) || message.TrimStart().StartsWith('{') || message.TrimStart().StartsWith('['))
                    {
                        if (message.TrimStart().StartsWith('['))
                        {
                            // we received an array of messages
                            object[] messageArray = JsonConvert.DeserializeObject<object[]>(message);
                            foreach (object singleMessage in messageArray)
                            {
                                DecodeMessage(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(singleMessage)), receivedTime, new JsonNetworkMessage());
                            }
                        }
                        else
                        {
                            DecodeMessage(payload, receivedTime, new JsonNetworkMessage());
                        }
                    }
                    else
                    {
                        DecodeMessage(payload, receivedTime, new UadpNetworkMessage());
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Exception {ex.Message} processing message {message}");
            }
            finally
            {
                _messagesProcessed++;
            }
        }

        private void AddUadpDataSetReader(string publisherId, ushort dataSetWriterId, DataSetMetaDataType metadata, DateTime receivedTime)
        {
            DataSetReaderDataType uadpDataSetReader = new DataSetReaderDataType();
            uadpDataSetReader.Name = publisherId + ":" + dataSetWriterId.ToString();
            uadpDataSetReader.DataSetWriterId = dataSetWriterId;
            uadpDataSetReader.PublisherId = publisherId;
            uadpDataSetReader.Enabled = true;
            uadpDataSetReader.DataSetFieldContentMask = (uint)DataSetFieldContentMask.None;
            uadpDataSetReader.KeyFrameCount = 1;
            uadpDataSetReader.TransportSettings = new ExtensionObject(new BrokerDataSetReaderTransportDataType());
            uadpDataSetReader.DataSetMetaData = metadata;

            UadpDataSetReaderMessageDataType uadpDataSetReaderMessageSettings = new UadpDataSetReaderMessageDataType()
            {
                NetworkMessageContentMask = (uint)(UadpNetworkMessageContentMask.NetworkMessageNumber | UadpNetworkMessageContentMask.PublisherId | UadpNetworkMessageContentMask.DataSetClassId),
                DataSetMessageContentMask = (uint)UadpDataSetMessageContentMask.None,
            };

            uadpDataSetReader.MessageSettings = new ExtensionObject(uadpDataSetReaderMessageSettings);

            TargetVariablesDataType subscribedDataSet = new TargetVariablesDataType();
            subscribedDataSet.TargetVariables = new FieldTargetDataTypeCollection();
            uadpDataSetReader.SubscribedDataSet = new ExtensionObject(subscribedDataSet);

            if (_dataSetReaders.ContainsKey(uadpDataSetReader.Name))
            {
                _dataSetReaders[uadpDataSetReader.Name] = uadpDataSetReader;
            }
            else
            {
                _dataSetReaders.Add(uadpDataSetReader.Name, uadpDataSetReader);
            }

            CaptureAssetName(metadata, publisherId, receivedTime);
        }

        private void AddJsonDataSetReader(string publisherId, ushort dataSetWriterId, DataSetMetaDataType metadata, DateTime receivedTime)
        {
            DataSetReaderDataType jsonDataSetReader = new DataSetReaderDataType();
            jsonDataSetReader.Name = publisherId + ":" + dataSetWriterId.ToString();
            jsonDataSetReader.PublisherId = publisherId;
            jsonDataSetReader.DataSetWriterId = dataSetWriterId;
            jsonDataSetReader.Enabled = true;
            jsonDataSetReader.DataSetFieldContentMask = (uint)DataSetFieldContentMask.None;
            jsonDataSetReader.KeyFrameCount = 1;
            jsonDataSetReader.TransportSettings = new ExtensionObject(new BrokerDataSetReaderTransportDataType());
            jsonDataSetReader.DataSetMetaData = metadata;

            JsonDataSetReaderMessageDataType jsonDataSetReaderMessageSettings = new JsonDataSetReaderMessageDataType()
            {
                NetworkMessageContentMask = (uint)(JsonNetworkMessageContentMask.NetworkMessageHeader | JsonNetworkMessageContentMask.DataSetMessageHeader | JsonNetworkMessageContentMask.DataSetClassId | JsonNetworkMessageContentMask.PublisherId),
                DataSetMessageContentMask = (uint)JsonDataSetMessageContentMask.None,
            };

            jsonDataSetReader.MessageSettings = new ExtensionObject(jsonDataSetReaderMessageSettings);

            TargetVariablesDataType subscribedDataSet = new TargetVariablesDataType();
            subscribedDataSet.TargetVariables = new FieldTargetDataTypeCollection();
            jsonDataSetReader.SubscribedDataSet = new ExtensionObject(subscribedDataSet);

            if (_dataSetReaders.ContainsKey(jsonDataSetReader.Name))
            {
                _dataSetReaders[jsonDataSetReader.Name] = jsonDataSetReader;
            }
            else
            {
                _dataSetReaders.Add(jsonDataSetReader.Name, jsonDataSetReader);
            }

            CaptureAssetName(metadata, publisherId, receivedTime);
        }

        private void CaptureAssetName(DataSetMetaDataType metadata, string publisherName, DateTime receivedTime)
        {
            // try to extract the OPC UA asset name from the metadata name field (like UA Cloud Publisher supports)
            string assetName = string.Empty;
            try
            {
                if (metadata.Name != null)
                {
                    assetName = metadata.Name.Substring(0, metadata.Name.LastIndexOf(';'));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Exception {ex.Message} trying to extract OPC UA asset name from metadata name field");
            }

            if (!string.IsNullOrEmpty(assetName))
            {
                lock (_hubClient.TableEntries)
                {
                    if (_hubClient.TableEntries.ContainsKey(assetName))
                    {
                        _hubClient.TableEntries[assetName] = new Tuple<string, string>("OPC UA asset", receivedTime.ToString());
                    }
                    else
                    {
                        // add to our SignalR table
                        _hubClient.TableEntries.Add(assetName, new Tuple<string, string>("OPC UA asset", receivedTime.ToString()));

                        // add asset to ADT as digital twin
                        AddAssetToADT(assetName, publisherName);
                    }
                }
            }
        }

        private void AddAssetToADT(string assetName, string publisherName)
        {
            // create ISA95 Work Center for asset
            if (!string.IsNullOrEmpty(assetName))
            {
                try
                {
                    ADT.ADTClient?.GetDigitalTwin<BasicDigitalTwin>(DTDLEscapeString(assetName));
                }
                catch (Exception)
                {
                    BasicDigitalTwin twin = new()
                    {
                        Id = DTDLEscapeString(assetName),
                        Metadata =
                        {
                            ModelId = "dtmi:digitaltwins:isa95:WorkCenter;1"
                        },
                        Contents =
                        {
                            { "tags", new Dictionary<string, object> {{ "$metadata", new {} }} }
                        }
                    };

                    try
                    {
                        ADT.ADTClient?.CreateOrReplaceDigitalTwin(DTDLEscapeString(assetName), twin);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError("Error creating ADT twin: {nodeId} {ex} {twin}", DTDLEscapeString(assetName), ex, JsonConvert.SerializeObject(twin));
                    }
                }
            }

            // create ISA95 Area for publisher
            if (!string.IsNullOrEmpty(publisherName))
            {
                try
                {
                    ADT.ADTClient?.GetDigitalTwin<BasicDigitalTwin>(DTDLEscapeString(publisherName));
                }
                catch (Exception)
                {
                    BasicDigitalTwin twin = new()
                    {
                        Id = DTDLEscapeString(publisherName),
                        Metadata =
                        {
                            ModelId = "dtmi:digitaltwins:isa95:Area;1"
                        },
                        Contents =
                        {
                            { "tags", new Dictionary<string, object> {{ "$metadata", new {} }} }
                        }
                    };

                    try
                    {
                        ADT.ADTClient?.CreateOrReplaceDigitalTwin(DTDLEscapeString(publisherName), twin);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError("Error creating ADT twin: {nodeId} {ex} {twin}", DTDLEscapeString(publisherName), ex, JsonConvert.SerializeObject(twin));
                    }
                }
            }

            // create relationship between the two
            if (!string.IsNullOrEmpty(assetName) && !string.IsNullOrEmpty(publisherName))
            {
                string id = new Guid().ToString();
                BasicRelationship relationship = new()
                {
                    Id = id,
                    SourceId = DTDLEscapeString(publisherName),
                    TargetId = DTDLEscapeString(assetName),
                    Name = "contains"
                };

                try
                {
                    ADT.ADTClient?.CreateOrReplaceRelationship(DTDLEscapeString(publisherName), id, relationship);
                }
                catch (Exception ex)
                {
                    _logger.LogError("Error creating ADT relationship: {publisher} {asset} {ex} {relationship}", DTDLEscapeString(publisherName), DTDLEscapeString(assetName), ex, JsonConvert.SerializeObject(relationship));
                }
            }
        }

        private string DTDLEscapeString(string input)
        {
            return input.Replace(":", "_").Replace(";", "_").Replace(".", "_").Replace("/", "_");
        }

        private void DecodeMessage(byte[] payload, DateTime receivedTime, UaNetworkMessage encodedMessage)
        {
            encodedMessage.Decode(ServiceMessageContext.GlobalContext, payload, null);
            if (encodedMessage.IsMetaDataMessage)
            {
                // setup dataset reader
                if (encodedMessage is JsonNetworkMessage)
                {
                    JsonNetworkMessage jsonMessage = (JsonNetworkMessage)encodedMessage;

                    AddJsonDataSetReader(jsonMessage.PublisherId, jsonMessage.DataSetWriterId, encodedMessage.DataSetMetaData, receivedTime);
                }
                else
                {
                    UadpNetworkMessage uadpMessage = (UadpNetworkMessage)encodedMessage;
                    AddUadpDataSetReader(uadpMessage.PublisherId.ToString(), uadpMessage.DataSetWriterId, encodedMessage.DataSetMetaData, receivedTime);
                }
            }
            else
            {
                encodedMessage.Decode(ServiceMessageContext.GlobalContext, payload, _dataSetReaders.Values.ToArray());

                // reset metadata fields on default dataset readers
                _dataSetReaders["default_uadp:0"].DataSetMetaData.Fields.Clear();
                _dataSetReaders["default_json:0"].DataSetMetaData.Fields.Clear();

                string publisherID;
                if (encodedMessage is JsonNetworkMessage)
                {
                    publisherID = ((JsonNetworkMessage)encodedMessage).PublisherId?.ToString();
                }
                else
                {
                    publisherID = ((UadpNetworkMessage)encodedMessage).PublisherId?.ToString();
                }

                // now flatten any complex types
                foreach (UaDataSetMessage datasetmessage in encodedMessage.DataSetMessages)
                {
                    Dictionary<string, DataValue> flattenedPublishedNodes = new();

                    // extract the asset name
                    string dataSetWriterId = datasetmessage.DataSetWriterId.ToString();
                    string assetName = null;
                    if (_dataSetReaders.ContainsKey(publisherID + ":" + dataSetWriterId.ToString()))
                    {
                        string name = _dataSetReaders[publisherID + ":" + dataSetWriterId.ToString()].DataSetMetaData.Name;
                        assetName = name.Substring(0, name.LastIndexOf(';'));
                    }
                    else
                    {
                        _logger.LogInformation($"No metadata message for {publisherID}:{dataSetWriterId} received yet!");
                    }

                    if (datasetmessage.DataSet != null)
                    {
                        for (int i = 0; i < datasetmessage.DataSet.Fields.Count(); i++)
                        {
                            Field field = datasetmessage.DataSet.Fields[i];

                            if (field.Value != null)
                            {
                                if (field.Value.SourceTimestamp == DateTime.MinValue)
                                {
                                    field.Value.SourceTimestamp = datasetmessage.Timestamp;
                                }

                                if (field.FieldMetaData == null)
                                {
                                    if (field.Value.WrappedValue.Value is Variant[])
                                    {
                                        foreach (Variant variant in (Variant[])field.Value.WrappedValue.Value)
                                        {
                                            string[] keyValue = (string[])variant.Value;
                                            flattenedPublishedNodes.Add(publisherID + "_" + datasetmessage.DataSetWriterId.ToString() + "_field" + (i + 1).ToString() + "_" + keyValue[0], new DataValue(new Variant(keyValue[1])));
                                        }
                                    }
                                    else
                                    {
                                        flattenedPublishedNodes.Add(publisherID + "_" + datasetmessage.DataSetWriterId.ToString() + "_field" + (i + 1).ToString(), field.Value);
                                    }
                                }
                                else
                                {
                                    if (field.Value.WrappedValue.Value is Variant[])
                                    {
                                        int j = 0;
                                        foreach (Variant variant in (Variant[])field.Value.WrappedValue.Value)
                                        {
                                            if (variant.Value is string[])
                                            {
                                                string[] keyValue = (string[])variant.Value;
                                                if (keyValue != null)
                                                {
                                                    flattenedPublishedNodes.Add(publisherID + "_" + datasetmessage.DataSetWriterId.ToString() + "_" + field.FieldMetaData.Name + "_" + keyValue[0] + "_" + j.ToString(), new DataValue(new Variant(keyValue[1])));
                                                }
                                            }
                                            else
                                            {
                                                flattenedPublishedNodes.Add(publisherID + "_" + datasetmessage.DataSetWriterId.ToString() + "_" + field.FieldMetaData.Name + "_" + j.ToString(), new DataValue(new Variant(variant.Value.ToString())));
                                            }

                                            j++;
                                        }
                                    }
                                    else
                                    {
                                        string key = publisherID + "_" + datasetmessage.DataSetWriterId.ToString() + "_" + field.FieldMetaData.Name + "_field" + (i + 1).ToString();
                                        if (flattenedPublishedNodes.ContainsKey(key))
                                        {
                                            flattenedPublishedNodes[key] = field.Value;
                                        }
                                        else
                                        {
                                            flattenedPublishedNodes.Add(key, field.Value);
                                        }
                                    }
                                }

                            }
                        }
                    }

                    SendPublishedNodesToADT(assetName, flattenedPublishedNodes);
                }
            }
        }

        private void SendPublishedNodesToADT(string assetName, Dictionary<string, DataValue> publishedNodes)
        {
            if (assetName != null)
            {
                foreach (string publishedNodeId in publishedNodes.Keys)
                {
                    DataValue publishedNode = publishedNodes[publishedNodeId];

                    if (publishedNode == null) continue;

                    try
                    {
                        if (publishedNode.Value != null)
                        {
                            // Update ADT twin properties for each published node
                            _ = Task.Run(() => UpdateADTTwin(assetName, publishedNodeId, publishedNode.Value.ToString()));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogInformation($"Cannot add item {publishedNodeId}: {ex.Message}");
                    }
                }
            }
        }

        private void UpdateADTTwin(string assetName, string id, string value)
        {
            if (GetTwin(DTDLEscapeString(assetName), out BasicDigitalTwin twin))
            {
                var tags = twin.Contents as Dictionary<string, object>;
                if (tags != null)
                {
                    var tagValues = tags.ContainsKey("values")? tags["values"] as Dictionary<string, string> : new Dictionary<string, string>();

                    if (tagValues.ContainsKey(DTDLEscapeString(id)))
                    {
                        tagValues[DTDLEscapeString(id)] = value;
                    }
                    else
                    {
                        tagValues.Add(DTDLEscapeString(id), value);
                    }

                    JsonPatchDocument updateTwinData = new();
                    updateTwinData.AppendAdd($"/tags/values", tagValues);

                    // update twin with full patch document
                    ADT.ADTClient?.UpdateDigitalTwinAsync(DTDLEscapeString(assetName), updateTwinData).GetAwaiter().GetResult();
                }
            }
        }

        private bool GetTwin(string id, out BasicDigitalTwin twin)
        {
            try
            {
                twin = ADT.ADTClient?.GetDigitalTwin<BasicDigitalTwin>(id);
                return twin != null;
            }
            catch (Exception)
            {
                twin = null;
                return false;
            }
        }
    }
}
