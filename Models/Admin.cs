using System.ComponentModel.DataAnnotations;

namespace ayuteng.Models
{
    public class Admin
    {
        [Key]
        public Guid Id { get; set; }
        public string? Email { get; set; }
        public string? Password { get; set; }
        public bool IsActive { get; set; }
        public int Role { get; set; } // 1 - Super, 2 - Portal
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}