
namespace UACloudTwin
{
    using Microsoft.AspNetCore.SignalR;
    using Newtonsoft.Json;
    using Opc.Ua;
    using UACloudTwin.Models;
    using Opc.Ua.PubSub;
    using Opc.Ua.PubSub.Encoding;
    using Opc.Ua.PubSub.PublishedData;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Text;

    public class UAPubSubMessageProcessor : IUAPubSubMessageProcessor
    {
        private StatusHubClient _hubClient;
        private Dictionary<string, DataSetReaderDataType> _dataSetReaders;

        public UAPubSubMessageProcessor()
        {
            IServiceProvider serviceProvider = Program.AppHost.Services;
            _hubClient = new StatusHubClient((IHubContext<StatusHub>)serviceProvider.GetService(typeof(IHubContext<StatusHub>)));
            _dataSetReaders = new Dictionary<string, DataSetReaderDataType>();

            // add default dataset readers
            AddUadpDataSetReader("default_uadp", 0, new DataSetMetaDataType());
            AddJsonDataSetReader("default_json", 0, new DataSetMetaDataType());
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
            string message = string.Empty;
            try
            {
                message = Encoding.UTF8.GetString(payload);
                if (message != null)
                {
#if DEBUG
                    Trace.TraceInformation($"Received Message {message}");
#endif
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
                Trace.TraceError($"Exception {ex.Message} processing message {message}");
            }
        }

        private void AddUadpDataSetReader(string publisherId, ushort dataSetWriterId, DataSetMetaDataType metadata)
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

            TargetVariablesDataType subscribedDataSet2 = new TargetVariablesDataType();
            subscribedDataSet2.TargetVariables = new FieldTargetDataTypeCollection();
            uadpDataSetReader.SubscribedDataSet = new ExtensionObject(subscribedDataSet2);

            if (_dataSetReaders.ContainsKey(uadpDataSetReader.Name))
            {
                _dataSetReaders[uadpDataSetReader.Name] = uadpDataSetReader;
            }
            else
            {
                _dataSetReaders.Add(uadpDataSetReader.Name, uadpDataSetReader);
            }
        }

        private void AddJsonDataSetReader(string publisherId, ushort dataSetWriterId, DataSetMetaDataType metadata)
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

            TargetVariablesDataType subscribedDataSet1 = new TargetVariablesDataType();
            subscribedDataSet1.TargetVariables = new FieldTargetDataTypeCollection();
            jsonDataSetReader.SubscribedDataSet = new ExtensionObject(subscribedDataSet1);

            if (_dataSetReaders.ContainsKey(jsonDataSetReader.Name))
            {
                _dataSetReaders[jsonDataSetReader.Name] = jsonDataSetReader;
            }
            else
            {
                _dataSetReaders.Add(jsonDataSetReader.Name, jsonDataSetReader);
            }
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

