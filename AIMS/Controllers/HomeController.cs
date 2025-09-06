using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using AIMS.Models;
using System.Threading.Tasks;

namespace AIMS.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly UserQuery _userQuery;
    private readonly HardwareQuery _hardwareQuery;
    private readonly SoftwareQuery _softwareQuery;
    private readonly AssignmentsQuery _assignQuery;

    public HomeController(ILogger<HomeController> logger, UserQuery userQuery, HardwareQuery hardwareQuery, SoftwareQuery softwareQuery, AssignmentsQuery assignQuery)
    {
        _logger = logger;
        _userQuery = userQuery;
        _hardwareQuery = hardwareQuery;
        _softwareQuery = softwareQuery;
        _assignQuery = assignQuery;
    }

    public async Task<IActionResult> Index()
    {
        var hardware = await _hardwareQuery.GetAllHardwareAsync();
        var software = await _softwareQuery.GetAllSoftwareAsync();
        var users = await _userQuery.GetAllUsersAsync();

        var model = new HomeIndexViewModel
        {
            Hardware = hardware,
            Software = software,
        };
        return View(model);
    }


    public IActionResult Reports()
    {
        return View();
    }


    public IActionResult Search(string? searchQuery)
    {
        if (!string.IsNullOrEmpty(searchQuery))
        {
            ViewBag.SearchQuery = searchQuery;
        }
        else
        {
            ViewBag.SearchQuery = "";
        }
        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }

    public IActionResult Feedback()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

    public IActionResult HomePageCardComponent()
    {
        return View();
    }

    public IActionResult CardComponent()
    {
        return View();
    }

    public IActionResult AssetDetailsComponent(string category)
    {
        this.ViewData["Category"] = category;

        var tableHeaders = new List<string> { "Asset Name", "Type", "Tag #", "Status" };

        var tableData = new List<Dictionary<string, string>> {
         new() { {"Asset Name", "Lenovo ThinkPad E16"}, {"Type", "Laptop"}, {"Tag #", "LT-0020"}, {"Status", "Assigned"} },
            new() { {"Asset Name", "Dell S2421NX"}, {"Type", "Monitor"}, {"Tag #", "MN-0001"}, {"Status", "Assigned"} },
            new() { {"Asset Name", "HP EliteBook 840 G7"}, {"Type", "Laptop"}, {"Tag #", "DT-0011"}, {"Status", "Surveyed"} },
            new() { {"Asset Name", "iMac Pro"}, {"Type", "Desktop"}, {"Tag #", "DT-2020"}, {"Status", "Assigned"} },
            new() { {"Asset Name", "Macbook Pro 2021"}, {"Type", "Laptop"}, {"Tag #", "SW-0100"}, {"Status", "Assigned"} },
            new() { {"Asset Name", "Logitech Zone 300"}, {"Type", "Headset"}, {"Tag #", "HS-0080"}, {"Status", "Available"} },
            new() { {"Asset Name", "USB-C Cable"}, {"Type", "Charging Cable"}, {"Tag #", "CC-0090"}, {"Status", "Available"} },
            new() { {"Asset Name", "Dell XPS 13"}, {"Type", "Laptop"}, {"Tag #", "LT-1010"}, {"Status", "Assigned"} },
            new() { {"Asset Name", "HP Spectre x360"}, {"Type", "Laptop"}, {"Tag #", "LT-1021"}, {"Status", "Available"} },
            new() { {"Asset Name", "Lenovo Yoga Slim 7"}, {"Type", "Laptop"}, {"Tag #", "LT-1035"}, {"Status", "In Repair"} },
            new() { {"Asset Name", "Apple MacBook Pro 14\""}, {"Type", "Laptop"}, {"Tag #", "LT-1048"}, {"Status", "Surveyed"} },
            new() { {"Asset Name", "Microsoft Surface Laptop 5"}, {"Type", "Laptop"}, {"Tag #", "LT-1059"}, {"Status", "Assigned"} },
            new() { {"Asset Name", "Acer Swift 3"}, {"Type", "Laptop"}, {"Tag #", "LT-1073"}, {"Status", "Available"} },
            new() { {"Asset Name", "Asus ZenBook 14"}, {"Type", "Laptop"}, {"Tag #", "LT-1082"}, {"Status", "Surveyed"} },
            new() { {"Asset Name", "Razer Blade Stealth 13"}, {"Type", "Laptop"}, {"Tag #", "LT-1090"}, {"Status", "In Repair"} },
            new() { {"Asset Name", "Samsung Galaxy Book3 Pro"}, {"Type", "Laptop"}, {"Tag #", "LT-1104"}, {"Status", "Assigned"} },
            new() { {"Asset Name", "LG Gram 16"}, {"Type", "Laptop"}, {"Tag #", "LT-1111"}, {"Status", "Available"} },
            new() { {"Asset Name", "Logitech H390 USB Headset"}, {"Type", "Headset"}, {"Tag #", "HS-2001"}, {"Status", "Assigned"} },
            new() { {"Asset Name", "Jabra Evolve 40"}, {"Type", "Headset"}, {"Tag #", "HS-2002"}, {"Status", "Available"} },
            new() { {"Asset Name", "Plantronics Blackwire 5220"}, {"Type", "Headset"}, {"Tag #", "HS-2003"}, {"Status", "In Repair"} },
            new() { {"Asset Name", "Logitech Zone Wired"}, {"Type", "Headset"}, {"Tag #", "HS-2004"}, {"Status", "Surveyed"} },
            new() { {"Asset Name", "Corsair HS60 Pro"}, {"Type", "Headset"}, {"Tag #", "HS-2005"}, {"Status", "Assigned"} },
            new() { {"Asset Name", "Sennheiser SC 165"}, {"Type", "Headset"}, {"Tag #", "HS-2006"}, {"Status", "Available"} },
            new() { {"Asset Name", "HyperX Cloud II"}, {"Type", "Headset"}, {"Tag #", "HS-2007"}, {"Status", "In Repair"} },
            new() { {"Asset Name", "Sony WH-CH710N"}, {"Type", "Headset"}, {"Tag #", "HS-2008"}, {"Status", "Surveyed"} },
            new() { {"Asset Name", "Bose 700 UC"}, {"Type", "Headset"}, {"Tag #", "HS-2009"}, {"Status", "Available"} },
            new() { {"Asset Name", "EPOS Adapt 260"}, {"Type", "Headset"}, {"Tag #", "HS-2010"}, {"Status", "Assigned"} },
            new() { {"Asset Name", "Microsoft Office 365"}, {"Type", "Software"}, {"Tag #", "SW-3001"}, {"Status", "Assigned"} },
            new() { {"Asset Name", "Adobe Creative Cloud"}, {"Type", "Software"}, {"Tag #", "SW-3002"}, {"Status", "Assigned"} },
            new() { {"Asset Name", "AutoCAD 2024"}, {"Type", "Software"}, {"Tag #", "SW-3003"}, {"Status", "Assigned"} },
            new() { {"Asset Name", "Visual Studio 2022 Pro"}, {"Type", "Software"}, {"Tag #", "SW-3004"}, {"Status", "Assigned"} },
            new() { {"Asset Name", "Slack Enterprise"}, {"Type", "Software"}, {"Tag #", "SW-3005"}, {"Status", "Assigned"} },
            new() { {"Asset Name", "Zoom Pro"}, {"Type", "Software"}, {"Tag #", "SW-3006"}, {"Status", "Assigned"} },
            new() { {"Asset Name", "Cisco WebEx"}, {"Type", "Software"}, {"Tag #", "SW-3007"}, {"Status", "Assigned"} },
            new() { {"Asset Name", "Notion Team Plan"}, {"Type", "Software"}, {"Tag #", "SW-3008"}, {"Status", "Assigned"} },
            new() { {"Asset Name", "IntelliJ IDEA Ultimate"}, {"Type", "Software"}, {"Tag #", "SW-3009"}, {"Status", "Assigned"} },
            new() { {"Asset Name", "Jira Software Cloud"}, {"Type", "Software"}, {"Tag #", "SW-3010"}, {"Status", "Assigned"} },
            new() { {"Asset Name", "Dell UltraSharp U2723QE"}, {"Type", "Monitor"}, {"Tag #", "MN-4001"}, {"Status", "Available"} },
            new() { {"Asset Name", "HP E24 G4 FHD"}, {"Type", "Monitor"}, {"Tag #", "MN-4002"}, {"Status", "Assigned"} },
            new() { {"Asset Name", "Samsung Smart Monitor M7"}, {"Type", "Monitor"}, {"Tag #", "MN-4003"}, {"Status", "In Repair"} },
            new() { {"Asset Name", "Asus ProArt Display"}, {"Type", "Monitor"}, {"Tag #", "MN-4004"}, {"Status", "Surveyed"} },
            new() { {"Asset Name", "LG 27UN850-W 4K"}, {"Type", "Monitor"}, {"Tag #", "MN-4005"}, {"Status", "Available"} },
            new() { {"Asset Name", "ViewSonic VG2455"}, {"Type", "Monitor"}, {"Tag #", "MN-4006"}, {"Status", "Surveyed"} },
            new() { {"Asset Name", "BenQ PD2700U"}, {"Type", "Monitor"}, {"Tag #", "MN-4007"}, {"Status", "Assigned"} },
            new() { {"Asset Name", "Acer R240HY"}, {"Type", "Monitor"}, {"Tag #", "MN-4008"}, {"Status", "Available"} },
            new() { {"Asset Name", "Gigabyte M28U"}, {"Type", "Monitor"}, {"Tag #", "MN-4009"}, {"Status", "In Repair"} },
            new() { {"Asset Name", "Dell P2422H"}, {"Type", "Monitor"}, {"Tag #", "MN-4010"}, {"Status", "Assigned"} },
            new() { {"Asset Name", "HP EliteDesk 800 G6"}, {"Type", "Desktop"}, {"Tag #", "DT-5001"}, {"Status", "Available"} },
            new() { {"Asset Name", "Dell OptiPlex 7090"}, {"Type", "Desktop"}, {"Tag #", "DT-5002"}, {"Status", "Assigned"} },
            new() { {"Asset Name", "Apple iMac M1 2021"}, {"Type", "Desktop"}, {"Tag #", "DT-5003"}, {"Status", "Surveyed"} },
            new() { {"Asset Name", "Lenovo ThinkCentre M90t"}, {"Type", "Desktop"}, {"Tag #", "DT-5004"}, {"Status", "Available"} },
            new() { {"Asset Name", "Acer Veriton X"}, {"Type", "Desktop"}, {"Tag #", "DT-5005"}, {"Status", "In Repair"} },
            new() { {"Asset Name", "Asus ExpertCenter D7"}, {"Type", "Desktop"}, {"Tag #", "DT-5006"}, {"Status", "Assigned"} },
            new() { {"Asset Name", "MSI PRO DP21"}, {"Type", "Desktop"}, {"Tag #", "DT-5007"}, {"Status", "Available"} },
            new() { {"Asset Name", "Apple Mac Studio"}, {"Type", "Desktop"}, {"Tag #", "DT-5008"}, {"Status", "Surveyed"} },
            new() { {"Asset Name", "HP ProDesk 600"}, {"Type", "Desktop"}, {"Tag #", "DT-5009"}, {"Status", "Assigned"} },
            new() { {"Asset Name", "Intel NUC 11 Pro"}, {"Type", "Desktop"}, {"Tag #", "DT-5010"}, {"Status", "Available"} },
            new() { {"Asset Name", "Anker USB-C to USB-C Cable"}, {"Type", "Charging Cable"}, {"Tag #", "CC-6001"}, {"Status", "Assigned"} },
            new() { {"Asset Name", "Apple Lightning to USB-C"}, {"Type", "Charging Cable"}, {"Tag #", "CC-6002"}, {"Status", "Available"} },
            new() { {"Asset Name", "Amazon Basics Micro USB"}, {"Type", "Charging Cable"}, {"Tag #", "CC-6003"}, {"Status", "In Repair"} },
            new() { {"Asset Name", "Belkin BOOST↑CHARGE USB-C"}, {"Type", "Charging Cable"}, {"Tag #", "CC-6004"}, {"Status", "Surveyed"} },
            new() { {"Asset Name", "Google Pixel USB-C Cable"}, {"Type", "Charging Cable"}, {"Tag #", "CC-6005"}, {"Status", "Available"} },
            new() { {"Asset Name", "Samsung Fast Charge Cable"}, {"Type", "Charging Cable"}, {"Tag #", "CC-6006"}, {"Status", "Assigned"} },
            new() { {"Asset Name", "Anker Powerline III"}, {"Type", "Charging Cable"}, {"Tag #", "CC-6007"}, {"Status", "Available"} },
            new() { {"Asset Name", "Native Union Belt Cable"}, {"Type", "Charging Cable"}, {"Tag #", "CC-6008"}, {"Status", "Surveyed"} },
            new() { {"Asset Name", "Ugreen Braided USB-C"}, {"Type", "Charging Cable"}, {"Tag #", "CC-6009"}, {"Status", "Assigned"} },
            new() { {"Asset Name", "Nomad Kevlar USB-C"}, {"Type", "Charging Cable"}, {"Tag #", "CC-6010"}, {"Status", "In Repair"} },




    };


        var specsData = new Dictionary<string, Dictionary<string, string>>
        {
            { "LT-1035", new Dictionary<string, string> { { "RAM", "16GB" }, { "GPU", "Intel Iris Xe" }, { "Storage", "512GB SSD" } } },
            { "LT-1048", new Dictionary<string, string> { { "RAM", "16GB" }, { "GPU", "Apple M1 Pro" }, { "Storage", "1TB SSD" } } },
            { "LT-1059", new Dictionary<string, string> { { "RAM", "16GB" }, { "GPU", "Intel Iris Xe" }, { "Storage", "512GB SSD" } } },
            { "LT-1104", new Dictionary<string, string> { { "RAM", "32GB" }, { "GPU", "Intel Iris Xe" }, { "Storage", "1TB SSD" } } },
            { "LT-1111", new Dictionary<string, string> { { "RAM", "16GB" }, { "GPU", "Intel Iris Xe" }, { "Storage", "512GB SSD" } } },
            { "HS-0080", new Dictionary<string, string> { { "Battery Life", "20 hours" }, { "Noise Cancellation", "Active" }, { "Connectivity", "USB-A" } } },
            { "CC-0090", new Dictionary<string, string> { { "Length", "1m" }, { "Type", "USB-C to USB-A" }, { "Material", "Nylon Braided" } } },
            { "SW-0100", new Dictionary<string, string> { { "Version", "2023" }, { "License", "Enterprise" }, { "Expiry Date", "2024-12-31" } } },
            { "DT-0011", new Dictionary<string, string> { { "RAM", "32GB" }, { "GPU", "NVIDIA GeForce GTX 1650" }, { "Storage", "1TB SSD" } } },
            { "DT-2020", new Dictionary<string, string> { { "RAM", "16GB" }, { "GPU", "AMD Radeon Pro 5500 XT" }, { "Storage", "1TB SSD" } } },
            { "CC-6001", new Dictionary<string, string> { { "Length", "1.5m" }, { "Type", "USB-C to USB-C" }, { "Material", "Nylon Braided" } } },
            { "SW-3001", new Dictionary<string, string> { { "Version", "2023" }, { "License", "Enterprise" }, { "Expiry Date", "2024-12-31" } } },
            { "MN-0001", new Dictionary<string, string> { { "Resolution", "1920x1080" }, { "Size", "24 inches" }, { "Refresh Rate", "60Hz" } } },
            { "DT-5001", new Dictionary<string, string> { { "RAM", "16GB" }, { "GPU", "Intel UHD Graphics 630" }, { "Storage", "512GB SSD" } } },
            { "DT-5002", new Dictionary<string, string> { { "RAM", "32GB" }, { "GPU", "NVIDIA GeForce GTX 1660" }, { "Storage", "1TB SSD" } } },
            { "DT-5003", new Dictionary<string, string> { { "RAM", "16GB" }, { "GPU", "Apple M1" }, { "Storage", "512GB SSD" } } },
            { "DT-5004", new Dictionary<string, string> { { "RAM", "16GB" }, { "GPU", "Intel UHD Graphics 630" }, { "Storage", "1TB SSD" } } },
            { "DT-5005", new Dictionary<string, string> { { "RAM", "8GB" }, { "GPU", "Intel UHD Graphics 600" }, { "Storage", "256GB SSD" } } },
            { "DT-5006", new Dictionary<string, string> { { "RAM", "16GB" }, { "GPU", "NVIDIA GeForce GTX 1650" }, { "Storage", "512GB SSD" } } },
            { "DT-5007", new Dictionary<string, string> { { "RAM", "8GB" }, { "GPU", "Intel UHD Graphics 600" }, { "Storage", "256GB SSD" } } },
            { "DT-5008", new Dictionary<string, string> { { "RAM", "32GB" }, { "GPU", "Apple M1 Max" }, { "Storage", "2TB SSD" } } },
            { "DT-5009", new Dictionary<string, string> { { "RAM", "16GB" }, { "GPU", "Intel UHD Graphics 630" }, { "Storage", "512GB SSD" } } },
            { "DT-5010", new Dictionary<string, string> { { "RAM", "16GB" }, { "GPU", "Intel Iris Xe Graphics" }, { "Storage", "1TB SSD" } } },
            { "MN-4001", new Dictionary<string, string> { { "Resolution", "3840x2160" }, { "Size", "27 inches" }, { "Refresh Rate", "60Hz" } } },
            { "MN-4002", new Dictionary<string, string> { { "Resolution", "1920x1080" }, { "Size", "24 inches" }, { "Refresh Rate", "75Hz" } } },
            { "MN-4003", new Dictionary<string, string> { { "Resolution", "3840x2160" }, { "Size", "32 inches" }, { "Refresh Rate", "60Hz" } } },
            { "MN-4004", new Dictionary<string, string> { { "Resolution", "2560x1440" }, { "Size", "27 inches" }, { "Refresh Rate", "75Hz" } } },
            { "MN-4005", new Dictionary<string, string> { { "Resolution", "3840x2160" }, { "Size", "27 inches" }, { "Refresh Rate", "60Hz" } } },
            { "MN-4006", new Dictionary<string, string> { { "Resolution", "1920x1080" }, { "Size", "24 inches" }, { "Refresh Rate", "60Hz" } } },
            { "MN-4007", new Dictionary<string, string> { { "Resolution", "2560x1440" }, { "Size", "27 inches" }, { "Refresh Rate", "75Hz" } } },
            { "MN-4008", new Dictionary<string, string> { { "Resolution", "1920x1080" }, { "Size", "23.8 inches" }, { "Refresh Rate", "60Hz" } } },
            { "MN-4009", new Dictionary<string, string> { { "Resolution", "3840x2160" }, { "Size", "28 inches" }, { "Refresh Rate", "60Hz" } } },
            { "MN-4010", new Dictionary<string, string> { { "Resolution", "1920x1080" }, { "Size", "24 inches" }, { "Refresh Rate", "60Hz" } } },
            { "HS-2001", new Dictionary<string, string> { { "Battery Life", "20 hours" }, { "Noise Cancellation", "Active" }, { "Connectivity", "USB-A" } } },
            { "HS-2002", new Dictionary<string, string> { { "Battery Life", "14 hours" }, { "Noise Cancellation", "Passive" }, { "Connectivity", "USB-A" } } },
            { "HS-2003", new Dictionary<string, string> { { "Battery Life", "15 hours" }, { "Noise Cancellation", "Active" }, { "Connectivity", "USB-A" } } },
            { "HS-2004", new Dictionary<string, string> { { "Battery Life", "12 hours" }, { "Noise Cancellation", "Passive" }, { "Connectivity", "USB-A" } } },
            { "HS-2005", new Dictionary<string, string> { { "Battery Life", "20 hours" }, { "Noise Cancellation", "Active" }, { "Connectivity", "USB-C" } } },
            { "HS-2006", new Dictionary<string, string> { { "Battery Life", "15 hours" }, { "Noise Cancellation", "Passive" }, { "Connectivity", "USB-C" } } },
            { "HS-2007", new Dictionary<string, string> { { "Battery Life", "10 hours" }, { "Noise Cancellation", "Active" }, { "Connectivity", "USB-C" } } },
            { "HS-2008", new Dictionary<string, string> { { "Battery Life", "20 hours" }, { "Noise Cancellation", "Passive" }, { "Connectivity", "USB-C" } } },
            { "HS-2009", new Dictionary<string, string> { { "Battery Life", "15 hours" }, { "Noise Cancellation", "Active" }, { "Connectivity", "USB-C" } } },
            { "HS-2010", new Dictionary<string, string> { { "Battery Life", "12 hours" }, { "Noise Cancellation", "Passive" }, { "Connectivity", "USB-C" } } }

            };

        // Handle null or invalid categories gracefully
        if (string.IsNullOrWhiteSpace(category))
        {
            category = "Laptop"; // default category
        }

        var filteredData = tableData
            .Where(row => row["Type"].Equals(category, StringComparison.OrdinalIgnoreCase))
            .ToList();

        this.ViewData["TableHeaders"] = tableHeaders;
        this.ViewData["FilteredData"] = filteredData;
        this.ViewData["SpecsData"] = specsData; //saves the specs data to the view data

        return View();
    }
}

public class HomeIndexViewModel
{
    public List<GetHardwareDto> Hardware { get; set; } = new();
    public List<GetSoftwareDto> Software { get; set; } = new();
}