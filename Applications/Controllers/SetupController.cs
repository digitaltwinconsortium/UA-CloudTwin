
namespace UACloudTwin.Controllers
{
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Mvc;
    using System;
    using UACloudTwin.Interfaces;
    using UACloudTwin.Models;

    [Authorize]
    public class SetupController : Controller
    {
        private readonly IDigitalTwinClient _twinclient;

        public SetupController(IDigitalTwinClient twinClient)
        {
            _twinclient = twinClient;
        }

        public ActionResult Index()
        {
            SetupModel adtModel = new SetupModel
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
        public ActionResult Apply(string instanceUrl, string endpoint)
        {
            try
            {
                _twinclient.Login(instanceUrl);

                // check if an endpoint was supplied by the user
                if (!string.IsNullOrEmpty(endpoint))
                {
                    string[] parts = endpoint.Split(';');

                    Environment.SetEnvironmentVariable("BROKER_USERNAME", "$ConnectionString");
                    Environment.SetEnvironmentVariable("BROKER_PASSWORD", endpoint);
                    Environment.SetEnvironmentVariable("BROKER_PORT", "9093");
                    Environment.SetEnvironmentVariable("CLIENT_NAME", "uacloudtwin");

                    string brokerName = parts[0].Substring(parts[0].IndexOf('=') + 6).TrimEnd('/');

                    if (brokerName.StartsWith("iothub"))
                    {
                        Environment.SetEnvironmentVariable("TOPIC", parts[3].Substring(parts[3].IndexOf('=') + 1));
                    }
                    else
                    {
                        Environment.SetEnvironmentVariable("TOPIC", "data");
                        Environment.SetEnvironmentVariable("METADATA_TOPIC", "metadata");
                    }

                    Environment.SetEnvironmentVariable("BROKER_NAME", brokerName);
                }

                SetupModel adtModel = new SetupModel
                {
                    StatusMessage = "Settings applied successfully!"
                };

                return View("Index", adtModel);
            }
            catch (Exception ex)
            {
                SetupModel adtModel = new SetupModel
                {
                    StatusMessage = ex.Message
                };

                return View("Index", adtModel);
            }
        }
    }
}
