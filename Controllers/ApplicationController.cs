using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Session;
using Microsoft.Extensions.Caching.Memory;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

using ayuteng.Models;
using ayuteng.Data;
using ayuteng.Services;

namespace ayuteng.Controllers
{

    [ApiController]
    [Route("api/[controller]")]
    public class ApplicationController : Controller
    {
        private readonly ILogger<ApplicationController> _logger;
        private readonly ApplicationDbContext _context;
        private readonly IBrevoEmailService _emailService;
        private readonly IWebHostEnvironment _hostingEnvironment;
        private readonly IEmailVerificationService _verificationService;
        private readonly IUserService _userService;
        public ApplicationController(IUserService userService, IEmailVerificationService verificationService, IWebHostEnvironment hostingEnvironment, ILogger<ApplicationController> logger, ApplicationDbContext context, IConfiguration configuration, IBrevoEmailService emailService)
        {
            _context = context;
            _logger = logger;
            _emailService = emailService;
            _verificationService = verificationService;
            _hostingEnvironment = hostingEnvironment;
            _userService = userService;

        }
        private IActionResult LoginFailed(string message)
        {
            TempData["Error"] = message;
            return Redirect("/login");
        }


        [HttpPost("login")]
        public async Task<IActionResult> Login([FromForm] string email, [FromForm] string password)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
                return LoginFailed("Email and password are required");

            var user = await _context.Applications
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Email == email);

            if (user == null || !BCrypt.Net.BCrypt.Verify(password, user.Password))
                return LoginFailed("Invalid email or password");

            if (user.IsVerified != true)
            {
                return LoginFailed("Email not verified. Check your email or contact support");
            }
            // Generate JWT
            var token = new JwtHelper().GenerateJwtToken(user);

