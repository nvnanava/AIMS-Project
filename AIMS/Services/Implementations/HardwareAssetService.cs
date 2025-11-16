using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using AIMS.Data;
using AIMS.Models;
using AIMS.Dtos.Hardware;
using AIMS.Utilities;

public class HardwareAssetService : IHardwareAssetService
{
    private readonly AimsDbContext _db;

    public HardwareAssetService(AimsDbContext db)
    {
        _db = db;
    }

    public async Task<List<Hardware>> AddHardwareBulkAsync
        (BulkHardwareRequest req, CancellationToken ct = default)
    {
        if (req.Dtos == null || req.Dtos.Count == 0)
            throw new ArgumentException("The hardware list cannot be null or empty.", nameof(req.Dtos));

        var rows = NormalizeDtos(req);

        ValidateRows(rows);

        ValidateInternalDuplicates(rows);

        await ValidateDatabaseDuplicates(rows, ct);

        var entities = rows.Select(MapToEntity).ToList();

        _db.HardwareAssets.AddRange(entities);
        await _db.SaveChangesAsync(ct);

        CacheStamp.BumpAssets();
        return entities;
    }
    private Hardware MapToEntity(CreateHardwareDto d)
    {
        return new Hardware
        {
            AssetTag = d.AssetTag,
            AssetName = string.IsNullOrWhiteSpace(d.AssetName)
                ? $"{d.Manufacturer} {d.Model}".Trim()
                : d.AssetName,
            AssetType = d.AssetType,
            Status = d.Status,
            Manufacturer = d.Manufacturer,
            Model = d.Model,
            SerialNumber = d.SerialNumber,
            WarrantyExpiration = d.WarrantyExpiration,
            PurchaseDate = d.PurchaseDate,
            Comment = d.Comment
        };
    }

    private List<CreateHardwareDto> NormalizeDtos(BulkHardwareRequest req)
    {
        return (req.Dtos ?? new())
            .Select(d => new CreateHardwareDto
            {
                AssetTag = (d.AssetTag ?? "").Trim(),
                AssetName = (d.AssetName ?? "").Trim(),
                AssetType = (d.AssetType ?? "").Trim(),
                Status = (d.Status ?? "").Trim(),
                Manufacturer = (d.Manufacturer ?? "").Trim(),
                Model = (d.Model ?? "").Trim(),
                SerialNumber = (d.SerialNumber ?? "").Trim(),
                WarrantyExpiration = d.WarrantyExpiration,
                PurchaseDate = d.PurchaseDate,
                Comment = (d.Comment ?? "").Trim()
            })
            .Where(d =>
                !(string.IsNullOrWhiteSpace(d.AssetTag) &&
                  string.IsNullOrWhiteSpace(d.Manufacturer) &&
                  string.IsNullOrWhiteSpace(d.Model) &&
                  string.IsNullOrWhiteSpace(d.SerialNumber) &&
                  string.IsNullOrWhiteSpace(d.AssetType) &&
                  string.IsNullOrWhiteSpace(d.Status) &&
                  d.PurchaseDate == default &&
                  d.WarrantyExpiration == default))
            .ToList();
    }
    private void ValidateRows(List<CreateHardwareDto> rows)
    {
        foreach (var r in rows)
        {
            if (string.IsNullOrWhiteSpace(r.AssetTag) ||
                string.IsNullOrWhiteSpace(r.Manufacturer) ||
                string.IsNullOrWhiteSpace(r.Model) ||
                string.IsNullOrWhiteSpace(r.SerialNumber) ||
                string.IsNullOrWhiteSpace(r.AssetType) ||
                string.IsNullOrWhiteSpace(r.Status))
                throw new Exception("All fields required.");

            if (r.AssetTag.Length > 16)
                throw new Exception($"Asset tag too long: {r.AssetTag}");

            if (r.PurchaseDate > DateOnly.FromDateTime(DateTime.UtcNow))
                throw new Exception("Purchase date cannot be in future.");

            if (r.WarrantyExpiration < r.PurchaseDate)
                throw new Exception("Warranty expiration must be after purchase.");
        }
    }

