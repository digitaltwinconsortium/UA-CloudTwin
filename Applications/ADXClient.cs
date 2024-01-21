
namespace UACloudTwin
{
    using Kusto.Cloud.Platform.Utils;
    using Kusto.Data;
    using Kusto.Data.Common;
    using Kusto.Data.Ingestion;
    using Kusto.Data.Net.Client;
    using Kusto.Ingest;
    using Microsoft.Extensions.Logging;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using Opc.Ua;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.IO.Compression;
    using System.Linq;
    using System.Net.Http;
    using System.Text;
    using UACloudTwin.Interfaces;
    using UACloudTwin.Models;

    // Requires
    // .create table DTDL_models (context:string, id:string, type:string, displayname: string, description:string, comment:string, contenttype:string, contentid:string, writable:bool, schema:dynamic, stringcontentname:string, contentdisplayname: string, contentdescription:string, contentcomment:string, target:string, extends: string, schemas:string)
    // to be run on ADX cluster first!

    public class ADXClient : IDigitalTwinClient
    {
        public bool Ready { get; set; } = false;

        private readonly Microsoft.Extensions.Logging.ILogger<ADTClient> _logger;

        private IKustoIngestClient _ingestClient;
        private KustoIngestionProperties _ingestProperties;

        private static readonly ColumnMapping[] _jsonDTDLMapping = new ColumnMapping[]
        {
            // TODO
            new ColumnMapping { ColumnName = "EventText",  Properties = new Dictionary<string, string>{ { MappingConsts.Path, "$.EventText" },  { MappingConsts.TransformationMethod, CsvFromJsonStream_TransformationMethod.None.FastToString() } } },
            new ColumnMapping { ColumnName = "Properties", Properties = new Dictionary<string, string>{ { MappingConsts.Path, "$.Properties" }, { MappingConsts.TransformationMethod, CsvFromJsonStream_TransformationMethod.None.FastToString() } } },
        };

        public ADXClient(Microsoft.Extensions.Logging.ILogger<ADTClient> logger)
        {
            _logger = logger;
        }

        public void AddAsset(string assetName, string uaApplicationURI, string uaNamespaceURI, string publisherName)
        {
            // nothing to do - this is handled via ADX data ingest directly!
        }
        public void UpdateAssetTelemetry(string assetName, string telemetryName, BuiltInType telemetryType, DataValue telemetryValue)
        {
            // nothing to do - this is handled via ADX data ingest directly!
        }

        public void Login(string instanceUrl)
        {
            try
            {
                string tenantId = Environment.GetEnvironmentVariable("AZURE_TENANT_ID");
                string applicationClientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");
                string applicationKey = Environment.GetEnvironmentVariable("AZURE_CLIENT_SECRET");
                string adxDatabaseName = Environment.GetEnvironmentVariable("ADX_DB_NAME");
                string adxTableName = Environment.GetEnvironmentVariable("ADX_TABLE_NAME");

                // acquire access to ADX token Kusto SDK
                if (!string.IsNullOrEmpty(instanceUrl) && !string.IsNullOrEmpty(adxDatabaseName) && !string.IsNullOrEmpty(applicationClientId) && !string.IsNullOrEmpty(adxTableName))
                {
                    KustoConnectionStringBuilder connectionString;
                    if (!string.IsNullOrEmpty(applicationKey) && !string.IsNullOrEmpty(tenantId))
                    {
                        connectionString = new KustoConnectionStringBuilder(instanceUrl.Replace("https://", string.Empty), adxDatabaseName).WithAadApplicationKeyAuthentication(applicationClientId, applicationKey, tenantId);
                    }
                    else
                    {
                        connectionString = new KustoConnectionStringBuilder(instanceUrl, adxDatabaseName).WithAadUserManagedIdentity(applicationClientId);
                    }

                    const string _jsonMappingName = "DTDLJsonMapping";
                    using (ICslAdminProvider adminClient = KustoClientFactory.CreateCslAdminProvider(connectionString))
                    {
                        string showMappingsCommand = CslCommandGenerator.GenerateTableJsonMappingsShowCommand(adxTableName);
                        var existingMappings = adminClient.ExecuteControlCommand<IngestionMappingShowCommandResult>(adxDatabaseName, showMappingsCommand);

                        if (existingMappings.FirstOrDefault(m => string.Equals(m.Name, _jsonMappingName, StringComparison.Ordinal)) == null)
                        {
                            string createMappingCommand = CslCommandGenerator.GenerateTableMappingCreateCommand(IngestionMappingKind.Json, adxTableName, _jsonMappingName, _jsonDTDLMapping);
                            adminClient.ExecuteControlCommand(adxDatabaseName, createMappingCommand);
                        }
                    }

                    _ingestClient = KustoIngestFactory.CreateStreamingIngestClient(connectionString);
                    _ingestProperties = new KustoIngestionProperties(adxDatabaseName, adxTableName)
                    {
                        Format = DataSourceFormat.json,
                        IngestionMapping = new IngestionMapping { IngestionMappingKind = IngestionMappingKind.Json, IngestionMappingReference = _jsonMappingName }
                    };
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

        public void UploadTwinModels()
        {
            try
            {
                Login(Environment.GetEnvironmentVariable("ADX_INSTANCE_URL"));

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
                foreach (string rawmodel in models)
                {
                    DTDL model = JsonConvert.DeserializeObject<DTDL>(rawmodel);
                    if (model.contents != null)
                    {
                        foreach (Content content in model.contents)
                        {
                            FlattenedModel newModel = new()
                            {
                                context = model.context,
                                id = model.id,
                                type = model.type,
                                displayname = model.displayName,
                                description = model.description,
                                comment = model.comment,
                                contenttype = content.type,
                                contentid = content.id,
                                writable = content.writable,
                                schema = content.schema,
                                contentname = content.name,
                                contentdisplayname = content.displayName,
                                contentdescription = content.description,
                                contentcomment = content.comment,
                                target = content.target,
                                extends = model.extends?[0],
                                schemas = model.schemas
                            };

                            ADXIngest(JsonConvert.SerializeObject(newModel));

                            if (model.extends.Count > 1)
                            {
                                for (int i = 1; i < model.extends.Count; i++)
                                {
                                    FlattenedModel newModel1 = new()
                                    {
                                        context = model.context,
                                        id = model.id,
                                        type = model.type,
                                        displayname = model.displayName,
                                        description = model.description,
                                        comment = model.comment,
                                        contenttype = content.type,
                                        contentid = content.id,
                                        writable = content.writable,
                                        schema = content.schema,
                                        contentname = content.name,
                                        contentdisplayname = content.displayName,
                                        contentdescription = content.description,
                                        contentcomment = content.comment,
                                        target = content.target,
                                        extends = model.extends[i],
                                        schemas = model.schemas
                                    };

                                    ADXIngest(JsonConvert.SerializeObject(newModel1));
                                }
                            }
                        }
                    }
                }

                Ready = true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error uploading DTDL models to ADX: {ex.Message}");
            }
        }

        private void ADXIngest(string data)
        {
            using (MemoryStream stream = new(Encoding.UTF8.GetBytes(data)))
            {
                _ingestClient.IngestFromStream(stream, _ingestProperties);
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
