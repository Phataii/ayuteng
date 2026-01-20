

namespace ayuteng.Models
{
    public class Attendance
    {
        public int Id { get; set; }
        public string? Email { get; set; }
        public string? Event { get; set; }
        public bool Attended { get; set; }
        public string? Location { get; set; }
        public string? Longitude { get; set; }
        public string? Latitude { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

    }
}