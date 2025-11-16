using AIMS.Dtos.Hardware;
using AIMS.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
public interface IHardwareAssetService
{
    Task<List<Hardware>> AddHardwareBulkAsync(BulkHardwareRequest req, CancellationToken ct = default);
    Task<IActionResult> ValidateEditAsync(
        Hardware hardware,
        UpdateHardwareDto dto,
        int id,
        ModelStateDictionary modelState,
        CancellationToken ct);


}
