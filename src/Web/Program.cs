using Microsoft.EntityFrameworkCore;
using Web.Models;

var builder = WebApplication.CreateBuilder(args);

if (args.Length == 2 && args[0] == "export-sqlite")
{
    await Web.SnapshotExporter.RunAsync(builder.Configuration.GetConnectionString("MmaDb")!, args[1]);
    return;
}

// Locally the site reads the scraper's Postgres database. The deployed demo sets
// ConnectionStrings__MmaSqlite and reads a read-only SQLite snapshot instead.
string? sqliteConnection = builder.Configuration.GetConnectionString("MmaSqlite");

// On Hugging Face Spaces the proxy in front of the app handles HTTPS.
bool behindHttpsProxy = builder.Configuration.GetValue<bool>("BehindHttpsProxy");

builder.Services.AddRazorPages();
builder.Services.AddResponseCompression(options => options.EnableForHttps = true);
builder.Services.AddSingleton<Web.Services.FightPredictor>();
builder.Services.AddDbContext<AppDbContext>(options =>
{
    if (sqliteConnection != null)
        options.UseSqlite(sqliteConnection);
    else
        options.UseNpgsql(builder.Configuration.GetConnectionString("MmaDb"));
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    if (!behindHttpsProxy)
    {
        // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
        app.UseHsts();
    }
}

if (!behindHttpsProxy)
{
    app.UseHttpsRedirection();
}

app.UseResponseCompression();

app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