            Response.Cookies.Append("applicant-jwt", token, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                Expires = DateTime.UtcNow.AddHours(1)
            });
            if (user.ApplicationStep == 10 && user.Status == "completed")
            {
                TempData["Error"] = "Application may have been completed or does not exist.";
                return Redirect("/login");
            }
            // Resolve next step
            var step = StepRoutes.GetValueOrDefault(user.ApplicationStep, "step-two");

            return Redirect($"/application/{step}/{user.Id}");
        }

        [HttpPost("step-one")]
        public async Task<IActionResult> StepOne([FromBody] StepOneRequest request)
        {
            try
            {
                // Validate the request
                if (!ModelState.IsValid)
                {
                    return BadRequest(new
                    {
                        success = false,
                        errors = ModelState.ToDictionary(
                            kvp => kvp.Key,
                            kvp => kvp.Value.Errors.Select(e => e.ErrorMessage).ToArray()
                        )
                    });
                }

                // Validate age requirement
                if (DateTime.Now.AddYears(-18) < request.Dob)
                {
                    ModelState.AddModelError(nameof(request.Dob), "You must be 18 years or older to apply");
                    return BadRequest(new
                    {
                        success = false,
                        errors = new { Dob = new[] { "You must be 18 years or older to apply" } }
                    });
                }

                // Check if email already exists
                var existingApplication = await _context.Applications
                    .FirstOrDefaultAsync(a => a.Email == request.Email);

                if (existingApplication != null)
                {
                    ModelState.AddModelError(nameof(request.Email), "Email address is already registered");
                    return BadRequest(new
                    {
                        success = false,
                        errors = new { Email = new[] { "Email address is already registered" } }
                    });
                }

                // Create new application or update existing one from session
                var application = new Application();

                // Map request to application model
                application.FirstName = request.FirstName;
                application.LastName = request.LastName;
                application.Email = request.Email;
                application.Phone = request.Phone;
                application.Gender = request.Gender;
                application.Dob = request.Dob;
                application.Status = "Draft";
                application.ApplicationStep = 2; // Move to next step
                application.ReferenceNumber = $"AYT-{Guid.NewGuid().ToString("N")[..10].ToUpper()}";
                // Hash password if provided
                if (!string.IsNullOrEmpty(request.Password))
                {
                    var HashPassword = BCrypt.Net.BCrypt.HashPassword(request.Password);
                    application.Password = HashPassword;
                }

                // Set timestamps
                if (application.ApplicationStep == 2) // New application
                {
                    application.CreatedAt = DateTime.UtcNow;
                    _context.Applications.Add(application);
                }
                else // Update existing
                {
                    application.UpdatedAt = DateTime.UtcNow;
                    _context.Applications.Update(application);
                }

                // Save to database
                await _context.SaveChangesAsync();
                // Example token generation
                // Generate verification token
                var token = await _verificationService.GenerateAndSaveTokenAsync(application.Id.ToString());
                var environment = HttpContext.Request; // Use HttpContext directly
                var baseUrl = $"{environment.Scheme}://{environment.Host}";
                // Build verification URL
                var verificationUrl = $"{baseUrl}/verify-email?token={token}";

                //Send email
                await _emailService.SendEmailAsync(
                   application.Email,
                   "Applicant",
                   "Application Received",
                   $@"<h2>Thank you for applying for the AYuTe Africa Challenge Nigeria</h2>
                    <p>Your application has been received.</p>
                    <p>Please verify your email address by clicking the button below:</p>
                    <div style='margin: 20px 0;'>
                        <a href='{verificationUrl}' 
                            style='
                            background-color: #007bff;
                            color: white;
                            padding: 12px 24px;
                            text-decoration: none;
                            border-radius: 4px;
                            font-weight: bold;
                            display: inline-block;
                            '>
                        Verify Email Address
                        </a>
                    </div>
                    <p>If the button doesn't work, you can also copy and paste this link:</p>
                    <p>{verificationUrl}</p>
                    <p><small>This verification link will expire in 24 hours.</small></p>"
               );
                TempData["Success"] = "Signup successful. Please login to continue";
                // Return success response
                return Ok(new
                {
                    success = true,
                    message = "Personal information saved successfully",
                    applicationId = application.Id,
                    redirectUrl = $"/verify?email={application.Email}",
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving step one application data");

                return StatusCode(500, new
                {
                    success = false,
                    message = "An error occurred while saving your information. Please try again."
                });
            }
        }


        // [HttpPost("resend-verification")]
        // public async Task<IActionResult> ResendVerification([FromBody] ResendVerificationRequest request)
        // {
        //     // Assuming you have user ID from auth or request
        //     var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        //     if (string.IsNullOrEmpty(userId))
        //     {
        //         return Unauthorized();
        //     }

        //     var result = await _userService.ResendVerificationEmailAsync(userId);

        //     if (result)
        //     {
        //         return Ok(new
        //         {
        //             success = true,
        //             message = "Verification email sent"
        //         });
        //     }

        //     return BadRequest(new
        //     {
        //         success = false,
        //         message = "Failed to send verification email"
        //     });

        // }

        [HttpPost("step-two/{applicationId}")] // Route parameter
        public async Task<IActionResult> StepTwo(
             Guid applicationId, // From route parameter
             [FromBody] StepTwoRequest request)
        {
            try
            {
                // Validate the request
                if (!ModelState.IsValid)
                {
                    return BadRequest(new
                    {
                        success = false,
                        errors = ModelState.ToDictionary(
                            kvp => kvp.Key,
                            kvp => kvp.Value.Errors.Select(e => e.ErrorMessage).ToArray()
                        )
                    });
                }

                // Validate applicationId from route parameter
                if (applicationId == Guid.Empty)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Application ID is required"
                    });
                }

                // Find existing application by ID
                var application = await _context.Applications
                    .Include(a => a.SocialMedia) // Include SocialMedia navigation property
                    .FirstOrDefaultAsync(a => a.Id == applicationId);

                if (application == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Application not found"
                    });
                }

                // Update application with step 2 data
                application.StartupName = request.StartupName;
                application.Url = request.Url;
                application.Description = request.Description;
                application.Locations = request.Locations;
                application.LegallyRegistered = request.LegallyRegistered;
                application.YearOfIncorporation = request.YearOfIncorporation;
                application.CacRegNumber = request.CacRegNumber;
                application.ApplicationStep = 2;
                application.UpdatedAt = DateTime.UtcNow;

                // Update SocialMedia owned entity
                if (application.SocialMedia == null)
                {
                    application.SocialMedia = new SocialMedia();
                }

                // Update social media properties
                application.SocialMedia.LinkedIn = request.SocialMedia?.LinkedIn;
                application.SocialMedia.X = request.SocialMedia?.X;
                application.SocialMedia.Facebook = request.SocialMedia?.Facebook;
                application.SocialMedia.Instagram = request.SocialMedia?.Instagram;

                // Validate conditional fields if legally registered
                if (request.LegallyRegistered)
                {
                    if (string.IsNullOrEmpty(request.CacRegNumber))
                    {
                        ModelState.AddModelError(nameof(request.CacRegNumber),
                            "Registration number is required for legally registered companies");
                    }

                    if (!request.YearOfIncorporation.HasValue)
                    {
                        ModelState.AddModelError(nameof(request.YearOfIncorporation),
                            "Year of incorporation is required for legally registered companies");
                    }
                    else if (request.YearOfIncorporation > DateTime.UtcNow.Year)
                    {
                        ModelState.AddModelError(nameof(request.YearOfIncorporation),
                            "Year of incorporation cannot be in the future");
                    }
                    else if (request.YearOfIncorporation < 1900)
                    {
                        ModelState.AddModelError(nameof(request.YearOfIncorporation),
                            "Please enter a valid year (after 1900)");
                    }

                    if (!ModelState.IsValid)
                    {
                        return BadRequest(new
                        {
                            success = false,
                            errors = ModelState.ToDictionary(
                                kvp => kvp.Key,
                                kvp => kvp.Value.Errors.Select(e => e.ErrorMessage).ToArray()
                            )
                        });
                    }
                }

                // Validate description length
                if (!string.IsNullOrEmpty(request.Description) && request.Description.Length > 1000)
                {
                    ModelState.AddModelError(nameof(request.Description),
                        "Description cannot exceed 1000 characters");
                }

                // Validate locations length
                if (!string.IsNullOrEmpty(request.Locations) && request.Locations.Length > 500)
                {
                    ModelState.AddModelError(nameof(request.Locations),
                        "Locations cannot exceed 500 characters");
                }

                // // Validate URLs if provided
                // var urlFields = new Dictionary<string, string>
                // {
                //     { nameof(request.Url), request.Url },
                //     { nameof(request.SocialMedia.LinkedIn), request.SocialMedia?.LinkedIn },
                //     { nameof(request.SocialMedia.X), request.SocialMedia?.X },
                //     { nameof(request.SocialMedia.Facebook), request.SocialMedia?.Facebook },
                //     { nameof(request.SocialMedia.Instagram), request.SocialMedia?.Instagram }
                // };

                // foreach (var field in urlFields)
                // {
                //     if (!string.IsNullOrEmpty(field.Value) && !Uri.TryCreate(field.Value, UriKind.Absolute, out _))
                //     {
                //         ModelState.AddModelError(field.Key, "Please enter a valid URL");
                //     }
                // }

                if (!ModelState.IsValid)
                {
                    return BadRequest(new
                    {
                        success = false,
                        errors = ModelState.ToDictionary(
                            kvp => kvp.Key,
                            kvp => kvp.Value.Errors.Select(e => e.ErrorMessage).ToArray()
                        )
                    });
                }

                // Save to database
                await _context.SaveChangesAsync();

                // Return success response with next step URL
                return Ok(new
                {
                    success = true,
                    message = "Company information saved successfully",
                    applicationId = application.Id,
                    redirectUrl = Url.Action("step-three", "application", new { id = application.Id })
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving step two application data");

                return StatusCode(500, new
                {
                    success = false,
                    message = "An error occurred while saving your information. Please try again."
                });
            }
        }

        ////////////////////////////////////
        /// SECTION THREE
        /// 
        [HttpPost("step-three/{applicationId}")]
        public async Task<IActionResult> StepThree(
            Guid applicationId,
            [FromBody] StepThreeRequest request)
        {
            try
            {
                // Validate the request
                if (!ModelState.IsValid)
                {
                    return BadRequest(new
                    {
                        success = false,
                        errors = ModelState.ToDictionary(
                            kvp => kvp.Key,
                            kvp => kvp.Value.Errors.Select(e => e.ErrorMessage).ToArray()
                        )
                    });
                }

                // Validate applicationId
                if (applicationId == Guid.Empty)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Application ID is required"
                    });
                }

                // Find existing application
                var application = await _context.Applications
                    .FirstOrDefaultAsync(a => a.Id == applicationId);

                if (application == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Application not found"
                    });
                }

                // Update application with step 3 data
                application.FarmerChallenges = request.FarmerChallenges;
                application.SolutionDescription = request.SolutionDescription;
                application.ProductStage = request.ProductStage;
                application.ProductLink = request.ProductLink;
                application.InnovationHighlight = request.InnovationHighlight;
                application.PrimaryUsers = request.PrimaryUsers;

                // Parse number of active users
                if (int.TryParse(request.NoOfActiveUsers?.ToString(), out int activeUsers))
                {
                    application.NoOfActiveUsers = activeUsers;
                }
                else if (!string.IsNullOrEmpty(request.NoOfActiveUsers?.ToString()))
                {
                    ModelState.AddModelError(nameof(request.NoOfActiveUsers),
                        "Please enter a valid number for active users");
                }

                application.ApplicationStep = 3;
                application.UpdatedAt = DateTime.UtcNow;

                // Validate text length limits
                if (!string.IsNullOrEmpty(request.FarmerChallenges) && request.FarmerChallenges.Length > 2000)
                {
                    ModelState.AddModelError(nameof(request.FarmerChallenges),
                        "Farmer challenges cannot exceed 2000 characters");
                }

                if (!string.IsNullOrEmpty(request.SolutionDescription) && request.SolutionDescription.Length > 2000)
                {
                    ModelState.AddModelError(nameof(request.SolutionDescription),
                        "Solution description cannot exceed 2000 characters");
                }

                if (!string.IsNullOrEmpty(request.InnovationHighlight) && request.InnovationHighlight.Length > 1500)
                {
                    ModelState.AddModelError(nameof(request.InnovationHighlight),
                        "Innovation highlight cannot exceed 1500 characters");
                }

                if (!string.IsNullOrEmpty(request.PrimaryUsers) && request.PrimaryUsers.Length > 1000)
                {
                    ModelState.AddModelError(nameof(request.PrimaryUsers),
                        "Primary users description cannot exceed 1000 characters");
                }

                // Validate URL if provided
                if (!string.IsNullOrEmpty(request.ProductLink) && !Uri.TryCreate(request.ProductLink, UriKind.Absolute, out _))
                {
                    ModelState.AddModelError(nameof(request.ProductLink),
                        "Please enter a valid URL");
                }

                // Validate number of active users
                if (application.NoOfActiveUsers < 0)
                {
                    ModelState.AddModelError(nameof(request.NoOfActiveUsers),
                        "Number of active users cannot be negative");
                }

                if (!ModelState.IsValid)
                {
                    return BadRequest(new
                    {
                        success = false,
                        errors = ModelState.ToDictionary(
                            kvp => kvp.Key,
                            kvp => kvp.Value.Errors.Select(e => e.ErrorMessage).ToArray()
                        )
                    });
                }

                // Save to database
                await _context.SaveChangesAsync();

                // Return success response
                return Ok(new
                {
                    success = true,
                    message = "Problem & Solution information saved successfully",
                    applicationId = application.Id,
                    redirectUrl = Url.Action("step-four", "application", new { id = application.Id })
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving step three application data");

                return StatusCode(500, new
                {
                    success = false,
                    message = "An error occurred while saving your information. Please try again."
                });
            }
        }

        [HttpPost("step-four/{applicationId}")]
        public async Task<IActionResult> StepFour(
            Guid applicationId,
            [FromBody] StepFourRequest request)
        {
            try
            {
                // Validate the request
                if (!ModelState.IsValid)
                {
                    return BadRequest(new
                    {
                        success = false,
                        errors = ModelState.ToDictionary(
                            kvp => kvp.Key,
                            kvp => kvp.Value.Errors.Select(e => e.ErrorMessage).ToArray()
                        )
                    });
                }

                // Validate applicationId
                if (applicationId == Guid.Empty)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Application ID is required"
                    });
                }

                // Find existing application
                var application = await _context.Applications
                    .FirstOrDefaultAsync(a => a.Id == applicationId);

                if (application == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Application not found"
                    });
                }

                // Update application with step 4 data
                application.BusinessModel = request.BusinessModel;
                application.IsRevenueGeneration = request.IsRevenueGeneration;
                application.GoToMarketStrategy = request.GoToMarketStrategy;
                application.Competitors = request.Competitors;
                application.ApplicationStep = 4;
                application.UpdatedAt = DateTime.UtcNow;

                // Parse number of customers
                if (int.TryParse(request.NoOfCustomers?.ToString(), out int customers))
                {
                    application.NoOfCustomers = customers;
                }
                else
                {
                    ModelState.AddModelError(nameof(request.NoOfCustomers),
                        "Please enter a valid number for customers");
                }

                // Parse average CAC
                if (!string.IsNullOrEmpty(request.AverageCac?.ToString()))
                {
                    if (decimal.TryParse(request.AverageCac.ToString(), out decimal cac))
                    {
                        application.AverageCac = cac;
                    }
                    else
                    {
                        ModelState.AddModelError(nameof(request.AverageCac),
                            "Please enter a valid amount for average CAC");
                    }
                }

                // Validate text length limits
                if (!string.IsNullOrEmpty(request.BusinessModel) && request.BusinessModel.Length > 1500)
                {
                    ModelState.AddModelError(nameof(request.BusinessModel),
                        "Business model cannot exceed 1500 characters");
                }

                if (!string.IsNullOrEmpty(request.GoToMarketStrategy) && request.GoToMarketStrategy.Length > 1500)
                {
                    ModelState.AddModelError(nameof(request.GoToMarketStrategy),
                        "Go-to-market strategy cannot exceed 1500 characters");
                }

                if (!string.IsNullOrEmpty(request.Competitors) && request.Competitors.Length > 1500)
                {
                    ModelState.AddModelError(nameof(request.Competitors),
                        "Competitors description cannot exceed 1500 characters");
                }

                // Validate number of customers
                if (application.NoOfCustomers < 0)
                {
                    ModelState.AddModelError(nameof(request.NoOfCustomers),
                        "Number of customers cannot be negative");
                }

                // Validate average CAC
                if (application.AverageCac.HasValue && application.AverageCac < 0)
                {
                    ModelState.AddModelError(nameof(request.AverageCac),
                        "Average CAC cannot be negative");
                }

                if (!ModelState.IsValid)
                {
                    return BadRequest(new
                    {
                        success = false,
                        errors = ModelState.ToDictionary(
                            kvp => kvp.Key,
                            kvp => kvp.Value.Errors.Select(e => e.ErrorMessage).ToArray()
                        )
                    });
                }

                // Save to database
                await _context.SaveChangesAsync();

                // Return success response
                return Ok(new
                {
                    success = true,
                    message = "Business model information saved successfully",
                    applicationId = application.Id,
                    redirectUrl = Url.Action("step-five", "application", new { id = application.Id })
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving step four application data");

                return StatusCode(500, new
                {
                    success = false,
                    message = "An error occurred while saving your information. Please try again."
                });
            }
        }

        [HttpPost("step-five/{applicationId}")]
        public async Task<IActionResult> StepFive(
        Guid applicationId,
        [FromBody] StepFiveRequest request)
        {
            try
            {
                // Validate the request
                if (!ModelState.IsValid)
                {
                    return BadRequest(new
                    {
                        success = false,
                        errors = ModelState.ToDictionary(
                            kvp => kvp.Key,
                            kvp => kvp.Value.Errors.Select(e => e.ErrorMessage).ToArray()
                        )
                    });
                }

                // Validate applicationId
                if (applicationId == Guid.Empty)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Application ID is required"
                    });
                }

                // Find existing application
                var application = await _context.Applications
                    .FirstOrDefaultAsync(a => a.Id == applicationId);

                if (application == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Application not found"
                    });
                }

                // Parse farmers served values
                if (int.TryParse(request.FarmersServedPreviousYear?.ToString(), out int farmersPreviousYear))
                {
                    application.FarmersServedPreviousYear = farmersPreviousYear;
                }
                else
                {
                    ModelState.AddModelError(nameof(request.FarmersServedPreviousYear),
                        "Please enter a valid number for farmers served (previous year)");
                }

                if (int.TryParse(request.FarmersServedTotal?.ToString(), out int farmersTotal))
                {
                    application.FarmersServedTotal = farmersTotal;
                }
                else
                {
                    ModelState.AddModelError(nameof(request.FarmersServedTotal),
                        "Please enter a valid number for total farmers served");
                }

                // Update other fields
                application.ImpactOnFarmers = request.ImpactOnFarmers;
                application.SustainabilityPromotion = request.SustainabilityPromotion;
                application.ImpactEvidence = request.ImpactEvidence;
                application.ApplicationStep = 5; // Move to next step
                application.UpdatedAt = DateTime.UtcNow;

                // Validate that total farmers >= previous year farmers
                // if (application.FarmersServedPreviousYear.HasValue && 
                //     application.FarmersServedTotal.HasValue &&
                //     application.FarmersServedPreviousYear > application.FarmersServedTotal)
                // {
                //     ModelState.AddModelError(nameof(request.FarmersServedPreviousYear), 
                //         "Farmers served in previous year cannot be greater than total farmers served");
                // }

                // Validate text length limits
                if (!string.IsNullOrEmpty(request.ImpactOnFarmers) && request.ImpactOnFarmers.Length > 1500)
                {
                    ModelState.AddModelError(nameof(request.ImpactOnFarmers),
                        "Impact on farmers cannot exceed 1500 characters");
                }

                if (!string.IsNullOrEmpty(request.SustainabilityPromotion) && request.SustainabilityPromotion.Length > 1500)
                {
                    ModelState.AddModelError(nameof(request.SustainabilityPromotion),
                        "Sustainability promotion cannot exceed 1500 characters");
                }

                if (!string.IsNullOrEmpty(request.ImpactEvidence) && request.ImpactEvidence.Length > 1000)
                {
                    ModelState.AddModelError(nameof(request.ImpactEvidence),
                        "Impact evidence cannot exceed 1000 characters");
                }

                // Validate number ranges
                // if (application.FarmersServedPreviousYear.HasValue && application.FarmersServedPreviousYear < 0)
                // {
                //     ModelState.AddModelError(nameof(request.FarmersServedPreviousYear), 
                //         "Farmers served (previous year) cannot be negative");
                // }

                // if (application.FarmersServedTotal.HasValue && application.FarmersServedTotal < 0)
                // {
                //     ModelState.AddModelError(nameof(request.FarmersServedTotal), 
                //         "Total farmers served cannot be negative");
                // }

                if (!ModelState.IsValid)
                {
                    return BadRequest(new
                    {
                        success = false,
                        errors = ModelState.ToDictionary(
                            kvp => kvp.Key,
                            kvp => kvp.Value.Errors.Select(e => e.ErrorMessage).ToArray()
                        )
                    });
                }

                // Save to database
                await _context.SaveChangesAsync();

                // Return success response
                return Ok(new
                {
                    success = true,
                    message = "Impact information saved successfully",
                    applicationId = application.Id,
                    redirectUrl = Url.Action("step-six", "application", new { id = application.Id })
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving step five application data");

                return StatusCode(500, new
                {
                    success = false,
                    message = "An error occurred while saving your information. Please try again."
                });
            }
        }
        [HttpPost("step-six/{applicationId}")]
        public async Task<IActionResult> StepSix(
            Guid applicationId,
            [FromBody] StepSixRequest request)
        {
            try
            {
                // Validate the request
                if (!ModelState.IsValid)
                {
                    return BadRequest(new
                    {
                        success = false,
                        errors = ModelState.ToDictionary(
                            kvp => kvp.Key,
                            kvp => kvp.Value.Errors.Select(e => e.ErrorMessage).ToArray()
                        )
                    });
                }

                // Validate applicationId
                if (applicationId == Guid.Empty)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Application ID is required"
                    });
                }

                // Find existing application
                var application = await _context.Applications
                    .FirstOrDefaultAsync(a => a.Id == applicationId);

                if (application == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Application not found"
                    });
                }

                // Update application with step 6 data
                application.GenderInclusion = request.GenderInclusion;
                application.EnvironmentalSustainability = request.EnvironmentalSustainability;
                application.DataProtectionMeasures = request.DataProtectionMeasures;
                application.ApplicationStep = 6; // Mark as completed or move to review
                application.UpdatedAt = DateTime.UtcNow;

                // Parse jobs created
                if (int.TryParse(request.JobsCreated?.ToString(), out int jobs))
                {
                    application.JobsCreated = jobs;
                }
                else
                {
                    ModelState.AddModelError(nameof(request.JobsCreated),
                        "Please enter a valid number for jobs created");
                }

                // Validate text length limits
                if (!string.IsNullOrEmpty(request.GenderInclusion) && request.GenderInclusion.Length > 1500)
                {
                    ModelState.AddModelError(nameof(request.GenderInclusion),
                        "Gender inclusion description cannot exceed 1500 characters");
                }

                if (!string.IsNullOrEmpty(request.EnvironmentalSustainability) && request.EnvironmentalSustainability.Length > 1500)
                {
                    ModelState.AddModelError(nameof(request.EnvironmentalSustainability),
                        "Environmental sustainability description cannot exceed 1500 characters");
                }

                if (!string.IsNullOrEmpty(request.DataProtectionMeasures) && request.DataProtectionMeasures.Length > 1500)
                {
                    ModelState.AddModelError(nameof(request.DataProtectionMeasures),
                        "Data protection measures cannot exceed 1500 characters");
                }

                // Validate jobs created range
                // if (application.JobsCreated.HasValue && application.JobsCreated < 0)
                // {
                //     ModelState.AddModelError(nameof(request.JobsCreated), 
                //         "Jobs created cannot be negative");
                // }

                // if (application.JobsCreated.HasValue && application.JobsCreated > 10000)
                // {
                //     ModelState.AddModelError(nameof(request.JobsCreated), 
                //         "Jobs created cannot exceed 10,000");
                // }

                if (!ModelState.IsValid)
                {
                    return BadRequest(new
                    {
                        success = false,
                        errors = ModelState.ToDictionary(
                            kvp => kvp.Key,
                            kvp => kvp.Value.Errors.Select(e => e.ErrorMessage).ToArray()
                        )
                    });
                }

                // Save to database
                await _context.SaveChangesAsync();

                // Return success response - redirect to review or completion page
                return Ok(new
                {
                    success = true,
                    message = "Inclusion & Sustainability information saved successfully",
                    applicationId = application.Id,
                    redirectUrl = Url.Action("step-seven", "application", new { id = application.Id })
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving step six application data");

                return StatusCode(500, new
                {
                    success = false,
                    message = "An error occurred while saving your information. Please try again."
                });
            }
        }

        [HttpPost("step-seven/{applicationId}")]
        public async Task<IActionResult> StepSeven(
            Guid applicationId,
            [FromBody] StepSevenRequest request)
        {
            try
            {
                // Validate the request
                if (!ModelState.IsValid)
                {
                    return BadRequest(new
                    {
                        success = false,
                        errors = ModelState.ToDictionary(
                            kvp => kvp.Key,
                            kvp => kvp.Value.Errors.Select(e => e.ErrorMessage).ToArray()
                        )
                    });
                }

                // Validate applicationId
                if (applicationId == Guid.Empty)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Application ID is required"
                    });
                }

                // Find existing application
                var application = await _context.Applications
                    .FirstOrDefaultAsync(a => a.Id == applicationId);

                if (application == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Application not found"
                    });
                }

                // Parse number fields
                if (int.TryParse(request.NoOfFounders?.ToString(), out int founders))
                {
                    application.NoOfFounders = founders;
                }
                else
                {
                    ModelState.AddModelError(nameof(request.NoOfFounders),
                        "Please enter a valid number for founders");
                }

                if (int.TryParse(request.NoOfEmployees?.ToString(), out int employees))
                {
                    application.NoOfEmployees = employees;
                }
                else
                {
                    ModelState.AddModelError(nameof(request.NoOfEmployees),
                        "Please enter a valid number for employees");
                }

                // Update other fields
                application.FoundersDetails = request.FoundersDetails;
                application.TeamSkill = request.TeamSkill;
                application.ApplicationStep = 7; // Move to next step
                application.UpdatedAt = DateTime.UtcNow;

                // Validate that employees >= founders
                if (application.NoOfFounders.HasValue &&
                    application.NoOfEmployees.HasValue &&
                    application.NoOfFounders > application.NoOfEmployees)
                {
                    ModelState.AddModelError(nameof(request.NoOfFounders),
                        "Number of founders cannot be greater than total employees");
                }

                // Validate text length limits
                if (!string.IsNullOrEmpty(request.FoundersDetails) && request.FoundersDetails.Length > 2000)
                {
                    ModelState.AddModelError(nameof(request.FoundersDetails),
                        "Founders details cannot exceed 2000 characters");
                }

                if (!string.IsNullOrEmpty(request.TeamSkill) && request.TeamSkill.Length > 1500)
                {
                    ModelState.AddModelError(nameof(request.TeamSkill),
                        "Team skills cannot exceed 1500 characters");
                }

                // Validate number ranges
                if (application.NoOfFounders.HasValue &&
                    (application.NoOfFounders < 1 || application.NoOfFounders > 10))
                {
                    ModelState.AddModelError(nameof(request.NoOfFounders),
                        "Number of founders must be between 1 and 10");
                }

                if (application.NoOfEmployees.HasValue &&
                    (application.NoOfEmployees < 1 || application.NoOfEmployees > 1000))
                {
                    ModelState.AddModelError(nameof(request.NoOfEmployees),
                        "Number of employees must be between 1 and 1000");
                }

                if (!ModelState.IsValid)
                {
                    return BadRequest(new
                    {
                        success = false,
                        errors = ModelState.ToDictionary(
                            kvp => kvp.Key,
                            kvp => kvp.Value.Errors.Select(e => e.ErrorMessage).ToArray()
                        )
                    });
                }

                // Save to database
                await _context.SaveChangesAsync();

                // Return success response
                return Ok(new
                {
                    success = true,
                    message = "Team information saved successfully",
                    applicationId = application.Id,
                    redirectUrl = Url.Action("step-eight", "application", new { id = application.Id })
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving step seven application data");

                return StatusCode(500, new
                {
                    success = false,
                    message = "An error occurred while saving your information. Please try again."
                });
            }
        }

        [HttpPost("step-eight/{applicationId}")]
        public async Task<IActionResult> StepEight(
            Guid applicationId,
            [FromBody] StepEightRequest request)
        {
            try
            {
                // Validate the request
                if (!ModelState.IsValid)
                {
                    return BadRequest(new
                    {
                        success = false,
                        errors = ModelState.ToDictionary(
                            kvp => kvp.Key,
                            kvp => kvp.Value.Errors.Select(e => e.ErrorMessage).ToArray()
                        )
                    });
                }

                // Validate applicationId
                if (applicationId == Guid.Empty)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Application ID is required"
                    });
                }

                // Find existing application
                var application = await _context.Applications
                    .FirstOrDefaultAsync(a => a.Id == applicationId);

                if (application == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Application not found"
                    });
                }

                // Update application with step 8 data
                application.Milestone = request.Milestone;
                application.BiggestRiskFacing = request.BiggestRiskFacing;
                application.TwelveMonthRevenueProjection = request.TwelveMonthRevenueProjection;
                application.LongTermVision = request.LongTermVision;
                application.ApplicationStep = 8; // Mark as completed
                                                 // application.ApplicationStatus = ApplicationStatus.Submitted; // Change status to submitted
                application.UpdatedAt = DateTime.UtcNow;
                // application.SubmittedAt = DateTime.UtcNow; // Mark submission timestamp

                // Validate text length limits
                if (!string.IsNullOrEmpty(request.Milestone) && request.Milestone.Length > 1500)
                {
                    ModelState.AddModelError(nameof(request.Milestone),
                        "Milestones cannot exceed 1500 characters");
                }

                if (!string.IsNullOrEmpty(request.BiggestRiskFacing) && request.BiggestRiskFacing.Length > 1500)
                {
                    ModelState.AddModelError(nameof(request.BiggestRiskFacing),
                        "Risk assessment cannot exceed 1500 characters");
                }

                if (!string.IsNullOrEmpty(request.TwelveMonthRevenueProjection) && request.TwelveMonthRevenueProjection.Length > 1500)
                {
                    ModelState.AddModelError(nameof(request.TwelveMonthRevenueProjection),
                        "Revenue projection cannot exceed 1500 characters");
                }

                if (!string.IsNullOrEmpty(request.LongTermVision) && request.LongTermVision.Length > 1500)
                {
                    ModelState.AddModelError(nameof(request.LongTermVision),
                        "Long-term vision cannot exceed 1500 characters");
                }

                if (!ModelState.IsValid)
                {
                    return BadRequest(new
                    {
                        success = false,
                        errors = ModelState.ToDictionary(
                            kvp => kvp.Key,
                            kvp => kvp.Value.Errors.Select(e => e.ErrorMessage).ToArray()
                        )
                    });
                }

                // Save to database
                await _context.SaveChangesAsync();

                // Return success response - redirect to completion/review page
                return Ok(new
                {
                    success = true,
                    message = "Growth & Vision information saved successfully. Application submitted!",
                    applicationId = application.Id,
                    redirectUrl = Url.Action("step-nine", "application", new { id = application.Id })
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving step eight application data");

                return StatusCode(500, new
                {
                    success = false,
                    message = "An error occurred while saving your information. Please try again."
                });
            }
        }

        [HttpPost("upload-document")]
        public async Task<IActionResult> UploadDocument(
            [FromForm] IFormFile file,
            [FromForm] string fieldName,
            [FromForm] string applicationId)
        {
            try
            {
                if (file == null || file.Length == 0)
                {
                    return BadRequest(new { success = false, message = "No file uploaded" });
                }

                // Validate file type
                if (!IsValidPdfFile(file))
                {
                    return BadRequest(new { success = false, message = "Only PDF files are allowed" });
                }

                // Validate file size (10MB)
                if (file.Length > 10 * 1024 * 1024)
                {
                    return BadRequest(new { success = false, message = "File size must be less than 10MB" });
                }

                // Upload to local storage
                var uploadResult = await UploadToLocalStorage(file, fieldName, applicationId);

                if (!uploadResult.Success)
                {
                    _logger.LogError("Local storage upload error: {Error}", uploadResult.ErrorMessage);
                    return StatusCode(500, new
                    {
                        success = false,
                        message = $"Upload failed: {uploadResult.ErrorMessage}"
                    });
                }

                return Ok(new
                {
                    success = true,
                    url = uploadResult.Url,
                    fileName = uploadResult.FileName,
                    message = "File uploaded successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading document");
                return StatusCode(500, new
                {
                    success = false,
                    message = "An error occurred while uploading the file"
                });
            }
        }

        private async Task<LocalUploadResult> UploadToLocalStorage(IFormFile file, string fieldName, string applicationId)
        {
            try
            {
                // Get the web root path
                var webRootPath = _hostingEnvironment.WebRootPath;

                // If web root doesn't exist, use content root
                if (string.IsNullOrEmpty(webRootPath))
                {
                    webRootPath = _hostingEnvironment.ContentRootPath;
                }

                // Create folder by field type (PitchDeckUrl, CacUrl, etc.)
                var uploadsFolder = Path.Combine(webRootPath, "uploads", fieldName.ToLower());
                Directory.CreateDirectory(uploadsFolder);

                // Get unique filename in format: {applicationId}_{fieldName.ToLower()}.pdf
                var finalFileName = GetUniqueFileName(uploadsFolder, file.FileName, applicationId, fieldName);
                var filePath = Path.Combine(uploadsFolder, finalFileName);

                // Save file
                using (var fileStream = new System.IO.FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(fileStream);
                }

                // Generate ABSOLUTE URL
                var request = HttpContext.Request; // Use HttpContext directly
                var baseUrl = $"{request.Scheme}://{request.Host}";
                var url = $"{baseUrl}/uploads/{fieldName.ToLower()}/{finalFileName}";

                return new LocalUploadResult
                {
                    Success = true,
                    Url = url, // Now this is an absolute URL
                    FileName = finalFileName,
                    ErrorMessage = null
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading to local storage");
                return new LocalUploadResult
                {
                    Success = false,
                    Url = null,
                    FileName = null,
                    ErrorMessage = ex.Message
                };
            }
        }
        // Helper method to sanitize filename
        private string SanitizeFileName(string fileName)
        {
            // Remove path characters
            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = new string(fileName
                .Where(ch => !invalidChars.Contains(ch))
                .ToArray());

            // Replace spaces with underscores (optional)
            sanitized = sanitized.Replace(" ", "_");

            // Trim to reasonable length
            if (sanitized.Length > 100)
            {
                var extension = Path.GetExtension(sanitized);
                var nameWithoutExtension = Path.GetFileNameWithoutExtension(sanitized);
                sanitized = nameWithoutExtension.Substring(0, 95) + extension;
            }

            return sanitized;
        }
        private string GetUniqueFileName(string folderPath, string fileName, string applicationId, string fieldName)
        {
            // Generate base filename: {applicationId}_{fieldName.ToLower()}
            var baseFileName = $"{applicationId}_{fieldName.ToLower()}";
            var extension = Path.GetExtension(fileName); // Keep original file extension

            // Combine to create final filename
            var newFileName = $"{baseFileName}{extension}";
            var filePath = Path.Combine(folderPath, newFileName);

            // If file doesn't exist, return the generated filename
            if (!System.IO.File.Exists(filePath))
            {
                return newFileName;
            }

            // If file exists (shouldn't happen with this format), append counter
            var counter = 1;
            while (System.IO.File.Exists(filePath))
            {
                newFileName = $"{baseFileName}_{counter}{extension}";
                filePath = Path.Combine(folderPath, newFileName);
                counter++;

                // Safety limit
                if (counter > 100)
                {
                    // Fallback with timestamp
                    var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
                    return $"{baseFileName}_{timestamp}{extension}";
                }
            }

            return newFileName;
        }
        private bool IsValidPdfFile(IFormFile file)
        {
            // Check file extension
            var validExtensions = new[] { ".pdf" };
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

            if (!validExtensions.Contains(extension))
            {
                return false;
            }

            // Check content type
            var validContentTypes = new[]
            {
                "application/pdf",
                "application/x-pdf"
            };

            if (!validContentTypes.Contains(file.ContentType.ToLower()))
            {
                return false;
            }

            // Read first bytes to check PDF signature
            using (var stream = file.OpenReadStream())
            {
                byte[] header = new byte[5];
                stream.Read(header, 0, 5);
                stream.Position = 0; // Reset stream position

                // Check if file starts with PDF signature "%PDF-"
                return header[0] == 0x25 && // %
                       header[1] == 0x50 && // P
                       header[2] == 0x44 && // D
                       header[3] == 0x46 && // F
                       header[4] == 0x2D;   // -
            }
        }

        [HttpPost("step-nine/{applicationId}")]
        public async Task<IActionResult> StepNine(
            Guid applicationId,
            [FromBody] StepNineRequest request)
        {
            Console.WriteLine("ksdcdcsdc");
            try
            {
                // Validate the request
                if (!ModelState.IsValid)
                {
                    return BadRequest(new
                    {
                        success = false,
                        errors = ModelState.ToDictionary(
                            kvp => kvp.Key,
                            kvp => kvp.Value.Errors.Select(e => e.ErrorMessage).ToArray()
                        )
                    });
                }

                // Validate applicationId
                if (applicationId == Guid.Empty)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Application ID is required"
                    });
                }

                // Find existing application
                var application = await _context.Applications
                    .FirstOrDefaultAsync(a => a.Id == applicationId);

                if (application == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Application not found"
                    });
                }

                // Update application with step 9 data
                application.PitchDeckUrl = request.PitchDeckUrl;
                application.CacUrl = request.CacUrl;
                application.TinUrl = request.TinUrl;
                application.OthersUrl = request.OthersUrl;
                application.AgreeToToS_Ayute = request.AgreeToToS_Ayute;
                application.AgreeToToS_Heifer = request.AgreeToToS_Heifer;
                application.ApplicationStep = 9; // Mark as completed
                application.Status = "Submitted";
                application.UpdatedAt = DateTime.UtcNow;
                // application.SubmittedAt = DateTime.UtcNow;

                // Validate required documents
                if (string.IsNullOrEmpty(request.PitchDeckUrl))
                {
                    ModelState.AddModelError(nameof(request.PitchDeckUrl),
                        "Pitch Deck is required");
                }

                if (string.IsNullOrEmpty(request.CacUrl))
                {
                    ModelState.AddModelError(nameof(request.CacUrl),
                        "CAC Certificate is required");
                }

                // Validate agreements
                if (!request.AgreeToToS_Ayute)
                {
                    ModelState.AddModelError(nameof(request.AgreeToToS_Ayute),
                        "You must agree to Ayute Terms of Service");
                }

                if (!request.AgreeToToS_Heifer)
                {
                    ModelState.AddModelError(nameof(request.AgreeToToS_Heifer),
                        "You must agree to Heifer International Terms");
                }

                // Validate URLs (if provided)
                var urlFields = new Dictionary<string, string>
            {
                { nameof(request.PitchDeckUrl), request.PitchDeckUrl },
                { nameof(request.CacUrl), request.CacUrl },
                { nameof(request.TinUrl), request.TinUrl },
                { nameof(request.OthersUrl), request.OthersUrl }
            };

                foreach (var field in urlFields)
                {
                    if (!string.IsNullOrEmpty(field.Value) && !Uri.TryCreate(field.Value, UriKind.Absolute, out _))
                    {
                        ModelState.AddModelError(field.Key, "Please provide a valid URL");
                    }
                }

                if (!ModelState.IsValid)
                {
                    return BadRequest(new
                    {
                        success = false,
                        errors = ModelState.ToDictionary(
                            kvp => kvp.Key,
                            kvp => kvp.Value.Errors.Select(e => e.ErrorMessage).ToArray()
                        )
                    });
                }

                // Save to database
                await _context.SaveChangesAsync();
                await _emailService.SendEmailAsync(
                  application.Email,
                  "Applicant",
                  "Application Received",
                  $@"
                    <h2>Thank You for Applying to the AYuTe Africa Challenge Nigeria</h2>

                    <p>Thank you for your interest in the AYuTe Africa Challenge Nigeria. We have successfully received your application. Ref: {application.ReferenceNumber}</p>

                    <p>You will receive a follow-up email from us soon with next steps and additional information regarding your application.</p>

                    <p>If you did not submit an application for the AYuTe Africa Challenge Nigeria, you may safely ignore this email.</p>

                    <p>Best regards,<br/>
                    <strong>AYuTe Africa Challenge Nigeria Team</strong></p>
                    "

              );
                // Return success response
                return Ok(new
                {
                    success = true,
                    message = "Application submitted successfully!",
                    applicationId = application.Id,
                    redirectUrl = Url.Action("success", "application", new { id = application.Id })
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving step nine application data");

                return StatusCode(500, new
                {
                    success = false,
                    message = "An error occurred while submitting your application. Please try again."
                });
            }
        }


        private static readonly IReadOnlyDictionary<int, string> StepRoutes =
            new Dictionary<int, string>
            {
                { 2, "step-two" },
                { 3, "step-three" },
                { 4, "step-four" },
                { 5, "step-five" },
                { 6, "step-six" },
                { 7, "step-seven" },
                { 8, "step-eight" },
                { 9, "step-nine" },
            };
        [HttpGet("export")]
        public async Task<IActionResult> ExportApplications(
                    [FromQuery] string format = "csv",
                    [FromQuery] string status = "",
                    [FromQuery] DateTime? startDate = null,
                    [FromQuery] DateTime? endDate = null)
        {
            var query = _context.Applications.AsQueryable();

            // Apply filters
            if (!string.IsNullOrEmpty(status))
            {
                query = query.Where(a => a.Status == status);
            }

            if (startDate.HasValue)
            {
                query = query.Where(a => a.CreatedAt >= startDate.Value);
            }

            if (endDate.HasValue)
            {
                query = query.Where(a => a.CreatedAt <= endDate.Value);
            }

            var applications = await query
                .OrderByDescending(a => a.CreatedAt)
                .ToListAsync();

            // Generate file based on format
            byte[] fileContent;
            string contentType;
            string fileName;

            switch (format.ToLower())
            {
                case "csv":
                    fileContent = GenerateCsv(applications);
                    contentType = "text/csv";
                    fileName = $"applications_{DateTime.UtcNow:yyyyMMddHHmmss}.csv";
                    break;

                case "excel":
                    fileContent = GenerateExcel(applications);
                    contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                    fileName = $"applications_{DateTime.UtcNow:yyyyMMddHHmmss}.xlsx";
                    break;

                case "pdf":
                    fileContent = GeneratePdf(applications);
                    contentType = "application/pdf";
                    fileName = $"applications_{DateTime.UtcNow:yyyyMMddHHmmss}.pdf";
                    break;

                case "json":
                    fileContent = System.Text.Encoding.UTF8.GetBytes(
                        System.Text.Json.JsonSerializer.Serialize(applications));
                    contentType = "application/json";
                    fileName = $"applications_{DateTime.UtcNow:yyyyMMddHHmmss}.json";
                    break;

                default:
                    return BadRequest(new { message = "Invalid format" });
            }

            return File(fileContent, contentType, fileName);
        }

        // GET: /api/applications/{id}/export
        [HttpGet("{id}/export")]
        public async Task<IActionResult> ExportSingleApplication(Guid id)
        {
            var application = await _context.Applications
                .FirstOrDefaultAsync(a => a.Id == id);

            if (application == null)
            {
                return NotFound();
            }

            // Generate PDF for single application
            var fileContent = GenerateApplicationPdf(application);

            return File(fileContent, "application/pdf",
                $"application_{application.ReferenceNumber}_{DateTime.UtcNow:yyyyMMdd}.pdf");
        }

        private byte[] GenerateCsv(List<Application> applications)
        {
            using var memoryStream = new System.IO.MemoryStream();
            using var writer = new System.IO.StreamWriter(memoryStream);

            // Write header
            writer.WriteLine(
                "Reference Number,Status," +

                // SECTION A – Founder Info
                "First Name,Last Name,Email,Phone,Gender,Date of Birth,Is Verified," +

                // SECTION B – Startup Info
                "Startup Name,Website URL,Description,Locations,Legally Registered,Year of Incorporation,CAC Registration Number," +

                // Social Media
                "LinkedIn,X (Twitter),Instagram,Facebook," +

                // SECTION C – Problem & Solution
                "Farmer Challenges,Solution Description,Product Stage,Product Link,Innovation Highlight,Primary Users,Number of Active Users," +

                // SECTION D – Business Model
                "Business Model,Revenue Generating,Go To Market Strategy,Number of Customers,Average CAC,Competitors," +

                // SECTION E – Impact
                "Farmers Served (Previous Year),Farmers Served (Total),Impact on Farmers,Sustainability Promotion,Impact Evidence," +

                // SECTION F – Inclusion & Sustainability
                "Gender Inclusion,Jobs Created,Environmental Sustainability,Data Protection Measures," +

                // SECTION G – Team
                "Number of Founders,Founders Details,Number of Employees,Team Skill," +

                // SECTION H – Growth & Vision
                "Milestone,Biggest Risk,Twelve Month Revenue Projection,Long Term Vision," +

                // SECTION I – Documents & Agreements
                "Pitch Deck URL,CAC Document URL,TIN Document URL,Other Documents URL,Agree To Ayute ToS,Agree To Heifer ToS," +

                // Auditing
                "Created At,Updated At"
            );


            // Write data
            foreach (var app in applications)
            {
                writer.WriteLine(
                $"\"{app.ReferenceNumber}\"," +
                $"\"{app.Status}\"," +

                // SECTION A – Founder Info
                $"\"{app.FirstName}\"," +
                $"\"{app.LastName}\"," +
                $"\"{app.Email}\"," +
                $"\"{app.Phone}\"," +
                $"\"{app.Gender}\"," +
                $"\"{app.Dob:yyyy-MM-dd}\"," +
                $"\"{app.IsVerified}\"," +

                // SECTION B – Startup Info
                $"\"{app.StartupName}\"," +
                $"\"{app.Url}\"," +
                $"\"{app.Description}\"," +
                $"\"{app.Locations}\"," +
                $"\"{app.LegallyRegistered}\"," +
                $"\"{app.YearOfIncorporation}\"," +
                $"\"{app.CacRegNumber}\"," +

                // Social Media (Owned Entity)
                $"\"{app.SocialMedia?.LinkedIn}\"," +
                $"\"{app.SocialMedia?.X}\"," +
                $"\"{app.SocialMedia?.Instagram}\"," +
                $"\"{app.SocialMedia?.Facebook}\"," +

                // SECTION C – Problem & Solution
                $"\"{app.FarmerChallenges}\"," +
                $"\"{app.SolutionDescription}\"," +
                $"\"{app.ProductStage}\"," +
                $"\"{app.ProductLink}\"," +
                $"\"{app.InnovationHighlight}\"," +
                $"\"{app.PrimaryUsers}\"," +
                $"\"{app.NoOfActiveUsers}\"," +

                // SECTION D – Business Model
                $"\"{app.BusinessModel}\"," +
                $"\"{app.IsRevenueGeneration}\"," +
                $"\"{app.GoToMarketStrategy}\"," +
                $"\"{app.NoOfCustomers}\"," +
                $"\"{app.AverageCac}\"," +
                $"\"{app.Competitors}\"," +

                // SECTION E – Impact
                $"\"{app.FarmersServedPreviousYear}\"," +
                $"\"{app.FarmersServedTotal}\"," +
                $"\"{app.ImpactOnFarmers}\"," +
                $"\"{app.SustainabilityPromotion}\"," +
                $"\"{app.ImpactEvidence}\"," +

                // SECTION F – Inclusion & Sustainability
                $"\"{app.GenderInclusion}\"," +
                $"\"{app.JobsCreated}\"," +
                $"\"{app.EnvironmentalSustainability}\"," +
                $"\"{app.DataProtectionMeasures}\"," +

                // SECTION G – Team
                $"\"{app.NoOfFounders}\"," +
                $"\"{app.FoundersDetails}\"," +
                $"\"{app.NoOfEmployees}\"," +
                $"\"{app.TeamSkill}\"," +

                // SECTION H – Growth & Vision
                $"\"{app.Milestone}\"," +
                $"\"{app.BiggestRiskFacing}\"," +
                $"\"{app.TwelveMonthRevenueProjection}\"," +
                $"\"{app.LongTermVision}\"," +

                // SECTION I – Documents & Agreements
                $"\"{app.PitchDeckUrl}\"," +
                $"\"{app.CacUrl}\"," +
                $"\"{app.TinUrl}\"," +
                $"\"{app.OthersUrl}\"," +
                $"\"{app.AgreeToToS_Ayute}\"," +
                $"\"{app.AgreeToToS_Heifer}\"," +

                // Auditing
                $"\"{app.CreatedAt:yyyy-MM-dd HH:mm}\"," +
                $"\"{app.UpdatedAt:yyyy-MM-dd HH:mm}\""
            );

            }

            writer.Flush();
            return memoryStream.ToArray();
        }

        private byte[] GenerateExcel(List<Application> applications)
        {
            // Implement with EPPlus or ClosedXML
            // For now, return CSV as Excel
            return GenerateCsv(applications);
        }

        private byte[] GeneratePdf(List<Application> applications)
        {
            // Implement with iTextSharp or QuestPDF
            // For now, return a simple text file
            var pdfContent = System.Text.Encoding.UTF8.GetBytes("PDF Export - Implement with PDF library");
            return pdfContent;
        }

        // private byte[] GenerateApplicationPdf(Application application)
        // {
        //     // Generate detailed PDF for single application
        //     var pdfContent = System.Text.Encoding.UTF8.GetBytes($"Application: {application.FirstName} {application.LastName}");
        //     return pdfContent;
        // }

        // PUT: /api/applications/{id}/status
        [HttpPut("{id}/status")]
        public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateStatusRequest request)
        {
            try
            {
                var application = await _context.Applications.FindAsync(id);
                if (application == null)
                {
                    return NotFound(new { message = "Application not found" });
                }

                // Update status
                application.Status = request.Status;
                application.UpdatedAt = DateTime.UtcNow;

                // Save note if provided
                if (!string.IsNullOrEmpty(request.Notes))
                {
                    // You might want to create an ApplicationNotes table
                    // For now, we'll just update a field if you add one
                    // application.Notes = request.Notes;
                }

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    message = "Status updated successfully",
                    newStatus = application.Status
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error updating status", error = ex.Message });
            }
        }

        // GET: /api/applications/{id}/download
        [HttpGet("{id}/download")]
        public async Task<IActionResult> DownloadApplication(Guid id)
        {
            try
            {
                var application = await _context.Applications.FindAsync(id);
                if (application == null)
                {
                    return NotFound();
                }

                // Generate PDF - you'll need to implement this with a PDF library
                var pdfContent = GenerateApplicationPdf(application);

                return File(pdfContent, "application/pdf",
                    $"application_{application.ReferenceNumber}_{DateTime.UtcNow:yyyyMMdd}.pdf");
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error downloading application", error = ex.Message });
            }
        }

        private byte[] GenerateApplicationPdf(Application application)
        {
            // Implement with iTextSharp, QuestPDF, or another PDF library
            var pdfContent = System.Text.Encoding.UTF8.GetBytes(
                $"Application Details for {application.FirstName} {application.LastName}\n" +
                $"Reference: {application.ReferenceNumber}\n" +
                $"Status: {application.Status}\n" +
                $"Date: {application.CreatedAt:yyyy-MM-dd}");

            return pdfContent;
        }
    }

    public class UpdateStatusRequest
    {
        public string Status { get; set; }
        public string Notes { get; set; }
    }
}




