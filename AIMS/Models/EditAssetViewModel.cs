using System.ComponentModel.DataAnnotations;

namespace AIMS.Models
{
    public class EditAssetViewModel
    {
        [Required] public string Id        { get; set; } = string.Empty; // original AssetTag (PK)
        [Required] public string Name      { get; set; } = string.Empty;
        [Required] public string Type      { get; set; } = string.Empty;
        [Required, Display(Name = "Tag Number")]
        public string TagNumber            { get; set; } = string.Empty;
        [Required] public string Status    { get; set; } = string.Empty;
    }
}
