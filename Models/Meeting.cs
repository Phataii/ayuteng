// Meeting.cs
using System;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;

namespace ayuteng.Models
{
    public class Meeting
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        [StringLength(200)]
        public string Name { get; set; } = default!;

        public string? Description { get; set; }

        [Required]
        [StringLength(20)]
        public string MeetingCode { get; set; } = default!; // Unique code for QR generation

        [Required]
        [StringLength(20)]
        public string Type { get; set; } = default!; // "physical", "virtual", "hybrid"

        [Required]
        public string Venue { get; set; } = default!; // Physical address or meeting link

        public int MaxAttendees { get; set; }

        [Required]
        public DateTime StartTime { get; set; }

        [Required]
        public DateTime EndTime { get; set; }

        [StringLength(20)]
        public string Status { get; set; } = "upcoming"; // "upcoming", "ongoing", "completed", "cancelled"

        // Navigation properties
        public List<MeetingAttendee> Attendees { get; set; } = new();

        // Auditing
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public string? CreatedBy { get; set; } // Admin who created the meeting
    }

    public class MeetingAttendee
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        public Guid MeetingId { get; set; }
        public Meeting Meeting { get; set; } = default!;

        [Required]
        public string Name { get; set; } = default!;

        [EmailAddress]
        public string? Email { get; set; }

        public string? Location { get; set; }

        [StringLength(20)]
        public string Status { get; set; } = "registered"; // "registered", "checked-in", "cancelled"

        public DateTime CheckInTime { get; set; }
        public DateTime? CheckOutTime { get; set; }

        // QR Code scanning info
        public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
        public string? RegistrationMethod { get; set; }
        public string? Gender { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}