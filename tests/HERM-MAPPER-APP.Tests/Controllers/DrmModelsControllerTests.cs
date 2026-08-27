using HERMMapperApp.Controllers;
using HERMMapperApp.Data;
using HERMMapperApp.Models;
using HERMMapperApp.Services;
using HERMMapperApp.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HERMMapperApp.Tests.Controllers;

public sealed class DrmModelsControllerTests
{
    [Fact]
    public async Task CreateModelAndDataEntityPersistOnlyDrmReferencesAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var seeded = await fixture.SeedDrmCatalogueAsync();

        using var modelsController = fixture.CreateDrmModelsController();
        var createModelResult = await modelsController.CreateAsync(new DrmModelEditViewModel
        {
            Name = "Institution DRM",
            Area = "Student data",
            Status = "Draft",
            Description = "Initial data model"
        });

        var modelRedirect = Assert.IsType<RedirectToActionResult>(createModelResult);
        Assert.Equal("Details", modelRedirect.ActionName);
        var drmModel = await fixture.DbContext.DrmModels.SingleAsync();

        using var dataEntitiesController = fixture.CreateDrmDataEntitiesController();
        var createDataEntityResult = await dataEntitiesController.CreateAsync(drmModel.Id, new DrmDataEntityEditViewModel
        {
            SelectedDrmEntityId = seeded.Entity.Id,
            SelectedDrmCommonSubClassId = seeded.CommonSubClass.Id,
            Description = "Preferred student identifier",
            Notes = "Owned by data governance"
        });

        var dataEntityRedirect = Assert.IsType<RedirectToActionResult>(createDataEntityResult);
        Assert.Equal("Details", dataEntityRedirect.ActionName);
        Assert.Equal("DrmModels", dataEntityRedirect.ControllerName);
        Assert.Equal(drmModel.Id, dataEntityRedirect.RouteValues?["id"]);

        var modelItem = await fixture.DbContext.DrmModelDataEntities.SingleAsync();
        Assert.Equal(drmModel.Id, modelItem.DrmModelId);
        Assert.Equal(seeded.Entity.Id, modelItem.DrmEntityId);
        Assert.Equal(seeded.CommonSubClass.Id, modelItem.DrmCommonSubClassId);
        Assert.Equal("DE101 Legal Name", modelItem.Name);
        Assert.Equal("Preferred student identifier", modelItem.Description);
        Assert.Equal("Owned by data governance", modelItem.Notes);

