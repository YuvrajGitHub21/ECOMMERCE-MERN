using GroceryEasy.Domain.Common;
using Microsoft.AspNetCore.Identity;

namespace GroceryEasy.Infrastructure.Identity;

/// <summary>
/// The application's user, extending ASP.NET Core Identity with the few fields it does not carry.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this type lives in Infrastructure and not Domain.</b> It derives from
/// <see cref="IdentityUser{TKey}"/>, which is a type from the Identity package. Domain has zero
/// package references and an architecture test enforces that, so a user type that inherits from
/// a NuGet base class cannot live there. The alternative — a pure <c>Domain.User</c> mirrored
/// onto an Infrastructure Identity type — buys a cleaner diagram and costs a synchronisation
/// problem on every field, which is a bad trade for a system where Identity *is* the credential
/// store.
/// </para>
/// <para>
/// <b>Why <see cref="Guid"/> keys.</b> Identity defaults to <c>string</c> primary keys holding a
/// stringified GUID, which stores 36 characters where 16 bytes would do and sorts randomly.
/// <c>IdentityDbContext&lt;ApplicationUser, ApplicationRole, Guid&gt;</c> is the generic form
/// that fixes both, and identifiers are generated with <c>Guid.CreateVersion7()</c> so inserts
/// stay dense in the B-tree.
/// </para>
/// <para>
/// <b>Phone numbers reuse the inherited <see cref="IdentityUser{TKey}.PhoneNumber"/> column,</b>
/// configured as <c>varchar(15)</c> and documented as holding an E.164 value. Adding a second
/// <c>phone_e164</c> column alongside Identity's own would guarantee the two drift apart. The
/// column is text and never numeric — the legacy schema typed <c>phoneNo</c> as a number, which
/// silently destroys a leading zero.
/// </para>
/// </remarks>
public sealed class ApplicationUser : IdentityUser<Guid>, IAuditableEntity
{
    /// <summary>Creates a user with a sequential version-7 identifier.</summary>
    public ApplicationUser()
    {
        Id = Guid.CreateVersion7();
    }

    /// <summary>The customer's display name. Not split into given and family names deliberately:
    /// Indian naming conventions do not reliably decompose that way.</summary>
    public string FullName { get; set; } = string.Empty;

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; set; }

    /// <inheritdoc />
    public DateTimeOffset UpdatedAt { get; set; }
}
