// using AIMS.Data;
// using AIMS.Dtos.Software;
// using System.Threading;
// using System.Threading.Tasks;
// using Microsoft.EntityFrameworkCore;
// using Microsoft.AspNetCore.Mvc.ModelBinding;
// using AIMS.Models;
// using Microsoft.AspNetCore.Mvc;
// using AIMS.Dtos.Hardware;
// using Microsoft.EntityFrameworkCore.Metadata.Internal;


// namespace AIMS.Services;

// public class HardwareUpdateService
// {
//     private readonly AimsDbContext _db;
//     public HardwareUpdateService(AimsDbContext db)
//     {
//         _db = db;
//     }

//     public async Task<IActionResult> ValidateEditAsync(
//         Hardware hardware,
//         UpdateHardwareDto dto,
//         int id,
//         ModelStateDictionary modelState,
//         CancellationToken ct)
//     {
//         //duplicate asset tag check
//         if (dto.AssetTag is not null)
//         {
//             bool existsTag = await _db.HardwareAssets
//                 .AnyAsync(h =>
//                     h.AssetTag == dto.AssetTag &&
//                     h.HardwareID != id, ct);

//             if (existsTag)
//                 modelState.AddModelError(nameof(dto.AssetTag), "A hardware asset with this asset tag already exists.");
//         }
//         //duplicate serial number check
//         if (dto.SerialNumber is not null)
//         {
//             bool existsSerial = await _db.HardwareAssets
//                 .AnyAsync(h =>
//                     h.SerialNumber == dto.SerialNumber &&
//                     h.HardwareID != id, ct);

//             if (existsSerial)
//                 modelState.AddModelError(nameof(dto.SerialNumber), "A hardware asset with this serial number already exists.");
//         }

//         // validate that new dates are in bounds
//         if (dto.PurchaseDate is not null)
//         {
//             if (dto.PurchaseDate > DateOnly.FromDateTime(DateTime.UtcNow))
//                 modelState.AddModelError(nameof(dto.PurchaseDate),
//                     "Purchase date cannot be in the future.");
//         }

//         if (dto.WarrantyExpiration is not null)
//         {
//             var effectivePurchase = dto.PurchaseDate ?? hardware.PurchaseDate;
//             if (dto.WarrantyExpiration < effectivePurchase)
//                 modelState.AddModelError(nameof(dto.WarrantyExpiration),
//                     "Warranty expiration cannot be before purchase date.");
//         }
//         if (!modelState.IsValid)
//         {
//             return new BadRequestObjectResult(new ValidationProblemDetails(modelState));
//         }


//         return null;
//     }

//     public static void ApplyEdit(UpdateHardwareDto dto, Hardware hardware)
//     {
//         if (dto.AssetTag is not null) hardware.AssetTag = dto.AssetTag;
//         if (dto.AssetName is not null) hardware.AssetName = dto.AssetName;
//         if (dto.AssetType is not null) hardware.AssetType = dto.AssetType;
//         if (dto.Status is not null) hardware.Status = dto.Status;
//         if (dto.Manufacturer is not null) hardware.Manufacturer = dto.Manufacturer;
//         if (dto.Model is not null) hardware.Model = dto.Model;
//         if (dto.Comment is not null) hardware.Comment = dto.Comment;
//         if (dto.SerialNumber is not null) hardware.SerialNumber = dto.SerialNumber;
//         if (dto.WarrantyExpiration is not null) hardware.WarrantyExpiration = dto.WarrantyExpiration.Value;
//         if (dto.PurchaseDate is not null) hardware.PurchaseDate = dto.PurchaseDate.Value;
//     }
// }