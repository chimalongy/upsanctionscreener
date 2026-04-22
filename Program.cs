using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Upsanctionscreener.Classess.Utils;
using Upsanctionscreener.Data;
using Upsanctionscreener.Services;
using static Upsanctionscreener.Classess.Search.BKTree;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var builder = WebApplication.CreateBuilder(args);
//----BK Tree ------------------------------------------
builder.Services.AddSingleton(_ => SanctionBKTree.Instance);
builder.Services.AddHostedService<SanctionListRefreshService>();

// ── Database ──────────────────────────────────────────────────────────────────
string? encryptedConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrEmpty(encryptedConnectionString))
    throw new Exception("Connection string 'DefaultConnection' not found.");

string decryptedConnectionString = Cryptor.Decrypt(encryptedConnectionString, true);
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(decryptedConnectionString));

// ── JWT + Cookie Authentication ───────────────────────────────────────────────
var jwtKey = builder.Configuration["Jwt:Key"]!;
var jwtIssuer = builder.Configuration["Jwt:Issuer"]!;
var jwtAud = builder.Configuration["Jwt:Audience"]!;

builder.Services.AddAuthentication(options =>
{
    // Default scheme for MVC views = Cookie
    options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
{
    options.LoginPath = "/Auth/Login";
    options.LogoutPath = "/Auth/Logout";
    options.Cookie.Name = "UP.SanctionScan";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(
        int.Parse(builder.Configuration["Jwt:ExpiryMinutes"] ?? "480"));
    options.SlidingExpiration = true;
})
.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtIssuer,
        ValidAudience = jwtAud,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
    };
});

builder.Services.AddControllersWithViews();
builder.Services.AddScoped<UpSanctionSettingsService>();



var app = builder.Build();


using (var scope = app.Services.CreateScope())
{
    var settingsService = scope.ServiceProvider.GetRequiredService<UpSanctionSettingsService>();

    var result = await settingsService.GetScanSettingsAsync();

    if (result.Success)
    {
        Console.WriteLine($"Scan Settings Retrieved");
        var scanSettings = result.Data;
        double ScanSettingsThreshold = scanSettings.ScanThreshold / 100.0;

        var filePath = Path.Combine(
    GlobalVariables.root_folder,
    "SanctionDatabase", "basesource",
    "UPSanctionDB.xlsx"
);

        if (!File.Exists(filePath))
        {
            Console.WriteLine("Could not Read base UPSanctionDatabase: " + filePath);
            return;
        }
       

        var sanction_entries = await Task.Run(() =>
                  SanctionExcelReader.LoadFromExcel(filePath)
              );
        Console.WriteLine($"Sanction Entries Retrieved");

        SanctionBKTree.Instance.Configure(ScanSettingsThreshold, caseSensitive: false);

        SanctionBKTree.Instance.Load(sanction_entries);

        Console.WriteLine($"Tree loaded with {SanctionBKTree.Instance.NodeCount} nodes.");
       
    }
    else
    {
        Console.WriteLine($"Failed to load ScanSettings: {result.Error}");
    }
}









if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication(); // ← must come before UseAuthorization
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Auth}/{action=Login}/{id?}");

app.Run();