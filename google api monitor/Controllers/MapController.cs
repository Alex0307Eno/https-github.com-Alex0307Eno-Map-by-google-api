using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Map.Models;

namespace Map.Controllers
{

    public class MapController : Controller
    {
        private readonly GoogleMapsSettings _maps;
        public MapController(IOptions<GoogleMapsSettings> maps) { _maps = maps.Value; }

        public IActionResult Index()
        {
            ViewBag.GoogleMapsKey = _maps.ApiKey;
            return View();
        }
    }
}
