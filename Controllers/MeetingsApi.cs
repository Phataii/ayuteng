// MeetingsApiController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using ayuteng.Data;
using ayuteng.Models;
using System.Text;

namespace ayuteng.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class MeetingsApiController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<MeetingsApiController> _logger;

        public MeetingsApiController(ApplicationDbContext context, ILogger<MeetingsApiController> logger)
        {
            _context = context;
            _logger = logger;
        }

        private static string DetermineMeetingStatus(
            DateTime startTimeUtc,
            DateTime endTimeUtc,
            bool isCancelled = false)
        {
            if (isCancelled)
                return "cancelled";

            var now = DateTime.UtcNow;

            if (now < startTimeUtc)
                return "upcoming";

            if (now >= startTimeUtc && now <= endTimeUtc)
                return "ongoing";

            return "completed";
        }


        // POST: /api/meetings
        [HttpPost("meetings")]
        public async Task<IActionResult> CreateMeeting([FromBody] CreateMeetingRequest request)
        {
            try
            {
                // Generate unique meeting code
                var meetingCode = GenerateMeetingCode();
                var startTimeUtc = request.StartTime.ToUniversalTime();
                var endTimeUtc = request.EndTime.ToUniversalTime();
                var meeting = new Meeting
                {
                    Id = Guid.NewGuid(),
                    Name = request.Name,
                    Description = request.Description,
                    MeetingCode = meetingCode,
                    Type = request.Type,
                    Venue = request.Venue,
                    MaxAttendees = request.MaxAttendees,
                    StartTime = request.StartTime.ToUniversalTime(),
                    EndTime = request.EndTime.ToUniversalTime(),
                    Status = DetermineMeetingStatus(startTimeUtc, endTimeUtc),
                    CreatedBy = User.Identity?.Name,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.Meetings.Add(meeting);
                await _context.SaveChangesAsync();

                // If send notifications is enabled, send emails (implement separately)
                if (request.SendNotifications)
                {
                    // await SendMeetingNotifications(meeting);
                }

                return Ok(new
                {
                    success = true,
                    message = "Meeting created successfully",
                    meetingId = meeting.Id,
                    meetingCode = meeting.MeetingCode
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating meeting");
                return StatusCode(500, new { success = false, message = "Error creating meeting" });
            }
        }

        // GET: /api/meetingsapi/{id}/attendees
        [HttpGet("{id}/attendees")]
        public async Task<IActionResult> GetMeetingAttendees(Guid id)
        {
            try
            {
                var attendees = await _context.MeetingAttendees
                    .Where(a => a.MeetingId == id && a.Status != "cancelled")
                    .OrderByDescending(a => a.CheckInTime)
                    .Select(a => new
                    {
                        id = a.Id,
                        name = a.Name,
                        email = a.Email,
                        location = a.Location,
                        status = a.Status,
                        checkInTime = a.CheckInTime,
                        gender = a.Gender
                    })
                    .ToListAsync();

                return Ok(new { attendees });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting meeting attendees");
                return StatusCode(500, new { message = "Error getting attendees" });
            }
        }

        // PUT: /api/meetings/{id}/checkin/{attendeeId}
        [HttpPut("{id}/checkin/{attendeeId}")]
        public async Task<IActionResult> CheckInAttendee(Guid id, Guid attendeeId)
        {
            try
            {
                var attendee = await _context.MeetingAttendees
                    .FirstOrDefaultAsync(a => a.Id == attendeeId && a.MeetingId == id);

                if (attendee == null)
                {
                    return NotFound(new { message = "Attendee not found" });
                }

                attendee.Status = "checked-in";
                attendee.CheckInTime = DateTime.UtcNow;
                // attendee.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                return Ok(new { message = "Attendee checked in successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking in attendee");
                return StatusCode(500, new { message = "Error checking in attendee" });
            }
        }

        [HttpPut("{id}/conclude-meeting")]
        public async Task<IActionResult> ConcludeMeeting(Guid id)
        {
            try
            {
                var meeting = await _context.Meetings.FindAsync(id);
                if (meeting == null)
                {
                    return NotFound(new { message = "Meeting not found" });
                }


                meeting.Status = "completed";

                await _context.SaveChangesAsync();

                return Ok(new { message = "Event Concluded successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error concluding event");
                return StatusCode(500, new { message = "Error concluding event" });
            }
        }

        // DELETE: /api/meetingsApi/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteMeeting(Guid id)
        {
            try
            {
                var meeting = await _context.Meetings.FindAsync(id);
                if (meeting == null)
                {
                    return NotFound(new { message = "Meeting not found" });
                }

                // Also delete all attendees
                var attendees = await _context.MeetingAttendees
                    .Where(a => a.MeetingId == id)
                    .ToListAsync();

                _context.MeetingAttendees.RemoveRange(attendees);
                _context.Meetings.Remove(meeting);
                await _context.SaveChangesAsync();

                return Ok(new { message = "Meeting deleted successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting meeting");
                return StatusCode(500, new { message = "Error deleting meeting" });
            }
        }

        private string GenerateMeetingCode()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var random = new Random();

            // Check for uniqueness
            string code;
            do
            {
                code = "MTG-" + new string(Enumerable.Repeat(chars, 6)
                    .Select(s => s[random.Next(s.Length)]).ToArray());
            } while (_context.Meetings.Any(m => m.MeetingCode == code));

            return code;
        }

        [HttpGet("meetings/export/{meetingId}")]
        public async Task<IActionResult> ExportAttendees(Guid meetingId)
        {
            var attendees = await _context.MeetingAttendees
                .Where(a => a.MeetingId == meetingId)
                .ToListAsync();

            var builder = new StringBuilder();

            // CSV Header
            builder.AppendLine("Name,Email,Location,Status,CheckInTime,Gender");

            foreach (var a in attendees)
            {
                builder.AppendLine(string.Join(",",
                    Escape(a.Name),
                    Escape(a.Email),
                    Escape(a.Location),
                    Escape(a.Status),
                    Escape(a.CheckInTime.ToString("yyyy-MM-dd HH:mm:ss")),
                    Escape(a.Gender)
                ));
            }

            var bytes = Encoding.UTF8.GetBytes(builder.ToString());

            return File(bytes, "text/csv", $"MeetingAttendees_{meetingId}.csv");
        }

        private string Escape(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            if (value.Contains(",") || value.Contains("\""))
                value = $"\"{value.Replace("\"", "\"\"")}\"";

            return value;
        }


    }

    public class CreateMeetingRequest
    {
        public string Name { get; set; } = default!;
        public string? Description { get; set; }
        public string Type { get; set; } = default!;
        public string Venue { get; set; } = default!;
        public int MaxAttendees { get; set; } = 50;
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public bool SendNotifications { get; set; }
    }
}