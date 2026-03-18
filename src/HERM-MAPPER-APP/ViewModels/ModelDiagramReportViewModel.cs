namespace HERMMapperApp.ViewModels;

public sealed class ModelDiagramReportViewModel
{
    public int DomainCount { get; init; }
    public int CapabilityCount { get; init; }
    public int ComponentCount { get; init; }
    public int ProductCount { get; init; }
    public int MappedProductCount { get; init; }
    public int UnmappedProductCount { get; init; }
    public IReadOnlyList<ModelDiagramDomainViewModel> Domains { get; init; } = [];
    public IReadOnlyList<ModelDiagramProductViewModel> UnmappedProducts { get; init; } = [];

    public bool HasAnyContent => Domains.Count > 0 || UnmappedProducts.Count > 0;
}

public sealed class ModelDiagramDomainViewModel
{
    public int DomainId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public IReadOnlyList<ModelDiagramCapabilityViewModel> Capabilities { get; init; } = [];

    public string DisplayLabel => string.IsNullOrWhiteSpace(Code) ? Name : $"{Code} {Name}";
    public int CapabilityCount => Capabilities.Count;
    public int ComponentCount => Capabilities.Sum(x => x.ComponentCount);
    public int ProductCount => Capabilities.SelectMany(x => x.Components).SelectMany(x => x.Products).Select(x => x.ProductId).Distinct().Count();
}

public sealed class ModelDiagramCapabilityViewModel
{
    public int CapabilityId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public IReadOnlyList<ModelDiagramComponentViewModel> Components { get; init; } = [];

    public string DisplayLabel => string.IsNullOrWhiteSpace(Code) ? Name : $"{Code} {Name}";
    public int ComponentCount => Components.Count;
    public int ProductCount => Components.SelectMany(x => x.Products).Select(x => x.ProductId).Distinct().Count();
}

public sealed class ModelDiagramComponentViewModel
{
    public int ComponentId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public IReadOnlyList<ModelDiagramProductViewModel> Products { get; init; } = [];

    public string DisplayLabel => string.IsNullOrWhiteSpace(Code) ? Name : $"{Code} {Name}";
    public int ProductCount => Products.Select(x => x.ProductId).Distinct().Count();
}

public sealed class ModelDiagramProductViewModel
{
    public int ProductId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string StatusLabel { get; init; } = string.Empty;
    public string StatusCssClass { get; init; } = string.Empty;
    public string? Vendor { get; init; }
    public string? Version { get; init; }
    public string? OwnersLabel { get; init; }

    public string VersionLabel => string.IsNullOrWhiteSpace(Vendor) && string.IsNullOrWhiteSpace(Version)
        ? string.Empty
        : $"{Vendor} {Version}".Trim();
}
