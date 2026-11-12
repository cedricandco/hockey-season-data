using System.Text.Json;
using HockeySeason.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace HockeySeason.Infrastructure.Data;

/// <summary>
/// EF Core persistence model for PostgreSQL. Schema reference:
/// docs/database-schema.sql. Snake_case column/table names come from
/// <c>UseSnakeCaseNamingConvention</c> (EFCore.NamingConventions).
/// </summary>
public sealed class HockeySeasonDbContext(DbContextOptions<HockeySeasonDbContext> options) : DbContext(options)
{
    public const string SeedRoleCoach = "coach";
    public const string SeedRoleDirector = "director";
    public const string SeedRolePresident = "president";

    private static readonly JsonSerializerOptions ExtraJsonOptions = new(JsonSerializerDefaults.Web);

    private static string SerializeExtra(Dictionary<string, string?> extra) =>
        JsonSerializer.Serialize(extra, ExtraJsonOptions);

    private static Dictionary<string, string?> DeserializeExtra(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string?>>(json, ExtraJsonOptions) ?? [];

    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Division> Divisions => Set<Division>();
    public DbSet<Season> Seasons => Set<Season>();
    public DbSet<League> Leagues => Set<League>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<UserSetting> UserSettings => Set<UserSetting>();
    public DbSet<TeamSubscription> TeamSubscriptions => Set<TeamSubscription>();
    public DbSet<LeagueSubscription> LeagueSubscriptions => Set<LeagueSubscription>();
    public DbSet<OrganizationSubscription> OrganizationSubscriptions => Set<OrganizationSubscription>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Organization>(entity =>
        {
            entity.ToTable("organizations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).UseIdentityByDefaultColumn().ValueGeneratedOnAdd();
            entity.Property(x => x.Name).IsRequired();
            // Present only when the organization has a Spordle mirror.
            entity.HasIndex(x => x.ExternalId).IsUnique().HasFilter(null);
            entity.HasMany(x => x.Divisions)
                .WithOne(x => x.Organization)
                .HasForeignKey(x => x.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Division>(entity =>
        {
            entity.ToTable("divisions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).UseIdentityByDefaultColumn().ValueGeneratedOnAdd();
            entity.Property(x => x.Name).IsRequired();
            entity.Property(x => x.ExternalId).IsRequired();
            entity.HasIndex(x => x.ExternalId).IsUnique();
            entity.HasMany(x => x.Leagues)
                .WithOne(x => x.Division)
                .HasForeignKey(x => x.DivisionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Season>(entity =>
        {
            entity.ToTable("seasons");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).UseIdentityByDefaultColumn().ValueGeneratedOnAdd();
            entity.Property(x => x.Name).IsRequired();
            entity.Property(x => x.ExternalId).IsRequired();
            entity.HasIndex(x => x.ExternalId).IsUnique();
            entity.HasMany(x => x.Leagues)
                .WithOne(x => x.Season)
                .HasForeignKey(x => x.SeasonId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<League>(entity =>
        {
            entity.ToTable("leagues");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).UseIdentityByDefaultColumn().ValueGeneratedOnAdd();
            entity.Property(x => x.Name).IsRequired();
            entity.Property(x => x.ExternalId).IsRequired();
            entity.HasIndex(x => x.ExternalId).IsUnique();
            entity.HasIndex(x => x.DivisionId);
            entity.HasIndex(x => x.SeasonId);
            entity.HasMany(x => x.Teams)
                .WithOne(x => x.League)
                .HasForeignKey(x => x.LeagueId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Team>(entity =>
        {
            entity.ToTable("teams");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).UseIdentityByDefaultColumn().ValueGeneratedOnAdd();
            entity.Property(x => x.Name).IsRequired();
            entity.Property(x => x.ExternalId).IsRequired();
            entity.HasIndex(x => x.ExternalId).IsUnique();
            entity.HasIndex(x => x.LeagueId);
            entity.HasMany(x => x.Subscriptions)
                .WithOne(x => x.Team)
                .HasForeignKey(x => x.TeamId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.ToTable("roles");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Code).IsRequired();
            entity.HasIndex(x => x.Code).IsUnique();
            entity.Property(x => x.Name).IsRequired();
            entity.Property(x => x.MaxFollowedEntities).IsRequired().HasDefaultValue(0);
            entity.HasData(
                new Role { Id = 1, Code = SeedRoleCoach, Name = "Coach", MaxFollowedEntities = 5 },
                new Role { Id = 2, Code = SeedRoleDirector, Name = "Directeur", MaxFollowedEntities = 5 },
                new Role { Id = 3, Code = SeedRolePresident, Name = "Président", MaxFollowedEntities = 0 });
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).UseIdentityByDefaultColumn().ValueGeneratedOnAdd();
            // Explicit: the snake_case convention merges "Auth0" into
            // "auth0user_id", while the schema documentation (and Auth0's own
            // vocabulary) uses "auth0_user_id".
            entity.Property(x => x.Auth0UserId).IsRequired().HasColumnName("auth0_user_id");
            entity.HasIndex(x => x.Auth0UserId).IsUnique();
            entity.HasMany(x => x.Roles)
                .WithOne(x => x.User)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Setting)
                .WithOne(x => x.User)
                .HasForeignKey<UserSetting>(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.TeamSubscriptions)
                .WithOne(x => x.User)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.LeagueSubscriptions)
                .WithOne(x => x.User)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.OrganizationSubscriptions)
                .WithOne(x => x.User)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserRole>(entity =>
        {
            entity.ToTable("user_roles");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).UseIdentityByDefaultColumn().ValueGeneratedOnAdd();
            entity.HasIndex(x => new { x.UserId, x.RoleId }).IsUnique();
            entity.HasIndex(x => x.UserId);
            entity.HasOne(x => x.Role)
                .WithMany(x => x.Users)
                .HasForeignKey(x => x.RoleId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<UserSetting>(entity =>
        {
            entity.ToTable("user_settings");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).UseIdentityByDefaultColumn().ValueGeneratedOnAdd();
            entity.Property(x => x.UserId).IsRequired();
            entity.HasIndex(x => x.UserId).IsUnique();
            // Generic name/value extras as a JSON document: jsonb on
            // PostgreSQL, plain text when a lightweight provider is used
            // (tests). The value converter bridges the dictionary type; the
            // comparer (snapshot of the serialized text) keeps change
            // tracking honest for the mutable dictionary.
            entity.Property(x => x.Extra)
                .HasConversion(
                    value => SerializeExtra(value),
                    value => DeserializeExtra(value),
                    new ValueComparer<Dictionary<string, string?>>(
                        (a, b) => SerializeExtra(a) == SerializeExtra(b),
                        value => SerializeExtra(value).GetHashCode(),
                        value => DeserializeExtra(SerializeExtra(value))))
                .HasColumnType(Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite" ? "TEXT" : "jsonb");
        });

        modelBuilder.Entity<TeamSubscription>(entity =>
        {
            entity.ToTable("team_subscriptions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).UseIdentityByDefaultColumn().ValueGeneratedOnAdd();
            entity.HasIndex(x => new { x.UserId, x.TeamId }).IsUnique();
            entity.HasIndex(x => x.UserId);
            entity.HasIndex(x => x.TeamId);
        });

        modelBuilder.Entity<LeagueSubscription>(entity =>
        {
            entity.ToTable("league_subscriptions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).UseIdentityByDefaultColumn().ValueGeneratedOnAdd();
            entity.HasIndex(x => new { x.UserId, x.LeagueId }).IsUnique();
            entity.HasIndex(x => x.UserId);
        });

        modelBuilder.Entity<OrganizationSubscription>(entity =>
        {
            entity.ToTable("organization_subscriptions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).UseIdentityByDefaultColumn().ValueGeneratedOnAdd();
            entity.HasIndex(x => new { x.UserId, x.OrganizationId }).IsUnique();
            entity.HasIndex(x => x.UserId);
        });
    }
}
