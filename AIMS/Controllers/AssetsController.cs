using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AIMS.Data;
using AIMS.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIMS.Controllers
{
    public class AssetsController : Controller
    {
        private readonly AimsDbContext _db;

        public AssetsController(AimsDbContext db)
        {
            _db = db;
        }

        // ---------- Page action: renders the table for a given category ----------
        // NOTE: This uses the sample in-memory data you already had.
        public IActionResult AssetDetails(string category)
        {
            ViewData["Category"] = category;

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

            var filteredData = tableData
                .Where(row => row["Type"].Equals(category, StringComparison.OrdinalIgnoreCase))
                .ToList();

            ViewData["TableHeaders"] = tableHeaders;
            ViewData["FilteredData"] = filteredData;

            return View("AssetDetailsComponent");
        }

        // ---------- POST: edit an asset safely (validation + guards) ----------
[ValidateAntiForgeryToken]
[HttpPost]
public async Task<IActionResult> Edit(AIMS.Models.EditAssetViewModel vm)
{
    if (!ModelState.IsValid)
        return BadRequest(new { message = "Validation failed", errors = ModelState });

    // vm.Id holds the ORIGINAL tag (key) as a string. Parse it.
    if (!int.TryParse(vm.Id, out var originalTag))
        return BadRequest(new { message = "Bad Id (expected numeric AssetTag)" });

    // Fetch by primary key (AssetTag)
    var entity = await _db.Hardwares.FirstOrDefaultAsync(h => h.AssetTag == originalTag);
    if (entity == null)
        return NotFound(new { message = "Asset not found" });

    // Map incoming fields to entity names
    entity.AssetName = vm.Name.Trim();
    entity.AssetType = vm.Type.Trim();
    entity.Status    = vm.Status.Trim();

    // If TagNumber is editable: validate and (optionally) update primary key
if (!string.IsNullOrWhiteSpace(vm.TagNumber))
{
    if (!int.TryParse(vm.TagNumber, out var newTag))
        return BadRequest(new { message = "Tag Number must be numeric." });

    if (newTag != originalTag)
    {
        bool tagExists = await _db.Hardwares.AnyAsync(h => h.AssetTag == newTag);
        if (tagExists)
            return BadRequest(new { message = "Tag Number already exists." });

        entity.AssetTag = newTag;
    }
}

    await _db.SaveChangesAsync();
    return Ok(new { message = "Updated", id = entity.AssetTag });

 
}

}
}
