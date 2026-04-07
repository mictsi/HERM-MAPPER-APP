using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using HERMMapperApp.Data;
using HERMMapperApp.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HERMMapperApp.Tests;

public sealed class ProgramHostIntegrationTests
{
    [Fact]
    public async Task ApplicationStartupRedirectsAnonymousRootRequestToLoginAsync()
    {
        using var factory = new HermAppFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.StartsWith("http://localhost/Account/Login", response.Headers.Location!.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplicationStartupServesLoginPageAsync()
    {
        using var factory = new HermAppFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using var response = await client.GetAsync("/Account/Login");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<form", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApplicationStartupServesHealthEndpointWithoutAuthenticationAsync()
    {
        using var factory = new HermAppFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using var response = await client.GetAsync("/health");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("healthy", body);
    }

    [Fact]
    public async Task ApplicationStartupCachesHealthEndpointForSixtySecondsAsync()
    {
        using var factory = new HermAppFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Cache-Control", out var cacheControlValues));
        Assert.Contains(cacheControlValues, value => string.Equals(value.Replace(" ", string.Empty), "public,max-age=60", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProductsBulkEditRendersCheckboxTogglesForEditableSectionsAsync()
    {
        using var factory = new HermAppFactory();
        var productIds = await factory.SeedProductsAsync("Bulk Edit Alpha", "Bulk Edit Beta");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        await AuthenticateLocalAdminAsync(client);

        using var response = await client.GetAsync($"/Products/BulkEdit?selectedIds={productIds[0]}&selectedIds={productIds[1]}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<input type=\"hidden\" name=\"ApplyVendor\" value=\"false\" />", body, StringComparison.Ordinal);
        Assert.Contains("type=\"checkbox\"", body, StringComparison.Ordinal);
        Assert.Contains("id=\"ApplyVendor\"", body, StringComparison.Ordinal);
        Assert.Contains("id=\"ApplyOwners\"", body, StringComparison.Ordinal);
        Assert.Contains("id=\"ApplyLifecycleStatus\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"ApplyVendor\" name=\"ApplyVendor\" value=\"\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"ApplyOwners\" name=\"ApplyOwners\" value=\"\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"ApplyLifecycleStatus\" name=\"ApplyLifecycleStatus\" value=\"\"", body, StringComparison.Ordinal);
    }

    private static async Task AuthenticateLocalAdminAsync(HttpClient client)
    {
        using var loginPage = await client.GetAsync("/Account/Login");
        var loginBody = await loginPage.Content.ReadAsStringAsync();
        var tokenMatch = Regex.Match(
            loginBody,
            "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"",
            RegexOptions.CultureInvariant);

        Assert.True(tokenMatch.Success);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/Account/Login")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["UserName"] = "admin",
                ["Password"] = "ChangeMeNow!123",
                ["__RequestVerificationToken"] = tokenMatch.Groups[1].Value
            })
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
    }

    private sealed class HermAppFactory : WebApplicationFactory<Program>
    {
        private readonly TemporaryDirectory contentRoot = new();

        public async Task<int[]> SeedProductsAsync(params string[] names)
        {
            using var scope = Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var products = names.Select(name => new ProductCatalogItem
            {
                Name = name,
                UpdatedUtc = DateTime.UtcNow,
                CreatedUtc = DateTime.UtcNow
            }).ToArray();

            await dbContext.ProductCatalogItems.AddRangeAsync(products);
            await dbContext.SaveChangesAsync();

            return products.Select(product => product.Id).ToArray();
        }

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseContentRoot(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "HERM-MAPPER-APP")));
            builder.ConfigureAppConfiguration((_, configurationBuilder) =>
            {
                configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:Provider"] = "Sqlite",
                    ["Database:ConnectionString"] = $"Data Source={Path.Combine(contentRoot.Path, "herm-integration.db")}",
                    ["Security:Authentication:Local:Enabled"] = "true",
                    ["Security:Authentication:OpenIdConnect:Enabled"] = "false",
                    ["HermWorkbook:AutoImportOnFirstRun"] = "false",
                    ["SampleRelationships:AutoImportOnFirstRun"] = "false"
                });
            });
            builder.ConfigureServices(services =>
            {
                services.AddDataProtection()
                    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(contentRoot.Path, "data-protection-keys")));
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                contentRoot.Dispose();
            }
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"herm-mapper-host-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
