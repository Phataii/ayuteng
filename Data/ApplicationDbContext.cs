using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ayuteng.Models;

namespace ayuteng.Data;

public class ApplicationDbContext : IdentityDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }
    public DbSet<Application> Applications { get; set; }
    public DbSet<Admin> Admins { get; set; }
    public DbSet<Attendance> Attendances { get; set; }
}
