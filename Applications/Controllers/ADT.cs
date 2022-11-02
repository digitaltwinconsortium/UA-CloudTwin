
using Azure;
using Azure.DigitalTwins.Core;
using Azure.Identity;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using UACloudTwin.Interfaces;
using UACloudTwin.Models;

namespace UACloudTwin.Controllers
{
    public class ADT : Controller
    {
        private readonly ISubscriber _subscriber;

        public static DigitalTwinsClient ADTClient { get; private set; } = null;

        public ADT(ISubscriber subscriber)
        {
            _subscriber = subscriber;
        }

        public ActionResult Index()
        {
            ADTModel adtModel = new ADTModel
            {
                StatusMessage = ""
            };

            return View("Index", adtModel);
        }

        public ActionResult Privacy()
        {
            return View("Privacy");
        }

        [HttpPost]
        public ActionResult Login(string instanceUrl, string endpoint)
        {
            try
            {
                ADTClient = new DigitalTwinsClient(new Uri(instanceUrl), new DefaultAzureCredential());

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
                            metadata = ADTClient.GetModel(modelIds[i]);
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
                                ADTClient.DeleteModel(modelIds[i]);
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
                Response<DigitalTwinsModelData[]> response = ADTClient.CreateModels(models);

                if (!string.IsNullOrEmpty(endpoint))
                {
                    string[] parts = endpoint.Split(';');

                    Environment.SetEnvironmentVariable("BROKER_USERNAME", "$ConnectionString");
                    Environment.SetEnvironmentVariable("BROKER_PASSWORD", endpoint);
                    Environment.SetEnvironmentVariable("BROKER_PORT", "9093");
                    Environment.SetEnvironmentVariable("CLIENT_NAME", "microsoft");
                    Environment.SetEnvironmentVariable("BROKER_NAME", parts[0].Substring(parts[0].IndexOf('=') + 6).TrimEnd('/'));
                    Environment.SetEnvironmentVariable("TOPIC", parts[3].Substring(parts[3].IndexOf('=') + 1));
                }

                _subscriber.Connect();

                ADTModel adtModel = new ADTModel
                {
                    StatusMessage = "Connection to broker and ADT service successful!"
                };

                return View("Index", adtModel);
            }
            catch (Exception ex)
            {
                ADTModel adtModel = new ADTModel
                {
                    StatusMessage = ex.Message
                };

                return View("Index", adtModel);
            }
        }
    }
}
