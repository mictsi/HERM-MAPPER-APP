namespace HERMMapperApp.Models;

public sealed class ServiceCatalogItemConnection
{
    public int Id { get; set; }
    public int ServiceCatalogItemId { get; set; }
    public int FromProductCatalogItemId { get; set; }
    public int ToProductCatalogItemId { get; set; }
    public int SortOrder { get; set; }

    public ServiceCatalogItem ServiceCatalogItem { get; set; } = null!;
    public ProductCatalogItem FromProductCatalogItem { get; set; } = null!;
    public ProductCatalogItem ToProductCatalogItem { get; set; } = null!;
}
