// Force .NET to use IPv4 to prevent DNS resolution errors (SocketException 11004)
using Microsoft.EntityFrameworkCore;
using WisdomVideoApp.Data;
using WisdomVideoApp.Services;

AppContext.SetSwitch("System.Net.DisableIPv6", true);



var builder = WebApplication.CreateBuilder(args);

// ── Database ──────────────────────────────────────────────
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

// ── Application Services ──────────────────────────────────
builder.Services.AddControllersWithViews();
builder.Services.AddHttpClient<VideoGenerationService>();
builder.Services.AddScoped<VideoGenerationService>();

var app = builder.Build();

// ── Middleware Pipeline ────────────────────────────────────
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Video}/{action=Create}/{id?}");

app.Run();