// Request DTO for step one
public class StepOneRequest
{
    [Required(ErrorMessage = "First name is required")]
    [StringLength(100, ErrorMessage = "First name cannot exceed 100 characters")]
    public string FirstName { get; set; }

    [Required(ErrorMessage = "Last name is required")]
    [StringLength(100, ErrorMessage = "Last name cannot exceed 100 characters")]
    public string LastName { get; set; }

    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Please enter a valid email address")]
    public string Email { get; set; }

    [Required(ErrorMessage = "Password is required")]
    [MinLength(8, ErrorMessage = "Password must be at least 8 characters")]
    public string Password { get; set; }

    [Required(ErrorMessage = "Phone number is required")]
    [RegularExpression(@"^\(?([0-9]{3})\)?[-. ]?([0-9]{3})[-. ]?([0-9]{4})$",
        ErrorMessage = "Please enter a valid phone number")]
    public string Phone { get; set; }

    public string Gender { get; set; }

    [Required(ErrorMessage = "Date of birth is required")]
    [DataType(DataType.Date)]
    public DateTime Dob { get; set; }

    public bool MarketingConsent { get; set; }
}

// Updated Request DTO for step two
public class StepTwoRequest
{
    [StringLength(200, ErrorMessage = "Company name cannot exceed 200 characters")]
    public string StartupName { get; set; }

