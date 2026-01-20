using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ayuteng.Services;

public class BrevoEmailService : IBrevoEmailService
{
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;

    public BrevoEmailService(IConfiguration configuration, HttpClient httpClient)
    {
        _configuration = configuration;
        _httpClient = httpClient;
    }

    public async Task SendEmailAsync(string toEmail, string toName, string subject, string htmlContent)
    {
        Console.WriteLine(">>>>>>>>>>>>>>>>>>" + htmlContent);
        if (string.IsNullOrWhiteSpace(toEmail))
            throw new ArgumentException("Recipient email cannot be null or empty");

        var apiKey = _configuration["Brevo:ApiKey"];
        var senderEmail = _configuration["Brevo:SenderEmail"];
        var senderName = _configuration["Brevo:SenderName"];

        var payload = new
        {
            sender = new { name = senderName, email = senderEmail },
            to = new[] { new { email = toEmail, name = toName } },
            subject,
            htmlContent
        };

        var json = JsonSerializer.Serialize(payload);
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        request.Headers.Add("api-key", apiKey);

        var response = await _httpClient.SendAsync(request);
        Console.WriteLine(">>>>>>>>>>>>>>>>>>" + response);
        response.EnsureSuccessStatusCode();
    }
}
