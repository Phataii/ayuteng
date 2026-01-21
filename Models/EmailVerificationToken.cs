using ayuteng.Data;


namespace ayuteng.Models
{
    public class EmailVerificationToken
    {
        public int Id { get; set; }
        public string UserId { get; set; } // Or int if using integers
        public string Token { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public bool IsUsed { get; set; }
        public DateTime? UsedAt { get; set; }
    }
}