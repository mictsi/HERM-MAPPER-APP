using HERM_MAPPER_APP.Data;
using HERM_MAPPER_APP.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var dataDirectory = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
Directory.CreateDirectory(dataDirectory);

var configuredConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
var connectionString = string.IsNullOrWhiteSpace(configuredConnectionString)
    ? $"Data Source={Path.Combine(dataDirectory, "herm-mapper.db")}"
    : configuredConnectionString.Replace("|DataDirectory|", dataDirectory.Replace("\\", "/"), StringComparison.OrdinalIgnoreCase);

// Add services to the container.
builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));
builder.Services.AddScoped<TrmWorkbookImportService>();
builder.Services.AddScoped<SampleRelationshipImportService>();
builder.Services.AddScoped<DatabaseInitializer>();
builder.Services.AddScoped<CsvExportService>();
builder.Services.AddScoped<AuditLogService>();
builder.Services.AddScoped<ComponentVersioningService>();
builder.Services.AddControllersWithViews();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
    await initializer.InitializeAsync();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