    [Url(ErrorMessage = "Please enter a valid URL")]
    public string? Url { get; set; }

    [StringLength(1000, ErrorMessage = "Description cannot exceed 1000 characters")]
    public string Description { get; set; }

    [StringLength(500, ErrorMessage = "Locations cannot exceed 500 characters")]
    public string Locations { get; set; }

    public bool LegallyRegistered { get; set; }

    [Range(1900, 2100, ErrorMessage = "Please enter a valid year")]
    public int? YearOfIncorporation { get; set; }

    [StringLength(50, ErrorMessage = "Registration number cannot exceed 50 characters")]
    public string? CacRegNumber { get; set; }

    public SocialMediaRequest? SocialMedia { get; set; }
}

// DTO for Step 3
public class StepThreeRequest
{
    [Required(ErrorMessage = "Farmer challenges description is required")]
    [StringLength(2000, ErrorMessage = "Farmer challenges cannot exceed 2000 characters")]
    public string FarmerChallenges { get; set; }

    [Required(ErrorMessage = "Solution description is required")]
    [StringLength(2000, ErrorMessage = "Solution description cannot exceed 2000 characters")]
    public string SolutionDescription { get; set; }

    [Required(ErrorMessage = "Product stage is required")]
    public string ProductStage { get; set; }

