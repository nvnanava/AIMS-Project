using System;
using System.ComponentModel.DataAnnotations;

namespace AIMS.Models;

public class Agreement
{
    public int AgreementID { get; set; }

    // FK to either Hardware or Software PK (XOR with AssetKind)
    public int? HardwareID { get; set; }
    public Hardware? Hardware { get; set; }

    public int? SoftwareID { get; set; }
    public Software? Software { get; set; }

    public AssetKind AssetKind { get; set; }

    [Required]
    public string FileUri { get; set; } = string.Empty;

    public DateTime DateAdded { get; set; } = DateTime.UtcNow;
}
