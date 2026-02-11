

namespace ayuteng.Models
{
    public class SiteVisitor
    {
        public int Id { get; set; }
        public string? VisitorId { get; set; }
        public string? Path { get; set; }
        public string? IPAddress { get; set; }
        public string? UtmSource { get; set; }
        public string? UserAgent { get; set; }
        public DateTime? VisitedAt { get; set; }
        
    }   
}
