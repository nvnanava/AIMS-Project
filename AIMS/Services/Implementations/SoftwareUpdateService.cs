using AIMS.Data;
using AIMS.Dtos.Software;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using AIMS.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore.Metadata.Internal;


namespace AIMS.Services;

public class SoftwareUpdateService
{
    private readonly AimsDbContext _db;
    public SoftwareUpdateService(AimsDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> ValidateEditAsync(
        Software software,
        UpdateSoftwareDto dto,
        int id,
        ModelStateDictionary modelState,
        CancellationToken ct)
    {
        if (dto.SoftwareLicenseKey is not null)
        {
            var (isValidKey, error) = await ValidateLicenseKeyAsync(dto, id, ct);
            if (!isValidKey)
                modelState.AddModelError(nameof(dto.SoftwareLicenseKey), error!);
        }

        ValidateNonNegatives(dto, modelState);

        //cross-field validation for seat counts
        var effectiveTotal = dto.LicenseTotalSeats ?? software.LicenseTotalSeats;
        var effectiveUsed = dto.LicenseSeatsUsed ?? software.LicenseSeatsUsed;
        if (effectiveUsed > effectiveTotal)
        {
            modelState.AddModelError(nameof(dto.LicenseSeatsUsed), "Used seats cannot exceed total seats.");
        }

        if (!modelState.IsValid)
        {
            return new BadRequestObjectResult(new ValidationProblemDetails(modelState));
        }
        return null;
    }

    private static void ValidateNonNegatives(UpdateSoftwareDto dto, ModelStateDictionary modelState)
    {
        if (dto.LicenseTotalSeats.HasValue && dto.LicenseTotalSeats < 0)
            modelState.AddModelError(nameof(dto.LicenseTotalSeats), "Total seats cannot be negative.");
        if (dto.LicenseSeatsUsed.HasValue && dto.LicenseSeatsUsed < 0)
            modelState.AddModelError(nameof(dto.LicenseSeatsUsed), "Used seats cannot be negative.");
    }

    public static void ApplyEdit(UpdateSoftwareDto dto, Software software)
    {
        if (dto.SoftwareName is not null) software.SoftwareName = dto.SoftwareName;
        if (dto.SoftwareType is not null) software.SoftwareType = dto.SoftwareType;
        if (dto.SoftwareVersion is not null) software.SoftwareVersion = dto.SoftwareVersion;
        if (dto.SoftwareLicenseExpiration is not null) software.SoftwareLicenseExpiration = dto.SoftwareLicenseExpiration;
        if (dto.SoftwareUsageData is not null) software.SoftwareUsageData = dto.SoftwareUsageData.Value;
        if (dto.SoftwareCost is not null) software.SoftwareCost = dto.SoftwareCost.Value;
        if (dto.LicenseTotalSeats is not null) software.LicenseTotalSeats = dto.LicenseTotalSeats.Value;
        if (dto.LicenseSeatsUsed is not null) software.LicenseSeatsUsed = dto.LicenseSeatsUsed.Value;
        if (dto.Comment is not null) software.Comment = dto.Comment;
    }

    private async Task<(bool IsValid, string? Error)> ValidateLicenseKeyAsync(UpdateSoftwareDto dto, int id, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.SoftwareLicenseKey))
            return (true, null); // nothing to validate

        var keyExists = await _db.SoftwareAssets
            .AsNoTracking()
            .AnyAsync(s =>
                s.SoftwareLicenseKey == dto.SoftwareLicenseKey &&
                s.SoftwareID != id, ct);

        if (keyExists)
            return (false, "This license key is already in use by another software record.");

        return (true, null);
    }
}