    private void ValidateInternalDuplicates(List<CreateHardwareDto> rows)
    {
        var tags = new HashSet<string>(StringComparer.Ordinal);
        var serials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var r in rows)
        {
            if (!tags.Add(r.AssetTag))
                throw new Exception($"Duplicate asset tag in batch: {r.AssetTag}");

            if (!serials.Add(r.SerialNumber))
                throw new Exception($"Duplicate serial number in batch: {r.SerialNumber}");
        }
    }
    private async Task ValidateDatabaseDuplicates(List<CreateHardwareDto> rows, CancellationToken ct)
    {
        var existingTags = await _db.HardwareAssets
            .Where(h => h.AssetTag != null)
            .Select(h => h.AssetTag!)
            .ToListAsync(ct);

        var existingSerials = await _db.HardwareAssets
            .Where(h => h.SerialNumber != null)
            .Select(h => h.SerialNumber!)
            .ToListAsync(ct);

        var tagSet = new HashSet<string>(existingTags, StringComparer.Ordinal);
        var serialSet = new HashSet<string>(existingSerials, StringComparer.OrdinalIgnoreCase);

        foreach (var r in rows)
        {
            if (tagSet.Contains(r.AssetTag))
                throw new Exception($"Duplicate asset tag: {r.AssetTag}");

            if (serialSet.Contains(r.SerialNumber))
                throw new Exception($"Duplicate serial number: {r.SerialNumber}");
        }
    }



    /*
    //
    //
    //
    //
    */
    public async Task<IActionResult> ValidateEditAsync(
        Hardware hardware,
        UpdateHardwareDto dto,
        int id,
        ModelStateDictionary modelState,
        CancellationToken ct)
    {
        //duplicate asset tag check
        if (dto.AssetTag is not null)
        {
            bool existsTag = await _db.HardwareAssets
                .AnyAsync(h =>
                    h.AssetTag == dto.AssetTag &&
                    h.HardwareID != id, ct);

            if (existsTag)
                modelState.AddModelError(nameof(dto.AssetTag), "A hardware asset with this asset tag already exists.");
        }
        //duplicate serial number check
        if (dto.SerialNumber is not null)
        {
            bool existsSerial = await _db.HardwareAssets
                .AnyAsync(h =>
                    h.SerialNumber == dto.SerialNumber &&
                    h.HardwareID != id, ct);

            if (existsSerial)
                modelState.AddModelError(nameof(dto.SerialNumber), "A hardware asset with this serial number already exists.");
        }

        // validate that new dates are in bounds
        if (dto.PurchaseDate is not null)
        {
            if (dto.PurchaseDate > DateOnly.FromDateTime(DateTime.UtcNow))
                modelState.AddModelError(nameof(dto.PurchaseDate),
                    "Purchase date cannot be in the future.");
        }

        if (dto.WarrantyExpiration is not null)
        {
            var effectivePurchase = dto.PurchaseDate ?? hardware.PurchaseDate;
            if (dto.WarrantyExpiration < effectivePurchase)
                modelState.AddModelError(nameof(dto.WarrantyExpiration),
                    "Warranty expiration cannot be before purchase date.");
        }
        if (!modelState.IsValid)
        {
            return new BadRequestObjectResult(new ValidationProblemDetails(modelState));
        }


        return null;
    }

    public static void ApplyEdit(UpdateHardwareDto dto, Hardware hardware)
    {
        if (dto.AssetTag is not null) hardware.AssetTag = dto.AssetTag;
        if (dto.AssetName is not null) hardware.AssetName = dto.AssetName;
        if (dto.AssetType is not null) hardware.AssetType = dto.AssetType;
        if (dto.Status is not null) hardware.Status = dto.Status;
        if (dto.Manufacturer is not null) hardware.Manufacturer = dto.Manufacturer;
        if (dto.Model is not null) hardware.Model = dto.Model;
        if (dto.Comment is not null) hardware.Comment = dto.Comment;
        if (dto.SerialNumber is not null) hardware.SerialNumber = dto.SerialNumber;
        if (dto.WarrantyExpiration is not null) hardware.WarrantyExpiration = dto.WarrantyExpiration.Value;
        if (dto.PurchaseDate is not null) hardware.PurchaseDate = dto.PurchaseDate.Value;
    }
}