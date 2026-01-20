using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace ayuteng.Models
{
    public class Application
    {
        [Key]
        public Guid Id { get; set; }

        public int ApplicationStep { get; set; } = 1;
        public string? ReferenceNumber { get; set; }
        public string Status { get; set; }

        // ======================
        // SECTION A – Founder Info
        // ======================
        [Required, MaxLength(100)]
        public string FirstName { get; set; } = default!;

        [Required, MaxLength(100)]
        public string LastName { get; set; } = default!;

        [Required, EmailAddress]
        public string Email { get; set; } = default!;

        [Required]
        public string Password { get; set; } = default!;

        [Required]
        public string Phone { get; set; } = default!;

        public string? Gender { get; set; }

        [Required]
        public DateTime Dob { get; set; } = default!;

        public bool IsVerified { get; set; } = false;

        // ======================
        // SECTION B – Startup Info
        // ======================
        public string? StartupName { get; set; }
        public string? Url { get; set; }

        public SocialMedia SocialMedia { get; set; } = new();

        public string? Description { get; set; }
        public string? Locations { get; set; }
        public bool LegallyRegistered { get; set; } = false;
        public int? YearOfIncorporation { get; set; }
        public string? CacRegNumber { get; set; }

        // ======================
        // SECTION C – Problem & Solution
        // ======================
        public string? FarmerChallenges { get; set; }
        public string? SolutionDescription { get; set; }
        public string? ProductStage { get; set; }
        public string? ProductLink { get; set; }
        public string? InnovationHighlight { get; set; }
        public string? PrimaryUsers { get; set; }
        public int NoOfActiveUsers { get; set; } = 0;

        // ======================
        // SECTION D – Business Model
        // ======================
        public string? BusinessModel { get; set; }
        public bool IsRevenueGeneration { get; set; } = false;
        public string? GoToMarketStrategy { get; set; }
        public int NoOfCustomers { get; set; } = 0;
        public decimal? AverageCac { get; set; }
        public string? Competitors { get; set; }

        // ======================
        // SECTION E – Impact
        // ======================
        public int FarmersServedPreviousYear { get; set; } = 0;
        public int FarmersServedTotal { get; set; } = 0;
        public string? ImpactOnFarmers { get; set; }
        public string? SustainabilityPromotion { get; set; }

        [MaxLength(1000)]
        public string? ImpactEvidence { get; set; }

        // ======================
        // SECTION F – Inclusion & Sustainability
        // ======================
        public string? GenderInclusion { get; set; }
        public int JobsCreated { get; set; } = 0;
        public string? EnvironmentalSustainability { get; set; }
        public string? DataProtectionMeasures { get; set; }

        // ======================
        // SECTION G – Team
        // ======================
        public int? NoOfFounders { get; set; }
        public string? FoundersDetails { get; set; }
        public int? NoOfEmployees { get; set; }
        public string? TeamSkill { get; set; }

        // ======================
        // SECTION H – Growth & Vision
        // ======================
        public string? Milestone { get; set; }
        public string? BiggestRiskFacing { get; set; }
        public string? TwelveMonthRevenueProjection { get; set; }
        public string? LongTermVision { get; set; }

        // ======================
        // SECTION I – Documents & Agreements
        // ======================
        public string? PitchDeckUrl { get; set; }
        public string? CacUrl { get; set; }
        public string? TinUrl { get; set; }
        public string? OthersUrl { get; set; }

        public bool AgreeToToS_Ayute { get; set; }
        public bool AgreeToToS_Heifer { get; set; }

        // ======================
        // Auditing
        // ======================
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    // ======================
    // Embedded Object
    // ======================
    [Owned]
    public class SocialMedia
    {
        public string? LinkedIn { get; set; }
        public string? X { get; set; }
        public string? Instagram { get; set; }
        public string? Facebook { get; set; }
    }
}
