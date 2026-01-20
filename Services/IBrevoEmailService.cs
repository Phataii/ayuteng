namespace ayuteng.Services
{
    public interface IBrevoEmailService
    {
        Task SendEmailAsync(
            string toEmail,
            string toName,
            string subject,
            string htmlContent
        );
    }
}
