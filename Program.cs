using DinkToPdf;
using DinkToPdf.Contracts;
using DocumentFormat.OpenXml.Office2010.CustomUI;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Quartz;
using System.Text;
using Upsanctionscreener.Classess;
using Upsanctionscreener.Classess.Search;
using Upsanctionscreener.Classess.Utils;
using Upsanctionscreener.Data;
using Upsanctionscreener.Jobs;
using Upsanctionscreener.Services;
using Upsanctionscreener.Services;

Console.WriteLine("Starting Application");

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
Console.WriteLine("EnsuringBrowserAsync");
await SingleScreenReportGenerator.EnsureBrowserAsync();

Console.WriteLine("3");



//List<Merchant> merchants = MerchantGenerator.GenerateMerchants(300000);
//await MerchantGenerator.InsertMerchantsAsync(
//DatabaseType.Oracle,
// "p05CfML0aukekqdCkyROx7kbY/tWqLIWPVDsIzA+HZeS0gmupdOkOx8ekyCLskrR/1T924G8OsaZ/cN/LDv13042hiX2vn9u",
//merchants);
//string connstr = "jsyuRZJQPpHLg9EcVnk13vQHH8LKxs5AkyGngaqx7XoENtCcv9bRHq4w3uJBB28DCvsyU0p0+xEekqdCkyROx642+j+m8p2cdD68iD44R7H5XTt9D8V+Vg==";
//string decryptedConnStr = Cryptor.Decrypt(connstr, true);

//string newconstring = "User Id=upsanctions;Password=1;Data Source=localhost:1521/XEPDB1;";
//string newencryped = Cryptor.Encrypt(newconstring, true);


var builder = WebApplication.CreateBuilder(args);
AppConfigFetcher.Initialize(builder.Configuration);

// ── Quartz ────────────────────────────────────────────────────────────────────
builder.Services.AddQuartz(q =>
{
    q.UseMicrosoftDependencyInjectionJobFactory();

    // In-memory store — jobs survive restarts via SchedulerStartupService
    q.UseInMemoryStore();

    // Up to 10 target scans can run at the same time
    q.UseDefaultThreadPool(tp =>
    {
        tp.MaxConcurrency = 10;
    });
});

builder.Services.AddQuartzHostedService(opt =>
{
    // Wait for any running jobs to finish before the app shuts down
    opt.WaitForJobsToComplete = true;
});

// ── Scheduler services ────────────────────────────────────────────────────────
builder.Services.AddScoped<TargetSchedulerService>();
builder.Services.AddHostedService<SchedulerStartupService>();


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

builder.Services.AddHttpClient<SanctionDownloader>(client =>
{
    client.Timeout = TimeSpan.FromMinutes(5);
});


builder.Services.AddControllersWithViews();
builder.Services.AddScoped<UpSanctionSettingsService>();

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(3000, listenOptions =>
    {
        listenOptions.UseHttps(GlobalVariables.certificate_path, "1");
        //listenOptions.UseHttps();
    });
});

var app = builder.Build();




if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.UseMiddleware<Upsanctionscreener.Middleware.ApiKeyAuthMiddleware>(); 


app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Auth}/{action=Login}/{id?}");

app.Run();