
namespace UACloudTwin
{
    using Kusto.Data;
    using Kusto.Data.Common;
    using Microsoft.Extensions.Logging;
    using Newtonsoft.Json;
    using Opc.Ua;
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Diagnostics;
    using System.IO;
    using System.IO.Compression;
    using System.Net.Http;
    using UACloudTwin.Interfaces;
    using UACloudTwin.Models;

    public class ADXClient : IDigitalTwinClient
    {
        public bool Ready { get; set; } = false;

        private readonly ILogger<ADTClient> _logger;

        private ICslQueryProvider _queryProvider = null;

        public ADXClient(ILogger<ADTClient> logger)
        {
            _logger = logger;
        }

        public void AddAsset(string assetName, string uaApplicationURI, string uaNamespaceURI, string publisherName)
        {
            ClientRequestProperties clientRequestProperties = new ClientRequestProperties()
            {
                ClientRequestId = Guid.NewGuid().ToString()
            };

            string query = "TODO!";
            Dictionary<string, object> values = new();

            try
            {
                using (IDataReader reader = _queryProvider?.ExecuteQuery(query, clientRequestProperties))
                {
                    while ((reader != null) && reader.Read())
                    {
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            try
                            {
                                if (reader.GetValue(i) != null)
                                {
                                    string value = reader.GetValue(i).ToString();
                                    if (value != null)
                                    {
                                        if (values.ContainsKey(value))
                                        {
                                            values[value] = reader.GetValue(i);
                                        }
                                        else
                                        {
                                            values.TryAdd(value, reader.GetValue(i));
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex.Message);

                                // ignore this field and move on
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
            }
        }

        public void Login(string instanceUrl)
        {
            try
            {
                string tenantId = Environment.GetEnvironmentVariable("AZURE_TENANT_ID");
                string applicationClientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");
                string applicationKey = Environment.GetEnvironmentVariable("AZURE_CLIENT_SECRET");

                string adxInstanceURL = Environment.GetEnvironmentVariable("ADX_INSTANCE_URL");
                string adxDatabaseName = Environment.GetEnvironmentVariable("ADX_DB_NAME");

                // acquire access to ADX token Kusto SDK
                if (!string.IsNullOrEmpty(adxInstanceURL) && !string.IsNullOrEmpty(adxDatabaseName) && !string.IsNullOrEmpty(applicationClientId))
                {
                    KustoConnectionStringBuilder connectionString;
                    if (!string.IsNullOrEmpty(applicationKey) && !string.IsNullOrEmpty(tenantId))
                    {
                        connectionString = new KustoConnectionStringBuilder(adxInstanceURL.Replace("https://", string.Empty), adxDatabaseName).WithAadApplicationKeyAuthentication(applicationClientId, applicationKey, tenantId);
                    }
                    else
                    {
                        connectionString = new KustoConnectionStringBuilder(adxInstanceURL, adxDatabaseName).WithAadUserManagedIdentity(applicationClientId);
                    }

                    _queryProvider = Kusto.Data.Net.Client.KustoClientFactory.CreateCslQueryProvider(connectionString);
                    if (_queryProvider == null)
                    {
                        throw new Exception("Could not create ADX query provider!");
                    }
                }
                else
                {
                    throw new ArgumentException("ADX environment variables not set!");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error logging in: {ex.Message}");
            }
        }

        public void UpdateAssetTelemetry(string assetName, string telemetryName, BuiltInType telemetryType, DataValue telemetryValue)
        {
            // nothing to do - this is handled via ADX data ingest directly!
        }

        public void UploadTwinModels()
        {
            Login(Environment.GetEnvironmentVariable("ADT_HOSTNAME"));

            string baseModelsDirectory = "ISA95BaseModels";
            List<string> models = new();

            // read the ISA95 models from the DTC's manufacturing ontologies repo
            HttpClient webClient = new HttpClient();
            HttpResponseMessage responseMessage = webClient.Send(new HttpRequestMessage(HttpMethod.Get, "https://github.com/digitaltwinconsortium/ManufacturingOntologies/archive/refs/heads/main.zip"));
            File.WriteAllBytes(Path.Combine(Directory.GetCurrentDirectory(), baseModelsDirectory + ".zip"), responseMessage.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult());
            webClient.Dispose();

            // unzip and read the models
            ZipFile.ExtractToDirectory(baseModelsDirectory + ".zip", baseModelsDirectory, true);
            RetrieveModelsFromDirectory(Path.Combine(baseModelsDirectory, "ManufacturingOntologies-main", "Ontologies", "ISA95"), models);

            // read our own ISA95 models
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("USE_ISA95_EQUIPMENT_MODELS")))
            {
                RetrieveModelsFromDirectory(Path.Combine(Directory.GetCurrentDirectory(), "ISA95Equipment"), models);
            }
            else
            {
                RetrieveModelsFromDirectory(Path.Combine(Directory.GetCurrentDirectory(), "ISA95"), models);
            }

            // upload the models
            foreach (string model in models)
            {
                DTDL deserializedModel = JsonConvert.DeserializeObject<DTDL>(model);

                // TODO
                Debug.WriteLine("DTDL ID:" + deserializedModel.id);

                if (deserializedModel.contents != null)
                {
                    foreach (Content content in deserializedModel.contents)
                    {
                        if (content.schema != null)
                        {
                            Debug.WriteLine("Schema: " + content.schema.ToString());
                        }
                    }
                }
            }
        }

        private void RetrieveModelsFromDirectory(string baseModelsDirectory, List<string> models)
        {
            EnumerationOptions options = new()
            {
                RecurseSubdirectories = true
            };

            foreach (string dtdlFilePath in Directory.EnumerateFiles(Path.Combine(Directory.GetCurrentDirectory(), baseModelsDirectory), "*.json", options))
            {
                // extract model definition
                string modelDefinition = File.ReadAllText(dtdlFilePath);
                models.Add(modelDefinition);
            }
        }
    }
}
