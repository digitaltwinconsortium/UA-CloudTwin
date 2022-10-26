
using Azure.DigitalTwins.Core;
using Azure.Identity;
using Microsoft.AspNetCore.Mvc;
using System;
using UACloudTwin.Models;

namespace UACloudTwin.Controllers
{
    public class ADT : Controller
    {
        public static DigitalTwinsClient ADTClient { get; private set; } = null;

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
                ADTClient = new DigitalTwinsClient(new Uri(instanceUrl), new DefaultAzureCredential(new DefaultAzureCredentialOptions{ ExcludeVisualStudioCodeCredential = true }));

                // clear secret
                Environment.SetEnvironmentVariable("AZURE_CLIENT_SECRET", "");

                adtModel.StatusMessage = "Login successful!";
                return View("Index", adtModel);
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
