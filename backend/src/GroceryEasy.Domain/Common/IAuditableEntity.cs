namespace GroceryEasy.Domain.Common;

/// <summary>
/// Marks an entity whose creation and last-modification times are stamped automatically.
/// </summary>
/// <remarks>
/// <para>
/// The setters are deliberately <see langword="internal"/> to the assembly rather than public:
/// nothing in a handler should ever assign these by hand, because the moment two code paths
/// stamp a timestamp differently the column stops being trustworthy. A <c>SaveChanges</c>
/// interceptor in Infrastructure is the only writer.
/// </para>
/// <para>
/// The legacy schema hand-rolled <c>createdAt</c> and had no <c>updatedAt</c> anywhere at all,
/// which is why "when did this row last change" was unanswerable in the old system.
/// </para>
/// <para>
/// <see cref="DateTimeOffset"/>, never <see cref="DateTime"/>. It maps to PostgreSQL
/// <c>timestamptz</c> and carries the offset, so a row written by a container running in UTC
/// and read by a developer in India Standard Time means the same instant to both.
/// </para>
/// </remarks>
public interface IAuditableEntity
{
    /// <summary>When the row was first written.</summary>
    DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the row was last modified. Equal to <see cref="CreatedAt"/> on insert.</summary>
    DateTimeOffset UpdatedAt { get; set; }
}
