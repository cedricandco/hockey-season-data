namespace HockeySeason.Infrastructure.Data;

// =====================================================================
// Entities of the PostgreSQL persistence model. C# names stay PascalCase;
// EF Core's snake_case naming convention (UseSnakeCaseNamingConvention)
// maps them to the database columns documented in docs/database-schema.sql.
// =====================================================================

/// <summary>An association, e.g. Hockey Richelieu. Optionally mirrored from Spordle.</summary>
public sealed class Organization
{
    public int Id { get; set; }
    public required string Name { get; set; }
    /// <summary>Spordle identifier, present only when the organization has a mirror.</summary>
    public string? ExternalId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public List<Division> Divisions { get; set; } = [];
}

/// <summary>A Spordle office (division), e.g. 6193 "Hockey Richelieu Simple Lettre".</summary>
public sealed class Division
{
    public int Id { get; set; }
    /// <summary>FK to the organization that owns this office.</summary>
    public int OrganizationId { get; set; }
    public required string Name { get; set; }
    /// <summary>Spordle office id (6193, 6194), unique.</summary>
    public required string ExternalId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Organization Organization { get; set; } = null!;
    public List<League> Leagues { get; set; } = [];
}

/// <summary>A Spordle season, e.g. "2026-27".</summary>
public sealed class Season
{
    public int Id { get; set; }
    /// <summary>Display name, e.g. "2026-27".</summary>
    public required string Name { get; set; }
    /// <summary>Spordle season id ("2026-27"), unique.</summary>
    public required string ExternalId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public List<League> Leagues { get; set; } = [];
}

/// <summary>A Spordle league (schedule); league ids are season-specific.</summary>
public sealed class League
{
    public int Id { get; set; }
    public int DivisionId { get; set; }
    public int SeasonId { get; set; }
    public required string Name { get; set; }
    /// <summary>Spordle league id (e.g. 194908), unique.</summary>
    public required string ExternalId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Division Division { get; set; } = null!;
    public Season Season { get; set; } = null!;
    public List<Team> Teams { get; set; } = [];
}

/// <summary>A team published by a Spordle league.</summary>
public sealed class Team
{
    public int Id { get; set; }
    public int LeagueId { get; set; }
    public required string Name { get; set; }
    /// <summary>Spordle team id, unique.</summary>
    public required string ExternalId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public League League { get; set; } = null!;
    public List<TeamSubscription> Subscriptions { get; set; } = [];
}

/// <summary>An Auth0 user of the application, plus future Stripe billing ids.</summary>
public sealed class User
{
    public int Id { get; set; }
    /// <summary>Stable identity from the JWT `sub` claim, e.g. "auth0|6af39e...".</summary>
    public required string Auth0UserId { get; set; }
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    /// <summary>Stripe customer id, filled later by the billing webhook.</summary>
    public string? StripeCustomerId { get; set; }
    public bool IsAdmin { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public List<UserRole> Roles { get; set; } = [];
    public UserSetting? Setting { get; set; }
    public List<TeamSubscription> TeamSubscriptions { get; set; } = [];
    public List<LeagueSubscription> LeagueSubscriptions { get; set; } = [];
    public List<OrganizationSubscription> OrganizationSubscriptions { get; set; } = [];
}

/// <summary>A software licence role: COACH (5 teams), DIRECTOR (5 leagues), PRESIDENT (whole association).</summary>
public sealed class Role
{
    public short Id { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    /// <summary>Max individually-followed entities, e.g. 5 teams (coach) or 5 leagues (director).</summary>
    public int MaxFollowedEntities { get; set; }

    public List<UserRole> Users { get; set; } = [];
}

/// <summary>One granted licence (user × role), unique per pair.</summary>
public sealed class UserRole
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public short RoleId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public User User { get; set; } = null!;
    public Role Role { get; set; } = null!;
}

/// <summary>Per-user visual preferences (colors + generic extras), one row per user.</summary>
public sealed class UserSetting
{
    public int Id { get; set; }
    /// <summary>1:1 with User; the FK doubles as the unique constraint.</summary>
    public int UserId { get; set; }
    public string? PrimaryColor { get; set; }
    public string? SecondaryColor { get; set; }
    /// <summary>Generic extension dictionary for future settings.</summary>
    public Dictionary<string, string?> Extra { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public User User { get; set; } = null!;
}

/// <summary>
/// User follows one team. Locked 15 days after creation (deletion blocked)
/// unless an admin unlocks; <see cref="UnlockRequestedAt"/> is the audit trace
/// for the admin interface.
/// </summary>
public sealed class TeamSubscription
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int TeamId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    /// <summary>created_at + 15 days, computed by the application, not the database.</summary>
    public DateTimeOffset LockedUntil { get; set; }
    /// <summary>User asked an admin for an unlock; audit trace for the admin interface.</summary>
    public DateTimeOffset? UnlockRequestedAt { get; set; }

    public User User { get; set; } = null!;
    public Team Team { get; set; } = null!;
}

/// <summary>User follows one league (DIRECTOR licence, 5 max).</summary>
public sealed class LeagueSubscription
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int LeagueId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public User User { get; set; } = null!;
    public League League { get; set; } = null!;
}

/// <summary>
/// (Decision A2) User follows an organization: visible scope = every league of
/// the association (PRESIDENT licence). Chosen over duplicating one
/// league_subscription per league, which is fragile when leagues are added.
/// </summary>
public sealed class OrganizationSubscription
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int OrganizationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public User User { get; set; } = null!;
    public Organization Organization { get; set; } = null!;
}
