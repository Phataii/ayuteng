using System.ComponentModel.DataAnnotations;
using ayuteng.Data;
using ayuteng.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ayuteng.Controllers
{

    [ApiController]
    [Route("api/[controller]")]
    public class AdminController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<ApplicationController> _logger;
        public AdminController(ILogger<ApplicationController> logger, ApplicationDbContext context, IConfiguration configuration)
        {
            _context = context;
            _logger = logger;
        }

        private IActionResult LoginFailed(string message)
        {
            TempData["Error"] = message;
            return Redirect("/ayute/admin/login");
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromForm] string email, [FromForm] string password)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
                return LoginFailed("Email and password are required");

            var user = await _context.Admins
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Email == email);

            if (user == null || !BCrypt.Net.BCrypt.Verify(password, user.Password))
                return LoginFailed("Invalid email or password");

            // Generate JWT
            var token = new JwtHelper().GenerateJwtToken_Admin(user);
            Response.Cookies.Append("admin-jwt", token, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                Expires = DateTime.UtcNow.AddHours(1)
            });
            // if (user.ApplicationStep == 10 && user.Status == "completed")
            // {
            //     TempData["Error"] = "Application may have been completed or does not exist.";
            //     return Redirect("/login");
            // }

            return Redirect($"/dashboard");
        }
        [HttpGet("logout")]
        public IActionResult Logout()
        {
            Response.Cookies.Delete("admin-jwt");
            return Redirect("/ayute/admin/login");
        }
        [HttpPost("create")]
        public async Task<IActionResult> CreateAdmin([FromForm] CreateAdminRequest request)
        {
            try
            {
                // Validate request
                if (!ModelState.IsValid)
                {
                    var errors = ModelState.ToDictionary(
                        kvp => kvp.Key,
                        kvp => kvp.Value.Errors.Select(e => e.ErrorMessage).ToArray()
                    );

                    return BadRequest(new
                    {
                        success = false,
                        message = "Validation failed",
                        errors = errors
                    });
                }

                // Check if email already exists
                var existingAdmin = await _context.Admins
                    .FirstOrDefaultAsync(a => a.Email == request.Email);

                if (existingAdmin != null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Email already exists"
                    });
                }

                // Hash password (you should use a proper password hashing library)
                var HashPassword = BCrypt.Net.BCrypt.HashPassword(request.Password);
                // Create new admin
                var admin = new Admin
                {
                    Id = Guid.NewGuid(),
                    Email = request.Email,
                    Password = HashPassword,
                    Role = request.Role,
                    IsActive = request.IsActive,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                // Save to database
                _context.Admins.Add(admin);
                await _context.SaveChangesAsync();

                // Log the creation
                _logger.LogInformation("Admin created: {Email}", admin.Email);

                // Send email notification (implement this based on your email service)
                // await SendAdminCreatedEmail(admin.Email, request.Password);

                return Ok(new
                {
                    success = true,
                    message = "Admin created successfully",
                    adminId = admin.Id,
                    redirectUrl = "/dashboard"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating admin");
                return StatusCode(500, new
                {
                    success = false,
                    message = "An error occurred while creating admin"
                });
            }
        }

        private async Task<ApplicationsStats> GetApplicationsStats(IQueryable<Application> query)
        {
            var total = await query.CountAsync();
            var draft = await query.CountAsync(a => a.Status == "draft");
            var submitted = await query.CountAsync(a => a.Status == "submitted");
            var reviewing = await query.CountAsync(a => a.Status == "reviewing");
            var approved = await query.CountAsync(a => a.Status == "approved");
            var rejected = await query.CountAsync(a => a.Status == "rejected");
            var male = await query.CountAsync(a => a.Gender == "male");
            var female = await query.CountAsync(a => a.Gender == "female");
            var other = await query.CountAsync(a => a.Gender == "other" || string.IsNullOrEmpty(a.Gender));

            return new ApplicationsStats
            {
                Total = total,
                Draft = draft,
                Submitted = submitted,
                Reviewing = reviewing,
                Approved = approved,
                Rejected = rejected,
                Male = male,
                Female = female,
                Other = other
            };
        }

    }
}

public class CreateAdminRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; }

    [Required]
    [MinLength(8)]
    [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).+$",
        ErrorMessage = "Password must contain at least one uppercase letter, one lowercase letter, and one number")]
    public string Password { get; set; }

    [Required]
    [Range(1, 2, ErrorMessage = "Invalid role selected")]
    public int Role { get; set; }

    public bool IsActive { get; set; } = true;
}

public class ApplicationsStats
{
    public int Total { get; set; }
    public int Draft { get; set; }
    public int Submitted { get; set; }
    public int Reviewing { get; set; }
    public int Approved { get; set; }
    public int Rejected { get; set; }
    public int Male { get; set; }
    public int Female { get; set; }
    public int Other { get; set; }
}

public class ApplicationListItemDto
{
    public Guid Id { get; set; }
    public string ReferenceNumber { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string Email { get; set; }
    public string Phone { get; set; }
    public string Gender { get; set; }
    public string StartupName { get; set; }
    public string Status { get; set; }
    public int ApplicationStep { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ApplicationsResponse
{
    public List<ApplicationListItemDto> Applications { get; set; }
    public ApplicationsStats Stats { get; set; }
    public int TotalPages { get; set; }
    public int CurrentPage { get; set; }
}
