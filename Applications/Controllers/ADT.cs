
using Azure.DigitalTwins.Core;
using Azure.Identity;
using Microsoft.AspNetCore.Mvc;
using System;
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
                ADTClient = new DigitalTwinsClient(new Uri(instanceUrl), new DefaultAzureCredential(true));

                if (!string.IsNullOrEmpty(endpoint))
                {
                    string[] parts = endpoint.Split(';');

                    Environment.SetEnvironmentVariable("USERNAME", "$ConnectionString");
                    Environment.SetEnvironmentVariable("PASSWORD", endpoint);
                    Environment.SetEnvironmentVariable("BROKER_PORT", "9093");
                    Environment.SetEnvironmentVariable("CLIENT_NAME", "microsoft");
                    Environment.SetEnvironmentVariable("BROKER_NAME", parts[0].Substring(parts[0].IndexOf('=') + 6).TrimEnd('/'));
                    Environment.SetEnvironmentVariable("TOPIC", parts[3].Substring(parts[3].IndexOf('=') + 1));
                }

                _subscriber.Connect();

                ADTModel adtModel = new ADTModel
                {
                    StatusMessage = "Login to ADT service and connection to broker successful!"
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
