using GroceryEasy.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GroceryEasy.Infrastructure.Persistence.Configurations;

/// <summary>
/// Column types and constraints for the users table, on top of what Identity configures.
/// </summary>
internal sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(u => u.FullName)
            .HasMaxLength(200)
            .IsRequired();

        // citext: case-insensitive at the database level. Identity already enforces uniqueness
        // on the normalised (upper-cased) copy, so this is belt and braces — but it also means
        // any hand-written query or admin tool comparing the raw column behaves correctly,
        // which the normalised-column trick alone does not give you.
        builder.Property(u => u.Email)
            .HasColumnType("citext")
            .HasMaxLength(256);

        builder.Property(u => u.NormalizedEmail)
            .HasMaxLength(256);

        // varchar, never a numeric type. A phone number typed as a number silently loses a
        // leading zero and cannot hold a '+' — the legacy schema made exactly this mistake with
        // both phoneNo and pinCode. E.164 is at most 15 digits plus the '+'.
        builder.Property(u => u.PhoneNumber)
            .HasMaxLength(16);

        builder.Property(u => u.CreatedAt)
            .IsRequired();

        builder.Property(u => u.UpdatedAt)
            .IsRequired();
    }
}
