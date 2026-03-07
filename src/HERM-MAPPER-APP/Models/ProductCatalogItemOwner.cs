using System.ComponentModel.DataAnnotations;

namespace HERM_MAPPER_APP.Models;

public sealed class ProductCatalogItemOwner
{
    public int Id { get; set; }

    public int ProductCatalogItemId { get; set; }
    public ProductCatalogItem? ProductCatalogItem { get; set; }

    [Required, StringLength(120)]
    public string OwnerValue { get; set; } = string.Empty;
}
