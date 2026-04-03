using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
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

    private sealed class HermAppFactory : WebApplicationFactory<Program>
    {
        private readonly TemporaryDirectory contentRoot = new();

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
