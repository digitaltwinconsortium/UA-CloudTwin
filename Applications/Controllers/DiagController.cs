
namespace UACloudTwin.Controllers
{
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Mvc;

    [Authorize]
    public class DiagController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
