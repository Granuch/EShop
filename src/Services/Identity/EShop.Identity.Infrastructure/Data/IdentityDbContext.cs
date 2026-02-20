using Microsoft.EntityFrameworkCore;
using EShop.Identity.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using EShop.BuildingBlocks.Infrastructure.Data;
using EShop.BuildingBlocks.Application.Abstractions;
using MediatR;

namespace EShop.Identity.Infrastructure.Data;

/// <summary>
/// DbContext for Identity service
/// Inherits from BaseIdentityDbContext to get UnitOfWork, domain events, outbox, and audit field support
/// </summary>
public class IdentityDbContext : BaseIdentityDbContext<ApplicationUser, ApplicationRole, string>
{
    public DbSet<RefreshTokenEntity> RefreshTokens => Set<RefreshTokenEntity>();

    public IdentityDbContext(DbContextOptions<IdentityDbContext> options) : base(options)
    {
    }

    public IdentityDbContext(DbContextOptions<IdentityDbContext> options, IMediator mediator) : base(options, mediator)
    {
    }

    public IdentityDbContext(
        DbContextOptions<IdentityDbContext> options, 
        IMediator mediator,
        ICurrentUserContext currentUserContext) : base(options, mediator, currentUserContext)
    {
    }

    /// <summary>
    /// Enable outbox for Identity service to support integration event publishing
    /// via the outbox pattern for reliable cross-service communication.
    /// </summary>
    protected override bool UseOutbox => true;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Configure ApplicationUser entity
        builder.Entity<ApplicationUser>(entity =>
        {
            entity.ToTable("users");
            entity.Property(u => u.FirstName).HasMaxLength(50).IsRequired();
            entity.Property(u => u.LastName).HasMaxLength(50).IsRequired();
            entity.Property(u => u.ProfilePictureUrl).HasMaxLength(500);
            entity.Property(u => u.GoogleId).HasMaxLength(100);
            entity.Property(u => u.GitHubId).HasMaxLength(100);
            entity.Property(u => u.TwoFactorSecret).HasMaxLength(200);
            entity.Property(u => u.LastLoginIp).HasMaxLength(50);

            entity.HasIndex(u => u.GoogleId).IsUnique().HasFilter("\"GoogleId\" IS NOT NULL");
            entity.HasIndex(u => u.GitHubId).IsUnique().HasFilter("\"GitHubId\" IS NOT NULL");
        });

        // Configure ApplicationRole entity
        builder.Entity<ApplicationRole>(entity =>
        {
            entity.ToTable("roles");
            entity.Property(r => r.Description).HasMaxLength(250);
        });

        // Configure RefreshToken entity
        builder.Entity<RefreshTokenEntity>(entity =>
        {
            entity.ToTable("refresh_tokens");
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Token).HasMaxLength(500).IsRequired();
            entity.Property(t => t.UserId).IsRequired();
            entity.Property(t => t.CreatedByIp).HasMaxLength(50);
            entity.Property(t => t.RevokedByIp).HasMaxLength(50);
            entity.Property(t => t.ReplacedByToken).HasMaxLength(500);
            entity.Property(t => t.RevokeReason).HasMaxLength(250);

            // Optimistic concurrency token for token rotation race condition protection
            entity.Property(t => t.Version).IsRowVersion();

            entity.HasIndex(t => t.Token).IsUnique();
            entity.HasIndex(t => t.UserId);
            entity.HasIndex(t => new { t.UserId, t.ExpiresAt });

            entity.HasOne(t => t.User)
                .WithMany()
                .HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Rename Identity tables
        builder.Entity<IdentityUserRole<string>>().ToTable("user_roles");
        builder.Entity<IdentityUserClaim<string>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<string>>().ToTable("user_logins");
        builder.Entity<IdentityUserToken<string>>().ToTable("user_tokens");
        builder.Entity<IdentityRoleClaim<string>>().ToTable("role_claims");
    }
}