    [Url(ErrorMessage = "Please enter a valid URL")]
    public string ProductLink { get; set; }

    [Required(ErrorMessage = "Innovation highlight is required")]
    [StringLength(1500, ErrorMessage = "Innovation highlight cannot exceed 1500 characters")]
    public string InnovationHighlight { get; set; }

    [Required(ErrorMessage = "Primary users description is required")]
    [StringLength(1000, ErrorMessage = "Primary users description cannot exceed 1000 characters")]
    public string PrimaryUsers { get; set; }

    [Required(ErrorMessage = "Number of active users is required")]
    [Range(0, int.MaxValue, ErrorMessage = "Number of active users must be a positive number")]
    public string NoOfActiveUsers { get; set; }
}

// DTO for Step 4
public class StepFourRequest
{
    [Required(ErrorMessage = "Business model description is required")]
    [StringLength(1500, ErrorMessage = "Business model cannot exceed 1500 characters")]
    public string BusinessModel { get; set; }

    public bool IsRevenueGeneration { get; set; }

    [Required(ErrorMessage = "Go-to-market strategy is required")]
    [StringLength(1500, ErrorMessage = "Go-to-market strategy cannot exceed 1500 characters")]
    public string GoToMarketStrategy { get; set; }

    [Required(ErrorMessage = "Number of customers is required")]
    [Range(0, int.MaxValue, ErrorMessage = "Number of customers must be a positive number")]
    public string NoOfCustomers { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "Average CAC must be a positive number")]
    public string AverageCac { get; set; }

    [StringLength(1500, ErrorMessage = "Competitors description cannot exceed 1500 characters")]
    public string Competitors { get; set; }
}

