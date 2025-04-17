using AIMS.Data;
using AIMS.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIMS.Controllers
{
    // localhost:xxxx/api/AimsDb
    [Route("api/[controller]")]
    [ApiController]
    public class AimsDbController : ControllerBase
    {
        private readonly AimsDbContext _context;

        public AimsDbController(AimsDbContext context)
        {

            this._context = context;

        }

        // Gets all assets
        [HttpGet]
        public IActionResult GetAllAssets()
        {
            //AsNoTracking() improves performance when we don't need to track changes to the entities.
            // used for read-only operations
            var hardware = _context.Hardwares.AsNoTracking().ToList();
            var software = _context.Softwares.AsNoTracking().ToList();
            var assets = new
            {
                hardware,
                software
            };
            return Ok(assets);
        }

        // Gets all hardware //
        // localhost:xxxx/api/AimsDb/hardware
        [HttpGet("hardware")]
        public IActionResult GetHardware()
        {
            var hardware = _context.Hardwares.ToList();
            return Ok(hardware);
        }

        // Gets hardware by asset tag //
        // localhost:xxxx/api/AimsDb/hardware/{assetTag}
        [HttpGet("hardware/{assetTag}")]
        public IActionResult GetHardwareById(int assetTag)
        {
            var hardware = _context.Hardwares.Find(assetTag);
            if (hardware == null)
            {
                return NotFound();
            }
            return Ok(hardware);
        }

        // Gets all software //
        // localhost:xxxx/api/AimsDb/software
        [HttpGet("software")]
        public IActionResult GetSoftware()
        {
            var software = _context.Softwares.ToList();
            return Ok(software);
        }

        // Gets software by software id //
        // localhost:xxxx/api/AimsDb/software/{softwareId}

        [HttpGet("software/{softwareId}")]
        public IActionResult GetSoftwareById(int softwareId)
        {

            var software = _context.Softwares.Find(softwareId);
            if (software == null)
            {
                return NotFound();
            }
            return Ok(software);
        }

        // Adds a new hardware entity
        // localhost:xxxx/api/AimsDb/addhardware
        [HttpPost("addhardware")]
        public IActionResult AddHardware([FromBody] AddHardwareDto hardwareDto)
        {
            // using a DTO to validate the input data
            var hardware = new Hardware
            {
                AssetTag = hardwareDto.AssetTag,
                AssetName = hardwareDto.AssetName,
                AssetType = hardwareDto.AssetType,
                Status = hardwareDto.Status,
                Manufacturer = hardwareDto.Manufacturer,
                Model = hardwareDto.Model,
                SerialNumber = hardwareDto.SerialNumber,
                WarrantyExpiration = hardwareDto.WarrantyExpiration,
                PurchaseDate = hardwareDto.PurchaseDate
            };
            _context.Hardwares.Add(hardware);
            _context.SaveChanges(); //changes must be saved to the database
            return StatusCode(StatusCodes.Status201Created);
        }

        // Adds a new software entity
        // localhost:xxxx/api/AimsDb/addsoftware
        [HttpPost("addsoftware")]
        public IActionResult AddSoftware([FromBody] AddSoftwareDto softwareDto)
        {
            // using a DTO to validate the input data
            var software = new Software
            {
                SoftwareId = softwareDto.SoftwareId,
                SoftwareName = softwareDto.SoftwareName,
                SoftwareType = softwareDto.SoftwareType,
                SoftwareVersion = softwareDto.SoftwareVersion,
                SoftwareLicenseKey = softwareDto.SoftwareLicenseKey,
                SoftwareCost = softwareDto.SoftwareCost,
                SoftwareDeploymentLocation = softwareDto.SoftwareDeploymentLocation
            };
            _context.Softwares.Add(software);
            _context.SaveChanges(); //changes must be saved to the database
            return StatusCode(StatusCodes.Status201Created);
        }

        // Updates an existing hardware entity using its asset tag
        // localhost:xxxx/api/AimsDb/updatehardware/{assetTag}
        [HttpPut("updatehardware/{assetTag}")]

        public IActionResult Updatehardware(int assetTag, [FromBody] AddHardwareDto hardwareDto)

        {
            var hardware = _context.Hardwares.Find(assetTag);
            if (hardware == null)
            {
                return NotFound();
            }

            hardware.AssetName = hardwareDto.AssetName;
            hardware.AssetType = hardwareDto.AssetType;
            hardware.Status = hardwareDto.Status;
            hardware.Manufacturer = hardwareDto.Manufacturer;
            hardware.Model = hardwareDto.Model;
            hardware.SerialNumber = hardwareDto.SerialNumber;
            hardware.WarrantyExpiration = hardwareDto.WarrantyExpiration;
            hardware.PurchaseDate = hardwareDto.PurchaseDate;

            _context.SaveChanges();
            return NoContent();
        }

        // Updates an existing software entity
        // localhost:xxxx/api/AimsDb/updatesoftware/{softwareId}
        [HttpPut("updatesoftware/{softwareId}")]
        public IActionResult UpdateSoftware(int softwareId, [FromBody] AddSoftwareDto softwareDto)
        {
            var software = _context.Softwares.Find(softwareId);
            if (software == null)
            {
                return NotFound();
            }
            // using a DTO to validate the input data
            software.SoftwareName = softwareDto.SoftwareName;
            software.SoftwareType = softwareDto.SoftwareType;
            software.SoftwareVersion = softwareDto.SoftwareVersion;
            software.SoftwareLicenseKey = softwareDto.SoftwareLicenseKey;
            software.SoftwareCost = softwareDto.SoftwareCost;
            software.SoftwareDeploymentLocation = softwareDto.SoftwareDeploymentLocation;

            _context.SaveChanges();
            return NoContent();
        }

        //delete hardware by asset tag
        [HttpDelete("deletehardware/{assetTag}")]
        public IActionResult DeleteHardware(int assetTag)
        {
            var hardware = _context.Hardwares.Find(assetTag);
            if (hardware == null)
            {
                return NotFound();
            }
            _context.Hardwares.Remove(hardware);
            _context.SaveChanges();
            return NoContent();
        }

        //delete software by software id
        [HttpDelete("deletesoftware/{softwareId}")]
        public IActionResult DeleteSoftware(int softwareId)
        {
            var software = _context.Softwares.Find(softwareId);
            if (software == null)
            {
                return NotFound();
            }
            _context.Softwares.Remove(software);
            _context.SaveChanges();
            return NoContent();
        }

    }
}
