using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using AssetTrackingSystem.Data;
using AssetTrackingSystem.Models;

public class RegisterNewAssetModel : PageModel
{
    private readonly AimsDbContext _db;
    [BindProperty] public Asset Input { get; set; }

    public RegisterNewAssetModel(AimsDbContext db) => _db = db;

    public IActionResult OnPost()
    {
        if (!ModelState.IsValid) return Page();
        _db.Assets.Add(Input);
        _db.SaveChanges();
        return RedirectToPage("/AssetList");
    }
}

