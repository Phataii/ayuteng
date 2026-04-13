using ayuteng.Data;
using ayuteng.Migrations;
using ayuteng.Models;
using ayuteng.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

// -------------------- Database Configuration --------------------
var connectionString = configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// -------------------- Authentication --------------------
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.LogoutPath = "/logout";
        options.ExpireTimeSpan = TimeSpan.FromHours(1);
        options.SlidingExpiration = true;
        options.AccessDeniedPath = "/access-denied";
    });

builder.Services.AddAuthorization();

// -------------------- Application Services --------------------
builder.Services.AddScoped<IBrevoEmailService, BrevoEmailService>();
builder.Services.AddScoped<IEmailVerificationService, EmailVerificationService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<UserHelper>();

// -------------------- Session --------------------
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// -------------------- MVC & HTTP --------------------
builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();
builder.Services.AddHttpClient();

// -------------------- Build Application --------------------
var app = builder.Build();

// -------------------- Middleware Pipeline --------------------
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}


app.UseHttpsRedirection();


app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseSession();

app.Use(async (context, next) =>
{
    const string visitorCookie = "visitor_id";

    // 1️⃣ Get or create visitor id
    if (!context.Request.Cookies.TryGetValue(visitorCookie, out var visitorId))
    {
        visitorId = Guid.NewGuid().ToString();
        context.Response.Cookies.Append(visitorCookie, visitorId, new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddYears(1)
        });
    }

    await next();

    // 2️⃣ Only track landing page
    var path = context.Request.Path.Value?.ToLower();
    if (path != "/") return;

    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    var todayStart = DateTime.UtcNow.Date;
    var tomorrowStart = todayStart.AddDays(1);
    // 3️⃣ Check if visitor already exists today
    var alreadyLoggedToday = await db.SiteVisitors
        .AnyAsync(v => v.VisitorId == visitorId
                    && v.Path == "/"
                        && v.VisitedAt >= todayStart
                    && v.VisitedAt < tomorrowStart);

    if (alreadyLoggedToday) return;

    // 4️⃣ Capture UTM source
    var utmSource = context.Request.Query["utm_source"].ToString();

    // 5️⃣ Save unique landing visit
    var visit = new SiteVisitor
    {
        VisitorId = visitorId,
        Path = "/",
        UtmSource = string.IsNullOrWhiteSpace(utmSource) ? null : utmSource,
        IPAddress = context.Connection.RemoteIpAddress?.ToString(),
        UserAgent = context.Request.Headers["User-Agent"].ToString(),
        VisitedAt = DateTime.UtcNow
    };

    db.SiteVisitors.Add(visit);
    await db.SaveChangesAsync();
});
// app.UseStaticFiles(); // for wwwroot (keep this)

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider("/var/www/uploads"),
    RequestPath = "/uploads"
});
// -------------------- Endpoint Mapping --------------------
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapRazorPages();

// Health check endpoint
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", timestamp = DateTime.UtcNow }));

app.Run();