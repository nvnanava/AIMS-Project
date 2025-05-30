using Microsoft.EntityFrameworkCore; //team needs to get entity framework packages
using System;
using System.Collections.Generic;

namespace AIMS.Models;

public class Report
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public required string Type { get; set; }

    public string? Description { get; set; }

    public DateTime DateCreated { get; set; }
}