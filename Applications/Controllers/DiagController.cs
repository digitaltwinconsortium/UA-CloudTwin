
namespace UACloudTwin.Controllers
{
    using Microsoft.AspNetCore.Mvc;
    using UACloudTwin.Interfaces;

    public class DiagController : Controller
    {
        private readonly IMessageProcessor _processor;

        public DiagController(IMessageProcessor processor)
        {
            _processor = processor;
        }

        public IActionResult Index()
        {
            _processor.Clear();

            return View();
        }
    }
}
