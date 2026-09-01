using Microsoft.AspNetCore.Identity;

namespace GroceryEasy.Infrastructure.Identity;

/// <summary>
/// A role, keyed by <see cref="Guid"/> to match <see cref="ApplicationUser"/>.
/// </summary>
/// <remarks>
/// Roles are coarse buckets; the real access decisions are made by policies, several of them
/// resource-based, because "is this person staff" and "may this person read <em>this</em> order"
/// are different questions. The legacy application answered the second with the first, which is
/// how any logged-in user could delete any review (L-05).
/// </remarks>
public sealed class ApplicationRole : IdentityRole<Guid>
{
    /// <summary>Creates a role with a sequential version-7 identifier.</summary>
    public ApplicationRole()
    {
        Id = Guid.CreateVersion7();
    }

    /// <summary>Creates a named role with a sequential version-7 identifier.</summary>
    public ApplicationRole(string roleName) : base(roleName)
    {
        Id = Guid.CreateVersion7();
    }
}

/// <summary>
/// The four role names, as constants, so a typo is a compile error rather than a silent
/// authorization hole.
/// </summary>
/// <remarks>
/// The legacy schema stored <c>role</c> as a free string with no enumeration, so any value at
/// all was a valid role. Seeded in Phase 1; the policies that consume them arrive with
/// multi-tenancy in Phase 2.
/// </remarks>
public static class Roles
{
    /// <summary>An ordinary shopper. The default on registration.</summary>
    public const string Customer = "Customer";

    /// <summary>Store employee: fulfils orders, verifies pickup codes, adjusts stock.</summary>
    public const string StoreStaff = "StoreStaff";

    /// <summary>Store owner or manager: everything staff can do, plus catalogue and pricing.</summary>
    public const string StoreManager = "StoreManager";

    /// <summary>Operator of the whole platform, across every store.</summary>
    public const string PlatformAdmin = "PlatformAdmin";

    /// <summary>Every role name, for seeding and for tests that sweep across all of them.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        Customer,
        StoreStaff,
        StoreManager,
        PlatformAdmin,
    ];
}
