//using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;

namespace AIMS.Models;

public class Software
{
    public int Id {get; set;}
    public int LicenseKey { get; set; }
    public int SoftwareName { get; set; }
    public string? Status { get; set; }
    public string? Version { get; set; }
    public DateTime? PurchaseDate { get; set; }
    public DateTime? ExpireDate { get; set; }
}

