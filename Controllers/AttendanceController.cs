

using ayuteng.Data;
using Microsoft.AspNetCore.Mvc;

namespace ayuteng.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AttendanceController : Controller
    {
        private readonly ILogger<ApplicationController> _logger;
        private readonly ApplicationDbContext _context;
        public AttendanceController(ILogger<ApplicationController> logger, ApplicationDbContext context)
        {
            _context = context;
            _logger = logger;
        }


    }
}