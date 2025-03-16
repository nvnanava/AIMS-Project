
using Microsoft.EntityFrameworkCore; //team needs to get entity framework packages
using System;
using System.Collections.Generic;

namespace AIMS.Models;

//Base class for Hardware without functionality. Subclasses underneath to show inheritance example.
//Interacts with the db through AssetContext.cs in the Data folder.

public class Hardware
{
    public int Id { get; set; }
    public int TagNumber { get; set; }
    public string? Status { get; set; }
    public string? Type { get; set; }
    public string? AssignedTo { get; set; }
}

// Inherits from Hardware
public class Laptop : Hardware
{
    public bool HasTouchScreen { get; set; }
}

// Inherits from Hardware
public class Desktop :  Hardware
{
    public int MonitorsAssigned {get; set;}
}
