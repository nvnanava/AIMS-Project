using System;
using System.ComponentModel.DataAnnotations;

namespace AIMS.Models;


/// Data Transfer Object (DTO) for adding hardware information. 
/// DTOs are used for input validation for our model classes.
//  Can help in reducing the amount of data sent over the network.
public class AddHardwareDto
{
    [Required] 
    public int AssetTag { get; set; } = 0;

    [MaxLength(255)]
    public string AssetName { get; set; } = null!;
    public string AssetType { get; set; } = null!;
    public string Status { get; set; } = null!;
    public string Manufacturer { get; set; } = null!;
    public string Model { get; set; } = null!;
    public string SerialNumber { get; set; } = null!;
    
    [DataType(DataType.Date)]
    public DateOnly WarrantyExpiration { get; set; }

    [DataType(DataType.Date)]
    public DateOnly PurchaseDate { get; set; }
}
