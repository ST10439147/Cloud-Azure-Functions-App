using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using ST10439147_CLDV6212_POE.Models;

namespace ST10439147_CLDV6212_POE.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        public IActionResult Index()
        {
            return View();
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        public IActionResult Privacy()
        {
            return View();
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
//-----------------------------------------------------DDDDooooo END OF FILE oooooDDDD-----------------------------------------------------//