// DTO for Step 5
public class StepFiveRequest
{
    [Required(ErrorMessage = "Farmers served (previous year) is required")]
    [Range(0, 10000000, ErrorMessage = "Farmers served must be between 0 and 10,000,000")]
    public string FarmersServedPreviousYear { get; set; }

    [Required(ErrorMessage = "Total farmers served is required")]
    [Range(0, 10000000, ErrorMessage = "Total farmers served must be between 0 and 10,000,000")]
    public string FarmersServedTotal { get; set; }

    [Required(ErrorMessage = "Impact on farmers description is required")]
    [StringLength(1500, ErrorMessage = "Impact on farmers cannot exceed 1500 characters")]
    public string ImpactOnFarmers { get; set; }

    [Required(ErrorMessage = "Sustainability promotion description is required")]
    [StringLength(1500, ErrorMessage = "Sustainability promotion cannot exceed 1500 characters")]
    public string SustainabilityPromotion { get; set; }

    [Required(ErrorMessage = "Impact evidence is required")]
    [StringLength(1000, ErrorMessage = "Impact evidence cannot exceed 1000 characters")]
    public string ImpactEvidence { get; set; }
}

// DTO for Step 6
public class StepSixRequest
{
    [Required(ErrorMessage = "Gender inclusion description is required")]
    [StringLength(1500, ErrorMessage = "Gender inclusion cannot exceed 1500 characters")]
    public string GenderInclusion { get; set; }

