using System.Diagnostics;
using AIMS.Models;
using Microsoft.AspNetCore.Mvc;

namespace AIMS.Controllers;

public class AssetsController : Controller
{
    public IActionResult AssetDetails(string category)
    {
        this.ViewData["Category"] = category;

        // Move this to a service or JSON/database later
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
            new() { {"Asset Name", "Adobe Creative Cloud"}, {"Type", "Software"}, {"Tag #", "SW-3002"}, {"Status", "Available"} },
            new() { {"Asset Name", "AutoCAD 2024"}, {"Type", "Software"}, {"Tag #", "SW-3003"}, {"Status", "In Repair"} },
            new() { {"Asset Name", "Visual Studio 2022 Pro"}, {"Type", "Software"}, {"Tag #", "SW-3004"}, {"Status", "Surveyed"} },
            new() { {"Asset Name", "Slack Enterprise"}, {"Type", "Software"}, {"Tag #", "SW-3005"}, {"Status", "Available"} },
            new() { {"Asset Name", "Zoom Pro"}, {"Type", "Software"}, {"Tag #", "SW-3006"}, {"Status", "Assigned"} },
            new() { {"Asset Name", "Cisco WebEx"}, {"Type", "Software"}, {"Tag #", "SW-3007"}, {"Status", "Surveyed"} },
            new() { {"Asset Name", "Notion Team Plan"}, {"Type", "Software"}, {"Tag #", "SW-3008"}, {"Status", "Available"} },
            new() { {"Asset Name", "IntelliJ IDEA Ultimate"}, {"Type", "Software"}, {"Tag #", "SW-3009"}, {"Status", "Assigned"} },
            new() { {"Asset Name", "Jira Software Cloud"}, {"Type", "Software"}, {"Tag #", "SW-3010"}, {"Status", "In Repair"} },



        };

        // Filter by category
        var filteredData = tableData
            .Where(row => row["Type"].Equals(category, StringComparison.OrdinalIgnoreCase))
            .ToList();

        this.ViewData["TableHeaders"] = tableHeaders;
        this.ViewData["FilteredData"] = filteredData;

        return View("AssetDetailsComponent");
    }
}
