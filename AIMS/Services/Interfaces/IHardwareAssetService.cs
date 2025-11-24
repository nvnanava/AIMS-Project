using AIMS.Dtos.Hardware;
using AIMS.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
public interface IHardwareAssetService
{
    Task<List<Hardware>> AddHardwareBulkAsync(BulkHardwareRequest req, CancellationToken ct = default);
    Task<List<string>> ValidateEditAsync(Hardware hardware, UpdateHardwareDto dto, int id, CancellationToken ct);
    Task<Hardware> UpdateHardwareAsync(int id, UpdateHardwareDto dto, CancellationToken ct);

}
