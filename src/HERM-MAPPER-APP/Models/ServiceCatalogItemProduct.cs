namespace HERM_MAPPER_APP.Models;

public sealed class ServiceCatalogItemProduct
{
    public int Id { get; set; }
    public int ServiceCatalogItemId { get; set; }
    public int ProductCatalogItemId { get; set; }
    public int SortOrder { get; set; }

    public ServiceCatalogItem ServiceCatalogItem { get; set; } = null!;
    public ProductCatalogItem ProductCatalogItem { get; set; } = null!;
}
