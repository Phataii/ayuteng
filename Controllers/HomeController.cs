using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ayuteng.Models;
using ayuteng.Data;

namespace ayuteng.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly ApplicationDbContext _context;
    private readonly UserHelper _userHelper;
    public HomeController(ILogger<HomeController> logger, ApplicationDbContext context, UserHelper userHelper)
    {
        _logger = logger;
        _context = context;
        _userHelper = userHelper;
    }


    public IActionResult Index()
    {
        return View();
    }

    [HttpGet("/verify")]
    public IActionResult Verification(string email)
    {
        if (string.IsNullOrEmpty(email))
        {
            return RedirectToAction("Signup");
        }

        var model = new EmailVerificationSentViewModel
        {
            Email = email,
            SentAt = DateTime.UtcNow,
            ReturnUrl = Url.Action("CompleteSignup", "Account"),
            ResendUrl = Url.Action("ResendVerification", "Account")
        };

        return View(model);
    }

    [HttpGet("/ayute/admin/login")]
    public IActionResult AdminLogin()
    {
        return View("Admin/AdminLogin");
    }

    [HttpGet("/ayute/admin/create")]
    public async Task<IActionResult> Create()
    {
        // Sw86Z6DmomTe
        var loggedInUser = await _userHelper.GetLoggedInAdmin(Request);
        if (loggedInUser == null)
        {
            return Redirect("/ayute/admin/login");
        }
        return View("Admin/create");
    }

    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard()
    {
        var loggedInUser = await _userHelper.GetLoggedInAdmin(Request);
        if (loggedInUser == null)
        {
            return Redirect("/ayute/admin/login");
        }
        var model = new DashboardViewModel
        {
            // Basic counts
            TotalApplications = await _context.Applications.CountAsync(),
            TodayApplications = await _context.Applications
                    .Where(a => a.CreatedAt.Date == DateTime.UtcNow.Date)
                    .CountAsync(),
            ApprovedApplications = await _context.Applications.CountAsync(a => a.Status == "approved"),
            PendingApplications = await _context.Applications.CountAsync(a => a.Status == "submitted"),
            RejectedApplications = await _context.Applications.CountAsync(a => a.Status == "rejected"),

            // Detailed status counts
            DraftApplications = await _context.Applications.CountAsync(a => a.Status == "draft"),
            SubmittedApplications = await _context.Applications.CountAsync(a => a.Status == "submitted"),
            ReviewingApplications = await _context.Applications.CountAsync(a => a.Status == "reviewing"),

            // Gender counts
            MaleApplications = await _context.Applications.CountAsync(a => a.Gender == "male"),
            FemaleApplications = await _context.Applications.CountAsync(a => a.Gender == "female"),
            OtherApplications = await _context.Applications
                    .Where(a => a.Gender == "other" || string.IsNullOrEmpty(a.Gender))
                    .CountAsync(),

            // Calculations
            AwaitingReview = await _context.Applications
                    .Where(a => a.Status == "submitted" || a.Status == "reviewing")
                    .CountAsync()
        };

        // Calculate percentages
        if (model.TotalApplications > 0)
        {
            model.ApprovalRate = Math.Round((model.ApprovedApplications * 100.0) / model.TotalApplications, 1);
            model.RejectionRate = Math.Round((model.RejectedApplications * 100.0) / model.TotalApplications, 1);

            model.MalePercentage = Math.Round((model.MaleApplications * 100.0) / model.TotalApplications, 1);
            model.FemalePercentage = Math.Round((model.FemaleApplications * 100.0) / model.TotalApplications, 1);
            model.OtherPercentage = Math.Round((model.OtherApplications * 100.0) / model.TotalApplications, 1);

            model.DraftPercentage = Math.Round((model.DraftApplications * 100.0) / model.TotalApplications, 1);
            model.SubmittedPercentage = Math.Round((model.SubmittedApplications * 100.0) / model.TotalApplications, 1);
            model.ReviewingPercentage = Math.Round((model.ReviewingApplications * 100.0) / model.TotalApplications, 1);
            model.ApprovedPercentage = Math.Round((model.ApprovedApplications * 100.0) / model.TotalApplications, 1);
            model.RejectedPercentage = Math.Round((model.RejectedApplications * 100.0) / model.TotalApplications, 1);
        }



        // Calculate average processing days (simplified)
        var processedApplications = await _context.Applications
            .Where(a => a.Status == "approved" || a.Status == "rejected")
            .ToListAsync();

        if (processedApplications.Any())
        {
            var totalDays = processedApplications
                .Where(a => a.UpdatedAt > a.CreatedAt)
                .Sum(a => (a.UpdatedAt - a.CreatedAt).TotalDays);

            model.AverageProcessingDays = Math.Round(totalDays / processedApplications.Count, 1);
        }
        else
        {
            model.AverageProcessingDays = 0;
        }

        return View("Admin/Dashboard", model);

    }


    [HttpGet("application/success/{id:guid}")]
    public async Task<IActionResult> Success(Guid id)
    {
        var loggedInUser = await _userHelper.GetLoggedInUser(Request);
        if (loggedInUser == null)
        {
            return Redirect("/login");
        }

        try
        {
            var application = await _context.Applications
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == id);

            if (application == null)
            {
                TempData["Error"] = "Application not found.";
                return RedirectToAction("Login");
            }

            // Generate a reference number if not exists
            ViewBag.ReferenceNumber = application.ReferenceNumber;
            ViewBag.SubmissionDate = DateTime.Now.ToString("MMMM dd, yyyy HH:mm");

            return View(application);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading success page for application {ApplicationId}", id);
            TempData["Error"] = "An error occurred while loading the success page.";
            return Redirect("/login");
        }
    }
    [HttpGet("applications")]
    public async Task<IActionResult> Applications(
               int page = 1,
               string search = "",
               string status = "")
    {
        var loggedInUser = await _userHelper.GetLoggedInAdmin(Request);
        if (loggedInUser == null)
        {
            return Redirect("/ayute/admin/login");
        }

        var pageSize = 10;
        var query = _context.Applications.AsQueryable();

        // Apply status filter
        if (!string.IsNullOrEmpty(status))
        {
            query = query.Where(a => a.Status == status);
        }

        // Apply search filter
        if (!string.IsNullOrEmpty(search))
        {
            query = query.Where(a =>
                a.FirstName.Contains(search) ||
                a.LastName.Contains(search) ||
                a.Email.Contains(search) ||
                a.Phone.Contains(search) ||
                a.StartupName.Contains(search) ||
                a.ReferenceNumber.Contains(search));
        }

        // Get total count
        var totalApplications = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(totalApplications / (double)pageSize);

        // Ensure page is within bounds
        page = Math.Max(1, Math.Min(page, totalPages));
        var skip = (page - 1) * pageSize;

        // Get paginated data
        var applications = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip(skip)
            .Take(pageSize)
            .Select(a => new ApplicationListItemDto
            {
                Id = a.Id,
                ReferenceNumber = a.ReferenceNumber,
                FirstName = a.FirstName,
                LastName = a.LastName,
                Email = a.Email,
                Phone = a.Phone,
                StartupName = a.StartupName,
                Status = a.Status,
                CreatedAt = a.CreatedAt
            })
            .ToListAsync();

        // Create view model
        var viewModel = new ApplicationsPageViewModel
        {
            Applications = applications,
            TotalApplications = totalApplications,
            CurrentPage = page,
            TotalPages = totalPages,
            PageSize = pageSize,
            Search = search,
            StatusFilter = status
        };

        return View("Admin/Applications", viewModel);
    }

    // GET: /Applications/Details/{id}
    [HttpGet("applications/details/{id}")]
    public async Task<IActionResult> Details(Guid id)
    {
        var application = await _context.Applications
            .FirstOrDefaultAsync(a => a.Id == id);

        if (application == null)
        {
            return NotFound();
        }

        return View("Admin/Details", application);
    }
    [Route("login")]
    public IActionResult Login()
    {
        ViewBag.Success = TempData["Success"];
        ViewBag.Error = TempData["Error"];
        return View();
    }

    [HttpPost]
    public IActionResult Logout()
    {
        Response.Cookies.Delete("applicant-jwt");
        return Redirect("/login");
    }

    [Route("application/step-one")]
    public IActionResult Step1()
    {
        return View();
    }

    [HttpGet("application/step-two/{id:guid}")]
    public async Task<IActionResult> Step2(Guid id)
    {
        ViewBag.Next = $"step-three/{id}";
        var loggedInUser = await _userHelper.GetLoggedInUser(Request);
        if (loggedInUser == null)
        {
            return Redirect("/login");
        }
        try
        {
            var application = await _context.Applications
                .Include(a => a.SocialMedia)
                .FirstOrDefaultAsync(a => a.Id == id && a.Status != "Submitted");

            if (application == null)
            {
                TempData["Error"] = "Application may have been completed or does not exist.";
                return Redirect("/login");
            }

            if (TempData["ValidationErrors"] != null)
            {
                ViewBag.ValidationErrors = TempData["ValidationErrors"];
            }

            // ✅ THIS is what returns the page + data
            return View(application);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading step two view");
            TempData["Error"] = "An error occurred while loading your application.";
            return Redirect("/login");
        }
    }



    [Route("application/step-three/{id:guid}")]
    public async Task<IActionResult> Step3(Guid id)
    {
        ViewBag.Previous = $"step-two/{id}";
        ViewBag.Next = $"step-four/{id}";
        var loggedInUser = await _userHelper.GetLoggedInUser(Request);
        if (loggedInUser == null)
        {
            return Redirect("/login");
        }
        try
        {
            var application = await _context.Applications
                 .Include(a => a.SocialMedia)
                 .FirstOrDefaultAsync(a => a.Id == id && a.Status != "Submitted");

            if (application == null)
            {
                TempData["Error"] = "Application may have been completed or does not exist.";
                return Redirect("/login");
            }

            if (TempData["ValidationErrors"] != null)
            {
                ViewBag.ValidationErrors = TempData["ValidationErrors"];
            }

            // ✅ THIS is what returns the page + data
            return View(application);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading step two view");
            TempData["Error"] = "An error occurred while loading your application.";
            return Redirect("/login");
        }
    }

    [Route("application/step-four/{id:guid}")]
    public async Task<IActionResult> Step4(Guid id)
    {
        ViewBag.Previous = $"step-three/{id}";
        ViewBag.Next = $"step-five/{id}";
        var loggedInUser = await _userHelper.GetLoggedInUser(Request);
        if (loggedInUser == null)
        {
            return Redirect("/login");
        }
        try
        {
            var application = await _context.Applications
                .Include(a => a.SocialMedia)
                .FirstOrDefaultAsync(a => a.Id == id && a.Status != "Submitted");

            if (application == null)
            {
                TempData["Error"] = "Application may have been completed or does not exist.";
                return Redirect("/login");
            }

            if (TempData["ValidationErrors"] != null)
            {
                ViewBag.ValidationErrors = TempData["ValidationErrors"];
            }

            // ✅ THIS is what returns the page + data
            return View(application);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading step two view");
            TempData["Error"] = "An error occurred while loading your application.";
            return Redirect("/login");
        }
    }

    [HttpGet("application/step-five/{id:guid}")]
    public async Task<IActionResult> Step5(Guid id)
    {
        ViewBag.Previous = $"step-four/{id}";
        ViewBag.Next = $"step-six/{id}";
        var loggedInUser = await _userHelper.GetLoggedInUser(Request);
        if (loggedInUser == null)
        {
            return Redirect("/login");
        }
        try
        {
            var application = await _context.Applications
                 .Include(a => a.SocialMedia)
                 .FirstOrDefaultAsync(a => a.Id == id && a.Status != "Submitted");

            if (application == null)
            {
                TempData["Error"] = "Application may have been completed or does not exist.";
                return Redirect("/login");
            }

            if (TempData["ValidationErrors"] != null)
            {
                ViewBag.ValidationErrors = TempData["ValidationErrors"];
            }

            // ✅ THIS is what returns the page + data
            return View(application);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading step two view");
            TempData["Error"] = "An error occurred while loading your application.";
            return Redirect("/login");
        }
    }

    [Route("application/step-six/{id:guid}")]
    public async Task<IActionResult> Step6(Guid id)
    {
        ViewBag.Previous = $"step-five/{id}";
        ViewBag.Next = $"step-seven/{id}";
        var loggedInUser = await _userHelper.GetLoggedInUser(Request);
        if (loggedInUser == null)
        {
            return Redirect("/login");
        }
        try
        {
            var application = await _context.Applications
                .Include(a => a.SocialMedia)
                .FirstOrDefaultAsync(a => a.Id == id && a.Status != "Submitted");

            if (application == null)
            {
                TempData["Error"] = "Application may have been completed or does not exist.";
                return Redirect("/login");
            }

            if (TempData["ValidationErrors"] != null)
            {
                ViewBag.ValidationErrors = TempData["ValidationErrors"];
            }

            // ✅ THIS is what returns the page + data
            return View(application);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading step two view");
            TempData["Error"] = "An error occurred while loading your application.";
            return Redirect("/login");
        }
    }

    [Route("application/step-seven/{id:guid}")]
    public async Task<IActionResult> Step7(Guid id)
    {
        ViewBag.Previous = $"step-six/{id}";
        ViewBag.Next = $"step-eight/{id}";
        var loggedInUser = await _userHelper.GetLoggedInUser(Request);
        if (loggedInUser == null)
        {
            return Redirect("/login");
        }
        try
        {
            var application = await _context.Applications
                .Include(a => a.SocialMedia)
                .FirstOrDefaultAsync(a => a.Id == id && a.Status != "Submitted");

            if (application == null)
            {
                TempData["Error"] = "Application may have been completed or does not exist.";
                return Redirect("/login");
            }

            if (TempData["ValidationErrors"] != null)
            {
                ViewBag.ValidationErrors = TempData["ValidationErrors"];
            }

            // ✅ THIS is what returns the page + data
            return View(application);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading step two view");
            TempData["Error"] = "An error occurred while loading your application.";
            return Redirect("/login");
        }
    }

    [Route("application/step-eight/{id:guid}")]
    public async Task<IActionResult> Step8(Guid id)
    {
        ViewBag.Previous = $"step-seven/{id}";
        ViewBag.Next = $"step-nine/{id}";
        var loggedInUser = await _userHelper.GetLoggedInUser(Request);
        if (loggedInUser == null)
        {
            return Redirect("/login");
        }
        try
        {
            var application = await _context.Applications
                .Include(a => a.SocialMedia)
                .FirstOrDefaultAsync(a => a.Id == id && a.Status != "Submitted");

            if (application == null)
            {
                TempData["Error"] = "Application may have been completed or does not exist.";
                return Redirect("/login");
            }

            if (TempData["ValidationErrors"] != null)
            {
                ViewBag.ValidationErrors = TempData["ValidationErrors"];
            }

            // ✅ THIS is what returns the page + data
            return View(application);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading step two view");
            TempData["Error"] = "An error occurred while loading your application.";
            return Redirect("/login");
        }
    }

    [Route("application/step-nine/{id:guid}")]
    public async Task<IActionResult> Step9(Guid id)
    {
        ViewBag.Previous = $"step-eight/{id}";
        var loggedInUser = await _userHelper.GetLoggedInUser(Request);
        if (loggedInUser == null)
        {
            return Redirect("/login");
        }
        try
        {
            var application = await _context.Applications
                .Include(a => a.SocialMedia)
                .FirstOrDefaultAsync(a => a.Id == id && a.Status != "Submitted");

            if (application == null)
            {
                TempData["Error"] = "Application may have been completed or does not exist.";
                return Redirect("/login");
            }

            if (TempData["ValidationErrors"] != null)
            {
                ViewBag.ValidationErrors = TempData["ValidationErrors"];
            }

            // ✅ THIS is what returns the page + data
            return View(application);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading step two view");
            TempData["Error"] = "An error occurred while loading your application.";
            return Redirect("/login");
        }
    }

    [HttpGet]
    [Route("admin/list")]
    public async Task<IActionResult> List()
    {
        try
        {
            var admins = await _context.Admins
                .OrderByDescending(a => a.CreatedAt)
                .ToListAsync();

            return View("admin/list", admins); // This renders Views/Admin/List.cshtml
        }
        catch (Exception ex)
        {
            // Log error
            return StatusCode(500, "Error loading admin list");
        }
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

    public class DashboardViewModel
    {
        // Basic metrics
        public int TotalApplications { get; set; }
        public int TodayApplications { get; set; }
        public int ApprovedApplications { get; set; }
        public int PendingApplications { get; set; }
        public int RejectedApplications { get; set; }
        public int AwaitingReview { get; set; }

        // Detailed status
        public int DraftApplications { get; set; }
        public int SubmittedApplications { get; set; }
        public int ReviewingApplications { get; set; }

        // Gender distribution
        public int MaleApplications { get; set; }
        public int FemaleApplications { get; set; }
        public int OtherApplications { get; set; }

        // Percentages
        public double ApprovalRate { get; set; }
        public double RejectionRate { get; set; }
        public double MalePercentage { get; set; }
        public double FemalePercentage { get; set; }
        public double OtherPercentage { get; set; }
        public double DraftPercentage { get; set; }
        public double SubmittedPercentage { get; set; }
        public double ReviewingPercentage { get; set; }
        public double ApprovedPercentage { get; set; }
        public double RejectedPercentage { get; set; }

        // Growth and performance
        public double WeeklyGrowth { get; set; }
        public double AverageProcessingDays { get; set; }
    }
    // DTOs
    public class ApplicationListItemDto
    {
        public Guid Id { get; set; }
        public string ReferenceNumber { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
        public string StartupName { get; set; }
        public string Status { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class ApplicationsPageViewModel
    {
        public List<ApplicationListItemDto> Applications { get; set; } = new();
        public int TotalApplications { get; set; }
        public int CurrentPage { get; set; } = 1;
        public int TotalPages { get; set; } = 1;
        public int PageSize { get; set; } = 10;
        public string Search { get; set; }
        public string StatusFilter { get; set; }
    }
}
