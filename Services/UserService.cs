using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore; // If using Identity
using System.Threading.Tasks;
using ayuteng.Data;
using ayuteng.Models;
using ayuteng.Services;




public interface IUserService
{
    Task<Application> GetUserByIdAsync(string userId);
    Task<bool> VerifyUserEmailAsync(string userId, string token);
    Task<bool> ResetPasswordAsync(string userId, string token, string password);
    Task<bool> IsEmailVerifiedAsync(string userId);
}

public class UserService : IUserService
{
    private readonly ApplicationDbContext _context;
    private readonly IEmailVerificationService _emailVerificationService;
    private readonly IBrevoEmailService _emailService;
    private readonly ILogger<UserService> _logger;

    public UserService(
        ApplicationDbContext context,
        IEmailVerificationService emailVerificationService,
        IBrevoEmailService emailService,
        ILogger<UserService> logger)
    {
        _context = context;
        _emailVerificationService = emailVerificationService;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<Application> GetUserByIdAsync(string userId)
    {
        return await _context.Applications
            .FirstOrDefaultAsync(u => u.Id.ToString() == userId);
    }

    public async Task<bool> VerifyUserEmailAsync(string userId, string token)
    {
        try
        {
            // Find the user
            var user = await GetUserByIdAsync(userId);
            if (user == null)
            {
                _logger.LogWarning($"User not found for email verification: {userId}");
                return false;
            }

            // Check if already verified
            if (user.IsVerified)
            {
                _logger.LogInformation($"User {userId} email already verified");
                return true;
            }

            // Find verification token
            var verificationToken = await _context.EmailVerificationTokens
                .FirstOrDefaultAsync(t => t.Token == token && t.UserId == userId);

            if (verificationToken == null)
            {
                _logger.LogWarning($"Invalid or expired verification token for user: {userId}");
                return false;
            }

            // Mark user as verified
            user.IsVerified = true;
            user.UpdatedAt = DateTime.UtcNow;

            // Remove used token
            _context.EmailVerificationTokens.Remove(verificationToken);

            // Save all changes in one transaction
            var result = await _context.SaveChangesAsync();

            if (result > 0)
            {
                _logger.LogInformation($"Successfully verified email and deleted token for user: {userId}");

                // Optional: send confirmation email
                // await SendEmailVerifiedConfirmationAsync(user);

                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error verifying email for user: {userId}");
            return false;
        }
    }

    public async Task<bool> ResetPasswordAsync(string userId, string token, string password)
    {
        try
        {
            // Find the user
            var user = await GetUserByIdAsync(userId);
            if (user == null)
            {
                _logger.LogWarning($"User not found for passwod reset: {userId}");
                return false;
            }



            // Find verification token
            var verificationToken = await _context.EmailVerificationTokens
                .FirstOrDefaultAsync(t => t.Token == token && t.UserId == userId);

            if (verificationToken == null)
            {
                _logger.LogWarning($"Invalid or expired verification token for user: {userId}");
                return false;
            }

            // Mark user as verified
            user.Password = password;
            user.UpdatedAt = DateTime.UtcNow;

            // Remove used token
            _context.EmailVerificationTokens.Remove(verificationToken);

            // Save all changes in one transaction
            var result = await _context.SaveChangesAsync();

            if (result > 0)
            {
                _logger.LogInformation($"Successfully changed password and deleted token for user: {userId}");

                // Optional: send confirmation email
                // await SendEmailVerifiedConfirmationAsync(user);

                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error verifying email for user: {userId}");
            return false;
        }
    }
    public async Task<bool> IsEmailVerifiedAsync(string userId)
    {
        var user = await GetUserByIdAsync(userId);
        return user?.IsVerified ?? false;
    }

    // public async Task<bool> ResendVerificationEmailAsync(string userId)
    // {
    //     try
    //     {
    //         var user = await GetUserByIdAsync(userId);
    //         if (user == null)
    //         {
    //             return false;
    //         }

    //         // Check if already verified
    //         if (user.IsVerified)
    //         {
    //             return true; // Already verified, no need to resend
    //         }

    //         // Generate new verification token
    //         var token = await _emailVerificationService.GenerateAndSaveTokenAsync(userId);

    //         // Update user token info (optional)
    //         user.EmailVerificationToken = token;
    //         _context.Applications.Update(user);
    //         await _context.SaveChangesAsync();

    //         // Send verification email
    //         var verificationUrl = $"https://yourdomain.com/verify-email?token={token}";

    //         var emailBody = $@"<h2>Email Verification</h2>
    //                          <p>Please verify your email address by clicking the button below:</p>
    //                          <div style='margin: 20px 0;'>
    //                            <a href='{verificationUrl}' 
    //                               style='
    //                                 background-color: #007bff;
    //                                 color: white;
    //                                 padding: 12px 24px;
    //                                 text-decoration: none;
    //                                 border-radius: 4px;
    //                                 font-weight: bold;
    //                                 display: inline-block;
    //                               '>
    //                              Verify Email Address
    //                            </a>
    //                          </div>
    //                          <p>If you didn't request this, please ignore this email.</p>
    //                          <p><small>This link will expire in 24 hours.</small></p>";

    //         await _emailService.se(
    //             user.Email,
    //             "Email Verification",
    //             emailBody
    //         );

    //         _logger.LogInformation($"Resent verification email to user: {userId}");
    //         return true;
    //     }
    //     catch (Exception ex)
    //     {
    //         _logger.LogError(ex, $"Error resending verification email for user: {userId}");
    //         return false;
    //     }
    // }
}

internal interface IEmailService
{
}