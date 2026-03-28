namespace HERMMapperApp.ViewModels;

public sealed class ModelDiagramReportViewModel
{
    public string ScopeKey { get; init; } = "trm";
    public int? BrmModelId { get; init; }
    public int? ServiceId { get; init; }
    public int? ApplicationId { get; init; }
    public string ReportFragmentId { get; init; } = "report-product-model";
    public string DiagramTitle { get; init; } = "TRM diagram (all objects)";
    public string DiagramDescription { get; init; } = string.Empty;
    public string PosterTitle { get; init; } = "Product model poster";
    public string PosterDescription { get; init; } = string.Empty;
    public string MappedItemLabel { get; init; } = "mapped product(s)";
    public string EmptyStateTitle { get; init; } = "No model content available";
    public string EmptyStateBody { get; init; } = "Import the HERM reference model and product mappings to populate this report.";
    public string BackReportAction { get; init; } = "TrmModelReport";
    public string BackReportLabel { get; init; } = "Back to TRM report";
    public bool ShowUnmappedItems { get; init; } = true;
    public bool OnlyShowMappedNodes { get; init; }
    public bool UseCompactMappedSummary { get; init; }
    public bool ShowComponentMappedSummary { get; init; } = true;
    public bool ShowBranchEmptyStates { get; init; } = true;
    public string UnmappedSectionTitle { get; init; } = "Unmapped Products";
    public string UnmappedSummaryLabel { get; init; } = "item(s) still need a component placement";
    public string? DrawIoDownloadAction { get; init; } = "DownloadDrawIo";
    public string? ArchiDownloadAction { get; init; } = "DownloadArchiXml";
    public string PosterSvgMarkup { get; set; } = string.Empty;
    public int DomainCount { get; init; }
    public int CapabilityCount { get; init; }
    public int ComponentCount { get; init; }
    public int ProductCount { get; init; }
    public int MappedProductCount { get; init; }
    public int UnmappedProductCount { get; init; }
    public int ItemCount { get; init; }
    public int MappedItemCount { get; init; }
    public int UnmappedItemCount { get; init; }
    public IReadOnlyList<ModelDiagramDomainViewModel> Domains { get; init; } = [];
    public IReadOnlyList<ModelDiagramProductViewModel> UnmappedProducts { get; init; } = [];

    public string MappedSummaryLabel => UseCompactMappedSummary ? "mapped" : MappedItemLabel;

    public bool HasAnyContent =>
        Domains.Any(x => !OnlyShowMappedNodes || x.ProductCount > 0) ||
        UnmappedProducts.Count > 0;

    public string FormatMappedSummary(int itemCount) => $"{itemCount} {MappedSummaryLabel}";
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
    public string? LinkController { get; init; }
    public string? LinkAction { get; init; }
    public int? LinkId { get; init; }

    public string VersionLabel => string.IsNullOrWhiteSpace(Vendor) && string.IsNullOrWhiteSpace(Version)
        ? string.Empty
        : $"{Vendor} {Version}".Trim();

    public bool HasLink =>
        !string.IsNullOrWhiteSpace(LinkController) &&
        !string.IsNullOrWhiteSpace(LinkAction) &&
        LinkId.HasValue;
}
