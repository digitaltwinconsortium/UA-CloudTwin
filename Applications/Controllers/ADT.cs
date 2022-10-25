
using Azure.DigitalTwins.Core;
using Azure.Identity;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.IO;
using UACloudTwin.Models;

namespace UACloudTwin.Controllers
{
    public class ADT : Controller
    {
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
        public ActionResult Upload(string instanceUrl, string tenantId, string clientId, string secret)
        {
            ADTModel adtModel = new ADTModel
            {
                InstanceUrl = instanceUrl,
                TenantId = tenantId,
                ClientId = clientId,
                Secret = secret
            };

            try
            {
                // login to Azure using the Environment variables mechanism of DefaultAzureCredential
                Environment.SetEnvironmentVariable("AZURE_CLIENT_ID", clientId);
                Environment.SetEnvironmentVariable("AZURE_TENANT_ID", tenantId);
                Environment.SetEnvironmentVariable("AZURE_CLIENT_SECRET", secret);

                // create ADT client instance
                DigitalTwinsClient client = new DigitalTwinsClient(new Uri(instanceUrl), new DefaultAzureCredential(new DefaultAzureCredentialOptions{ ExcludeVisualStudioCodeCredential = true }));

                // read generated DTDL models
                List<string> dtdlModels = new List<string>();
                foreach (string dtdlFilePath in Directory.EnumerateFiles(Path.Combine(Directory.GetCurrentDirectory(),"JSON"), "*.dtdl.json"))
                {
                    dtdlModels.Add(System.IO.File.ReadAllText(dtdlFilePath));
                }

                // upload
                Azure.Response<DigitalTwinsModelData[]> response = client.CreateModelsAsync(dtdlModels).GetAwaiter().GetResult();

                // clear secret
                Environment.SetEnvironmentVariable("AZURE_CLIENT_SECRET", "");

                if (response.GetRawResponse().Status == 201)
                {
                    adtModel.StatusMessage = "Upload successful!";
                    return View("Index", adtModel);
                }
                else
                {
                    adtModel.StatusMessage = response.GetRawResponse().ReasonPhrase;
                    return View("Index", adtModel);
                }
            }
            catch (Exception ex)
            {
                // clear secret
                Environment.SetEnvironmentVariable("AZURE_CLIENT_SECRET", "");

                adtModel.StatusMessage = ex.Message;
                return View("Index", adtModel);
            }
        }
    }
}