    [Required(ErrorMessage = "Number of jobs created is required")]
    [Range(0, 10000, ErrorMessage = "Jobs created must be between 0 and 10,000")]
    public string JobsCreated { get; set; }

    [Required(ErrorMessage = "Environmental sustainability description is required")]
    [StringLength(1500, ErrorMessage = "Environmental sustainability cannot exceed 1500 characters")]
    public string EnvironmentalSustainability { get; set; }

    [Required(ErrorMessage = "Data protection measures description is required")]
    [StringLength(1500, ErrorMessage = "Data protection measures cannot exceed 1500 characters")]
    public string DataProtectionMeasures { get; set; }
}

// DTO for Step 7
public class StepSevenRequest
{
    [Required(ErrorMessage = "Number of founders is required")]
    [Range(1, 10, ErrorMessage = "Number of founders must be between 1 and 10")]
    public string NoOfFounders { get; set; }

    [Required(ErrorMessage = "Number of employees is required")]
    [Range(1, 1000, ErrorMessage = "Number of employees must be between 1 and 1000")]
    public string NoOfEmployees { get; set; }

    [Required(ErrorMessage = "Founders details are required")]
    [StringLength(2000, ErrorMessage = "Founders details cannot exceed 2000 characters")]
    public string FoundersDetails { get; set; }