        Assert.Empty(await fixture.DbContext.ArmDomains.ToListAsync());
        Assert.Empty(await fixture.DbContext.ArmCapabilities.ToListAsync());
        Assert.Empty(await fixture.DbContext.ArmComponents.ToListAsync());
    }

    [Fact]
    public async Task DuplicateDataEntitySelectionReturnsValidationErrorAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var seeded = await fixture.SeedDrmCatalogueAsync();
        var drmModel = new DrmModel
        {
            Name = "Institution DRM",
            Area = "Student data",
            Status = "Draft",
            DataEntities =
            [
                new DrmModelDataEntity
                {
                    DrmEntity = seeded.Entity,
                    DrmCommonSubClass = seeded.CommonSubClass,
                    Name = seeded.CommonSubClass.DisplayLabel
                }
            ]
        };

        await fixture.DbContext.DrmModels.AddAsync(drmModel);
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateDrmDataEntitiesController();
        var result = await controller.CreateAsync(drmModel.Id, new DrmDataEntityEditViewModel
        {
            SelectedDrmEntityId = seeded.Entity.Id,
            SelectedDrmCommonSubClassId = seeded.CommonSubClass.Id
        });

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<DrmDataEntityEditViewModel>(view.Model);
        Assert.Equal(drmModel.Id, model.SelectedDrmModelId);
        Assert.True(controller.ModelState.ContainsKey(nameof(DrmDataEntityEditViewModel.SelectedDrmEntityId)));
        Assert.Contains(
            controller.ModelState[nameof(DrmDataEntityEditViewModel.SelectedDrmEntityId)]!.Errors,
            error => string.Equals("This DRM entity selection already exists in the model.", error.ErrorMessage, StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateDataEntityOptionsDescribeTopicTypeAndTopicRelationshipAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var seeded = await fixture.SeedDrmCatalogueAsync();
        var drmModel = new DrmModel
        {
            Name = "Institution DRM",
            Area = "Student data",
            Status = "Draft"
        };

        await fixture.DbContext.DrmModels.AddAsync(drmModel);
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateDrmDataEntitiesController();
        var result = await controller.CreateAsync(drmModel.Id);

        var model = Assert.IsType<DrmDataEntityEditViewModel>(Assert.IsType<ViewResult>(result).Model);
        var option = Assert.Single(model.EntityOptions);
        Assert.Equal(
            $"{seeded.Entity.Code} {seeded.Entity.Name} (Topic Type = {seeded.TopicType.Name} --> Topic = {seeded.Topic.Code} {seeded.Topic.Name})",
            option.Text);
    }

    [Fact]
    public async Task DetailsBuildsDrmModelHierarchyForSelectedEntitiesAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var seeded = await fixture.SeedDrmCatalogueAsync();
        var drmModel = new DrmModel
        {
            Name = "Institution DRM",
            Area = "Student data",
            Status = "Draft",
            DataEntities =
            [
                new DrmModelDataEntity
                {
                    DrmEntity = seeded.Entity,
                    DrmCommonSubClass = seeded.CommonSubClass,
                    Name = seeded.CommonSubClass.DisplayLabel
                }
            ]
        };

        await fixture.DbContext.DrmModels.AddAsync(drmModel);
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateDrmModelsController();
        var result = await controller.DetailsAsync(drmModel.Id);

        var model = Assert.IsType<DrmModelDetailsViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.True(model.HasHierarchy);
        Assert.Equal(drmModel.Name, model.HierarchyRoot.Label);

        var topicTypeNode = Assert.Single(model.HierarchyRoot.Children);
        Assert.Equal(seeded.TopicType.DisplayLabel, topicTypeNode.Label);
        Assert.Equal("Topic type", topicTypeNode.NodeType);

        var topicNode = Assert.Single(topicTypeNode.Children);
        Assert.Equal(seeded.Topic.DisplayLabel, topicNode.Label);
        Assert.Equal("Topic", topicNode.NodeType);

        var entityNode = Assert.Single(topicNode.Children);
        Assert.Equal(seeded.Entity.DisplayLabel, entityNode.Label);
        Assert.Equal("Data entity", entityNode.NodeType);

        var subClassNode = Assert.Single(entityNode.Children);
        Assert.Equal(seeded.CommonSubClass.DisplayLabel, subClassNode.Label);
        Assert.Equal("Common sub-class", subClassNode.NodeType);
    }

    [Fact]
    public async Task InvalidDataEntitySelectionPostbackFiltersCommonSubClassesToSelectedEntityAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var seeded = await fixture.SeedDrmCatalogueAsync();
        var siblingEntity = new DrmEntity
        {
            Code = "DE002",
            Name = "Account",
            ParentTopic = seeded.Topic,
            ParentTopicCode = seeded.Topic.Code
        };
        var siblingSubClass = new DrmCommonSubClass
        {
            Code = "DE102",
            Name = "Account Number",
            ParentEntity = siblingEntity,
            ParentEntityCode = siblingEntity.Code
        };
        var drmModel = new DrmModel
        {
            Name = "Institution DRM",
            Area = "Student data",
            Status = "Draft"
        };

        await fixture.DbContext.AddRangeAsync(siblingEntity, siblingSubClass, drmModel);
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateDrmDataEntitiesController();
        var result = await controller.CreateAsync(drmModel.Id, new DrmDataEntityEditViewModel
        {
            SelectedDrmEntityId = seeded.Entity.Id,
            SelectedDrmCommonSubClassId = siblingSubClass.Id
        });

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<DrmDataEntityEditViewModel>(view.Model);
        var visibleSubClassIds = model.SelectedEntityCommonSubClassOptions.Select(option => option.Id).ToArray();

        Assert.Equal([seeded.CommonSubClass.Id], visibleSubClassIds);
        Assert.Contains(
            model.CommonSubClassOptions,
            option => option.Id == siblingSubClass.Id && option.ParentEntityId == siblingEntity.Id);
        Assert.True(controller.ModelState.ContainsKey(nameof(DrmDataEntityEditViewModel.SelectedDrmCommonSubClassId)));
        Assert.Contains(
            controller.ModelState[nameof(DrmDataEntityEditViewModel.SelectedDrmCommonSubClassId)]!.Errors,
            error => string.Equals("The selected common sub-class does not belong to the selected entity.", error.ErrorMessage, StringComparison.Ordinal));
    }

    [Fact]
    public async Task StructureShowsOnlyTheEntitiesAndSubClassesAddedToTheModelAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var seeded = await fixture.SeedDrmCatalogueAsync();

        var unusedTopicType = new DrmTopicType
        {
            Code = "DY002",
            Name = "Estate data"
        };
        var unusedTopic = new DrmTopic
        {
            Code = "DT002",
            Name = "Location",
            TopicType = unusedTopicType,
            TopicTypeCode = unusedTopicType.Code
        };
        var unusedEntity = new DrmEntity
        {
            Code = "DE002",
            Name = "Building",
            ParentTopic = unusedTopic,
            ParentTopicCode = unusedTopic.Code
        };
        var unusedSubClass = new DrmCommonSubClass
        {
            Code = "DE102",
            Name = "Lecture Theatre",
            ParentEntity = seeded.Entity,
            ParentEntityCode = seeded.Entity.Code
        };

        await fixture.DbContext.AddRangeAsync(unusedTopicType, unusedTopic, unusedEntity, unusedSubClass);

        var drmModel = new DrmModel
        {
            Name = "Institution DRM",
            Area = "Student data",
            Status = "Draft",
            DataEntities =
            [
                new DrmModelDataEntity
                {
                    DrmEntity = seeded.Entity,
                    DrmCommonSubClass = seeded.CommonSubClass,
                    Name = seeded.CommonSubClass.DisplayLabel
                }
            ]
        };

        await fixture.DbContext.DrmModels.AddAsync(drmModel);
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateDrmModelsController();
        var result = await controller.StructureAsync(drmModel.Id, CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<DrmModelStructureViewModel>(view.Model);

        Assert.Equal(drmModel.Id, model.Id);
        Assert.Equal(1, model.DataEntityCount);
        Assert.Equal(1, model.EntityCount);
        Assert.Equal(1, model.CommonSubClassCount);
        Assert.True(model.Diagram.OnlyShowMappedNodes);
        Assert.True(model.Diagram.HasAnyContent);

        var visibleDomains = model.Diagram.Domains.Where(x => x.ProductCount > 0).ToList();
        Assert.Equal(seeded.TopicType.Code, Assert.Single(visibleDomains).Code);

        var visibleTopics = visibleDomains
            .SelectMany(x => x.Capabilities)
            .Where(x => x.ProductCount > 0)
            .ToList();
        Assert.Equal(seeded.Topic.Code, Assert.Single(visibleTopics).Code);

        var visibleComponents = visibleTopics
            .SelectMany(x => x.Components)
            .Where(x => x.ProductCount > 0)
            .ToList();
        Assert.Equal(seeded.CommonSubClass.Code, Assert.Single(visibleComponents).Code);

        var allComponentCodes = model.Diagram.Domains
            .SelectMany(x => x.Capabilities)
            .SelectMany(x => x.Components)
            .Where(x => x.ProductCount > 0)
            .Select(x => x.Code)
            .ToList();
        Assert.DoesNotContain(unusedEntity.Code, allComponentCodes);
        Assert.DoesNotContain(unusedSubClass.Code, allComponentCodes);
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;

        private TestFixture(SqliteConnection connection, AppDbContext dbContext)
        {
            this.connection = connection;
            DbContext = dbContext;
        }

        public AppDbContext DbContext { get; }

        public static async Task<TestFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;

            var dbContext = new AppDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();

            return new TestFixture(connection, dbContext);
        }

        public DrmModelsController CreateDrmModelsController()
        {
            var controller = new DrmModelsController(
                DbContext,
                new AuditLogService(DbContext),
                new ReferenceModelDiagramService(DbContext));
            controller.TempData = new TempDataDictionary(new DefaultHttpContext(), new TestTempDataProvider());
            return controller;
        }

        public DrmDataEntitiesController CreateDrmDataEntitiesController()
        {
            var controller = new DrmDataEntitiesController(DbContext, new AuditLogService(DbContext));
            controller.TempData = new TempDataDictionary(new DefaultHttpContext(), new TestTempDataProvider());
            return controller;
        }

        public async Task<SeededDrmCatalogue> SeedDrmCatalogueAsync()
        {
            var topicType = new DrmTopicType
            {
                Code = "DY001",
                Name = "Person data"
            };
            var topic = new DrmTopic
            {
                Code = "DT001",
                Name = "Student identity",
                TopicType = topicType,
                TopicTypeCode = topicType.Code
            };
            var entity = new DrmEntity
            {
                Code = "DE001",
                Name = "Student",
                ParentTopic = topic,
                ParentTopicCode = topic.Code
            };
            var commonSubClass = new DrmCommonSubClass
            {
                Code = "DE101",
                Name = "Legal Name",
                ParentEntity = entity,
                ParentEntityCode = entity.Code
            };

            await DbContext.AddRangeAsync(topicType, topic, entity, commonSubClass);
            await DbContext.SaveChangesAsync();

            return new SeededDrmCatalogue(topicType, topic, entity, commonSubClass);
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed record SeededDrmCatalogue(
        DrmTopicType TopicType,
        DrmTopic Topic,
        DrmEntity Entity,
        DrmCommonSubClass CommonSubClass);

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }
}
