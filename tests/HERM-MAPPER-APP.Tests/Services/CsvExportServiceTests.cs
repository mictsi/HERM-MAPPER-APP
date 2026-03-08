using HERM_MAPPER_APP.Models;
using HERM_MAPPER_APP.Services;
using Xunit;

namespace HERM_MAPPER_APP.Tests.Services;

public sealed class CsvExportServiceTests
{
    [Fact]
    public void BuildProductMappingExport_UsesFiveColumnHierarchyFormat()
    {
        var service = new CsvExportService();
        var domain = new TrmDomain
        {
            Code = "TD006",
            Name = "Technology Operation"
        };
        var capability = new TrmCapability
        {
            Code = "TP012",
            Name = "Technology Observation",
            ParentDomain = domain
        };
        var component = new TrmComponent
        {
            Code = "TC002",
            Name = "Monitoring & Alerting",
            ParentCapability = capability
        };
        var mapping = new ProductMapping
        {
            ProductCatalogItem = new ProductCatalogItem
            {
                Name = "Graylog"
            },
            TrmComponent = component
        };

        var csv = service.BuildProductMappingExport([mapping]);

        Assert.Equal(
            "MODEL;DOMAIN;CAPABILITY;COMPONENT;PRODUCT" + Environment.NewLine +
            "\"HERM\";\"TD006 Technology Operation\";\"TP012 Technology Observation\";\"TC002 Monitoring & Alerting\";\"Graylog\"" + Environment.NewLine,
            csv);
    }
}
