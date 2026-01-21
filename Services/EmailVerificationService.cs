using ayuteng.Data;
using ayuteng.Models;
using Microsoft.EntityFrameworkCore;

public interface IEmailVerificationService
{
    Task<string> GenerateAndSaveTokenAsync(string userId);
    Task<bool> ValidateTokenAsync(string token);
    Task<bool> UseTokenAsync(string token);
    Task<EmailVerificationToken> GetTokenAsync(string token);
}

public class EmailVerificationService : IEmailVerificationService
{
    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _configuration;

    public EmailVerificationService(ApplicationDbContext context, IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
    }

    public async Task<string> GenerateAndSaveTokenAsync(string userId)
    {
        // Generate a secure token
        var token = GenerateSecureToken();

        // Create token entity
        var verificationToken = new EmailVerificationToken
        {
            UserId = userId,
            Token = token,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(24), // 24-hour expiration
            IsUsed = false
        };

        // Save to database
        await SaveVerificationTokenAsync(verificationToken);

        return token;
    }

    public async Task<bool> ValidateTokenAsync(string token)
    {
        var verificationToken = await GetTokenAsync(token);

        if (verificationToken == null)
            return false;

        if (verificationToken.IsUsed)
            return false;

        if (verificationToken.ExpiresAt < DateTime.UtcNow)
            return false;

        return true;
    }

    public async Task<bool> UseTokenAsync(string token)
    {
        var verificationToken = await GetTokenAsync(token);

        if (verificationToken == null || verificationToken.IsUsed)
            return false;

        verificationToken.IsUsed = true;
        verificationToken.UsedAt = DateTime.UtcNow;

        _context.EmailVerificationTokens.Update(verificationToken);
        await _context.SaveChangesAsync();

        return true;
    }

    public async Task<EmailVerificationToken> GetTokenAsync(string token)
    {
        return await _context.EmailVerificationTokens
            .FirstOrDefaultAsync(t => t.Token == token);
    }

    // Private method to save token
    private async Task SaveVerificationTokenAsync(EmailVerificationToken token)
    {
        // Remove any existing tokens for this user (optional)
        var existingTokens = _context.EmailVerificationTokens
            .Where(t => t.UserId == token.UserId && !t.IsUsed);
        _context.EmailVerificationTokens.RemoveRange(existingTokens);

        // Add new token
        _context.EmailVerificationTokens.Add(token);
        await _context.SaveChangesAsync();
    }

    private string GenerateSecureToken()
    {
        // Method 1: Using GUID (simpler)
        return Convert.ToBase64String(Guid.NewGuid().ToByteArray())
            .Replace("=", "")
            .Replace("+", "")
            .Replace("/", "");

        // Method 2: Using cryptographic random bytes (more secure)
        // byte[] tokenBytes = new byte[32];
        // using (var rng = RandomNumberGenerator.Create())
        // {
        //     rng.GetBytes(tokenBytes);
        // }
        // return Convert.ToBase64String(tokenBytes)
        //     .Replace("=", "")
        //     .Replace("+", "")
        //     .Replace("/", "");
    }
}