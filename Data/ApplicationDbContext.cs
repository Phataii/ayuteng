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
    public DbSet<EmailVerificationToken> EmailVerificationTokens { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // 🔴 REQUIRED for Identity tables (THIS FIXES YOUR ERROR)
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<EmailVerificationToken>()
            .HasIndex(t => t.Token)
            .IsUnique();

        modelBuilder.Entity<EmailVerificationToken>()
            .HasIndex(t => t.UserId);

        modelBuilder.Entity<EmailVerificationToken>()
            .Property(t => t.Token)
            .IsRequired()
            .HasMaxLength(100);
    }
}
