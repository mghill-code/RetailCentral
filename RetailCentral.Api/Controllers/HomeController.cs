using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace RetailCentral.Api.Controllers
{
    public class HomeController : Controller
    {
        [AllowAnonymous]
        [HttpGet]
        public IActionResult AccessDenied(string? path = null)
        {
            ViewData["Path"] = path;
            ViewData["Title"] = "Access Denied";
            return View();
        }
    }
}