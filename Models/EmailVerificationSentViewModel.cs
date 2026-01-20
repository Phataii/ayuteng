// Models/EmailVerificationSentViewModel.cs
using System.ComponentModel.DataAnnotations;

public class EmailVerificationSentViewModel
{
    [Required]
    [EmailAddress]
    public string Email { get; set; }

    public string ReturnUrl { get; set; }

    public DateTime SentAt { get; set; } = DateTime.UtcNow;

    public string ResendUrl { get; set; }
}