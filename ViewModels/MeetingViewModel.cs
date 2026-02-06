using System.ComponentModel.DataAnnotations;

public class MeetingViewModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
    public string MeetingCode { get; set; } = default!;
    public string Type { get; set; } = default!;
    public string Venue { get; set; } = default!;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int MaxAttendees { get; set; }
    public int AttendeeCount { get; set; }
    public string Status { get; set; } = default!;
}


public class RegistrationViewModel
{
    [Required]
    public string Name { get; set; } = default!;

    [EmailAddress]
    public string? Email { get; set; }

    [Phone]
    public string? Phone { get; set; }

    public string? Location { get; set; }
}