using System.Diagnostics;
using HoleInHubReservationManagementSubsystem.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace HoleInHubReservationManagementSubsystem.Controllers;

public class HomeController : Controller
{
    public IActionResult Index()
    {
        return View();
    }

    public IActionResult About() => View();
    public IActionResult Gallery() => View();
    public IActionResult Contacts() => View();
    public IActionResult Services() => View();

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}