    [Required(ErrorMessage = "Team skills description is required")]
    [StringLength(1500, ErrorMessage = "Team skills cannot exceed 1500 characters")]
    public string TeamSkill { get; set; }
}

// DTO for Step 8
public class StepEightRequest
{
    [Required(ErrorMessage = "Milestones description is required")]
    [StringLength(1500, ErrorMessage = "Milestones cannot exceed 1500 characters")]
    public string Milestone { get; set; }

    [Required(ErrorMessage = "Risk assessment is required")]
    [StringLength(1500, ErrorMessage = "Risk assessment cannot exceed 1500 characters")]
    public string BiggestRiskFacing { get; set; }

    [Required(ErrorMessage = "Revenue projection is required")]
    [StringLength(1500, ErrorMessage = "Revenue projection cannot exceed 1500 characters")]
    public string TwelveMonthRevenueProjection { get; set; }

    [Required(ErrorMessage = "Long-term vision is required")]
    [StringLength(1500, ErrorMessage = "Long-term vision cannot exceed 1500 characters")]
    public string LongTermVision { get; set; }
}

// DTO for Step 9
public class LocalUploadResult
{
    public bool Success { get; set; }
    public string Url { get; set; }
    public string FileName { get; set; }
    public string ErrorMessage { get; set; }
}
public class StepNineRequest
{
    public string PitchDeckUrl { get; set; }
    public string CacUrl { get; set; }
    public string TinUrl { get; set; }
    public string? OthersUrl { get; set; }

    [Required(ErrorMessage = "You must agree to Ayute Terms of Service")]
    public bool AgreeToToS_Ayute { get; set; }

    [Required(ErrorMessage = "You must agree to Heifer International Terms")]
    public bool AgreeToToS_Heifer { get; set; }
}

// Custom validation attribute for boolean must be true
public class MustBeTrueAttribute : ValidationAttribute
{
    public override bool IsValid(object value)
    {
        return value is bool && (bool)value;
    }
}
// Separate DTO for SocialMedia owned entity
public class SocialMediaRequest
{
    [Url(ErrorMessage = "Please enter a valid LinkedIn URL")]
    public string? LinkedIn { get; set; }

    [Url(ErrorMessage = "Please enter a valid Twitter/X URL")]
    public string? X { get; set; }

    [Url(ErrorMessage = "Please enter a valid Facebook URL")]
    public string? Facebook { get; set; }

    [Url(ErrorMessage = "Please enter a valid Instagram URL")]
    public string? Instagram { get; set; }
}

public class ResendVerificationRequest
{
    public string Email { get; set; }
}