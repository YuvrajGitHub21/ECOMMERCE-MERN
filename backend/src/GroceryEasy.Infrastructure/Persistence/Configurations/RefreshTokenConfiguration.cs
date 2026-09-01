using GroceryEasy.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GroceryEasy.Infrastructure.Persistence.Configurations;

/// <summary>
/// The refresh-token table: three indexes, each earning its place.
/// </summary>
internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");

        builder.HasKey(t => t.Id);

        // Base64 of 32 bytes is 44 characters including padding.
        builder.Property(t => t.TokenHash)
            .HasMaxLength(64)
            .IsRequired();

        // UNIQUE, and this is load-bearing rather than tidiness: the refresh endpoint looks a
        // token up by hash, and a duplicate would make "which token did they present" ambiguous
        // at exactly the moment reuse detection needs a definite answer. It also gives the
        // lookup its index for free.
        builder.HasIndex(t => t.TokenHash)
            .IsUnique();

        // Reuse detection revokes an entire lineage in one statement. Without this index that
        // is a sequential scan on every detection, which is the one path that must stay fast
        // because it runs while an attacker is holding a live token.
        builder.HasIndex(t => t.FamilyId);

        // "Log this user out everywhere" and "show me this user's sessions" both start here.
        builder.HasIndex(t => t.UserId);

        builder.Property(t => t.CreatedByIp)
            .HasMaxLength(45); // an IPv6 address in full text form

        builder.Property(t => t.UserAgent)
            .HasMaxLength(512);

        // Cascade: deleting a user must not leave orphaned credentials behind that still hash
        // to something. There is no scenario where a token outliving its user is correct.
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
