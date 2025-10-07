using System.Collections.Generic;

namespace AIMS.ViewModels;

public class HomeIndexViewModel
{
    public List<GetHardwareDto> Hardware { get; set; } = new();
    public List<GetSoftwareDto> Software { get; set; } = new();
}
