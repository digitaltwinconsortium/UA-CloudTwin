
namespace UACloudTwin.Controllers
{
    using Microsoft.AspNetCore.Mvc;
    using System;
    using UACloudTwin.Interfaces;
    using UACloudTwin.Models;

    public class SetupController : Controller
    {
        private readonly ISubscriber _subscriber;
        private readonly IDigitalTwinClient _twinclient;

        public SetupController(ISubscriber subscriber, IDigitalTwinClient twinClient)
        {
            _subscriber = subscriber;
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
        public ActionResult Login(string instanceUrl, string endpoint)
        {
            try
            {
                _twinclient.Login(instanceUrl);
                _twinclient.UploadTwinModels();

                // check if an endpoint was supplied by the user
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

                SetupModel adtModel = new SetupModel
                {
                    StatusMessage = "Connection to broker and Setup service successful!"
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
