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

    public async Task ValidateEditAsync(
         Software software,
        UpdateSoftwareDto dto,
        int id,
        CancellationToken ct)
    {
        // duplicate name
        if (!string.IsNullOrWhiteSpace(dto.SoftwareName))
        {
            bool nameExists = await _db.SoftwareAssets
                .AnyAsync(x => x.SoftwareName == dto.SoftwareName &&
                               x.SoftwareID != id, ct);

            if (nameExists)
                throw new Exception("A software asset with this name already exists.");
        }

        // duplicate license key
        if (!string.IsNullOrWhiteSpace(dto.SoftwareLicenseKey))
        {
            bool keyExists = await _db.SoftwareAssets
                .AnyAsync(x => x.SoftwareLicenseKey == dto.SoftwareLicenseKey &&
                               x.SoftwareID != id, ct);

            if (keyExists)
                throw new Exception("A software asset with this license key already exists.");
        }

        // negative seats
        if (dto.LicenseTotalSeats < 0)
            throw new Exception("Total seats cannot be negative.");

        if (dto.LicenseSeatsUsed < 0)
            throw new Exception("Seats used cannot be negative.");

        // exceeding seats
        var total = dto.LicenseTotalSeats ?? software.LicenseTotalSeats;
        var used = dto.LicenseSeatsUsed ?? software.LicenseSeatsUsed;

        if (used > total)
            throw new Exception("Used seats cannot exceed total seats.");
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
}