                    AddJsonDataSetReader(jsonMessage.PublisherId, jsonMessage.DataSetWriterId, encodedMessage.DataSetMetaData);
                }
                else
                {
                    UadpNetworkMessage uadpMessage = (UadpNetworkMessage)encodedMessage;
                    AddUadpDataSetReader(uadpMessage.PublisherId.ToString(), uadpMessage.DataSetWriterId, encodedMessage.DataSetMetaData);
                }
            }
            else
            {
                encodedMessage.Decode(ServiceMessageContext.GlobalContext, payload, _dataSetReaders.Values.ToArray());

                // reset metadata fields on default dataset readers
                _dataSetReaders["default_uadp:0"].DataSetMetaData.Fields.Clear();
                _dataSetReaders["default_json:0"].DataSetMetaData.Fields.Clear();

                string publisherID = string.Empty;
                if (encodedMessage is JsonNetworkMessage)
                {
                    publisherID = ((JsonNetworkMessage)encodedMessage).PublisherId?.ToString();
                }
                else
                {
                    publisherID = ((UadpNetworkMessage)encodedMessage).PublisherId?.ToString();
                }

                OpcUaPubSubMessageModel publisherMessage = new OpcUaPubSubMessageModel();
                publisherMessage.Messages = new List<Message>();
                foreach (UaDataSetMessage datasetmessage in encodedMessage.DataSetMessages)
                {
                    Message pubSubMessage = new Message();
                    pubSubMessage.Payload = new Dictionary<string, DataValue>();
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
                                            pubSubMessage.Payload.Add(publisherID + "_" + datasetmessage.DataSetWriterId.ToString() + "_field" + (i + 1).ToString() + "_" + keyValue[0], new DataValue(new Variant(keyValue[1])));
                                        }
                                    }
                                    else
                                    {
                                        pubSubMessage.Payload.Add(publisherID + "_" + datasetmessage.DataSetWriterId.ToString() + "_field" + (i + 1).ToString(), field.Value);
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
                                                    pubSubMessage.Payload.Add(publisherID + "_" + datasetmessage.DataSetWriterId.ToString() + "_" + field.FieldMetaData.Name + "_" + keyValue[0] + "_" + j.ToString(), new DataValue(new Variant(keyValue[1])));
                                                }
                                            }
                                            else
                                            {
                                                pubSubMessage.Payload.Add(publisherID + "_" + datasetmessage.DataSetWriterId.ToString() + "_" + field.FieldMetaData.Name + "_" + j.ToString(), new DataValue(new Variant(variant.Value.ToString())));
                                            }

                                            j++;
                                        }
                                    }
                                    else
                                    {
                                        pubSubMessage.Payload.Add(publisherID + "_" + datasetmessage.DataSetWriterId.ToString() + "_" + field.FieldMetaData.Name + "_field" + (i + 1).ToString(), field.Value);
                                    }
                                }

                            }
                        }

                        publisherMessage.Messages.Add(pubSubMessage);
                    }
                }

                ProcessPublisherMessage(publisherMessage, receivedTime);
            }
        }

        private void ProcessPublisherMessage(OpcUaPubSubMessageModel publisherMessage, DateTime enqueueTime)
        {
            Dictionary<string, string> displayNameMap = new Dictionary<string, string>(); // TODO: Add display name substitudes here!

            // unbatch the received data
            if (publisherMessage.Messages != null)
            {
                foreach (Message message in publisherMessage.Messages)
                {
                    foreach (string nodeId in message.Payload.Keys)
                    {
                        // substitude the node Id with a custom display name, if available
                        string displayName = nodeId;
                        try
                        {
                            if (displayNameMap.Count > 0)
                            {
                                displayName = displayNameMap[nodeId];
                            }
                        }
                        catch
                        {
                            // keep the original node ID as the display name
                        }

                        if (message.Payload[nodeId] != null)
                        {
                            if (message.Payload[nodeId].SourceTimestamp == DateTime.MinValue)
                            {
                                // use the enqueued time if the OPC UA timestamp is not present
                                message.Payload[nodeId].SourceTimestamp = enqueueTime;
                            }

                            try
                            {
                                string timeStamp = message.Payload[nodeId].SourceTimestamp.ToString();
                                if (message.Payload[nodeId].Value != null)
                                {
                                    string value = message.Payload[nodeId].Value.ToString();

                                    lock (_hubClient.TableEntries)
                                    {
                                        if (_hubClient.TableEntries.ContainsKey(displayName))
                                        {
                                            _hubClient.TableEntries[displayName] = new Tuple<string, string>(value, timeStamp);
                                        }
                                        else
                                        {
                                            _hubClient.TableEntries.TryAdd(displayName, new Tuple<string, string>(value, timeStamp));
                                        }

                                        float floatValue;
                                        if (float.TryParse(value, out floatValue))
                                        {
                                            // create a keys array as index from our display names
                                            List<string> keys = new List<string>();
                                            foreach (string displayNameAsKey in _hubClient.TableEntries.Keys)
                                            {
                                                keys.Add(displayNameAsKey);
                                            }

                                            // check if we have to create an initially blank entry first
                                            if (!_hubClient.ChartEntries.ContainsKey(timeStamp) || (keys.Count != _hubClient.ChartEntries[timeStamp].Length))
                                            {
                                                string[] blankValues = new string[_hubClient.TableEntries.Count];
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

                                            _hubClient.ChartEntries[timeStamp][keys.IndexOf(displayName)] = floatValue.ToString();
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                // ignore this item
                                Trace.TraceInformation($"Cannot add item {nodeId}: {ex.Message}");
                            }
                        }
                    }
                }
            }
        }
    }
}
