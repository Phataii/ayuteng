// MeetingsController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using ayuteng.Models;
using ayuteng.Data;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace ayuteng.Controllers
{
    public class MeetingsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public MeetingsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: /Meetings
        public async Task<IActionResult> Index()
        {
            var meetings = await _context.Meetings
                .OrderByDescending(m => m.StartTime)
                .Select(m => new MeetingViewModel
                {
                    Id = m.Id,
                    Name = m.Name,
                    MeetingCode = m.MeetingCode,
                    Type = m.Type,
                    Venue = m.Venue,
                    StartTime = m.StartTime,
                    EndTime = m.EndTime,
                    MaxAttendees = m.MaxAttendees,
                    AttendeeCount = m.Attendees.Count(a => a.Status != "cancelled"),
                    Status = GetMeetingStatus(m.StartTime, m.EndTime, m.Status)
                })
                .ToListAsync();

            return View(meetings);
        }

        private string GetMeetingStatus(DateTime startTime, DateTime endTime, string currentStatus)
        {
            var now = DateTime.UtcNow;

            if (currentStatus == "cancelled")
                return "cancelled";

            if (now < startTime)
                return "upcoming";
            else if (now >= startTime && now <= endTime)
                return "ongoing";
            else
                return "completed";
        }

        // GET: /Meetings/Details/{id}
        public async Task<IActionResult> Details(Guid id)
        {
            var meeting = await _context.Meetings
                .Include(m => m.Attendees)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (meeting == null)
            {
                return NotFound();
            }

            return View(meeting);
        }



        // POST: /Meeting/Register/{code} - Handle registration
        [HttpPost]
        [Route("Meeting/Register/{code}")]
        public async Task<IActionResult> Register(string code, [FromForm] RegistrationViewModel model)
        {
            var meeting = await _context.Meetings
                .FirstOrDefaultAsync(m => m.MeetingCode == code);

            if (meeting == null)
            {
                return NotFound();
            }

            // Check if meeting is full
            // var currentAttendees = await _context.MeetingAttendees
            //     .CountAsync(a => a.MeetingId == meeting.Id && a.Status != "cancelled");

            // if (currentAttendees >= meeting.MaxAttendees)
            // {
            //     ModelState.AddModelError("", "This meeting is full. Registration closed.");
            //     return View(meeting);
            // }
            var hasRegistered = await _context.MeetingAttendees
               .FirstOrDefaultAsync(m => m.Email == model.Email);

            if (hasRegistered != null)
            {
                ModelState.AddModelError("", "You have already registered for this meeting.");
                return Redirect($"/meeting/register/{code}");
            }
            var attendee = new MeetingAttendee
            {
                Id = Guid.NewGuid(),
                MeetingId = meeting.Id,
                Name = model.Name,
                Email = model.Email,
                Location = model.Location,
                Status = "checked-in",
                RegistrationMethod = "qr",
                RegisteredAt = DateTime.UtcNow,
                CheckInTime = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };

            _context.MeetingAttendees.Add(attendee);
            await _context.SaveChangesAsync();

            // Send confirmation email (implement as needed)
            // await SendConfirmationEmail(attendee, meeting);

            return Redirect($"/success?code={code}");
        }

        

    }

}