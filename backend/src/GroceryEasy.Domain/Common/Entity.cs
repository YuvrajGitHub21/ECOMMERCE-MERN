namespace GroceryEasy.Domain.Common;

/// <summary>
/// Base class for anything with an identity that outlives its field values.
/// </summary>
/// <remarks>
/// <para>
/// Identifiers are UUIDv7, generated in the constructor. Two properties matter:
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// <b>Sequential.</b> The leading 48 bits are a millisecond timestamp, so inserts land at
/// the right-hand edge of the B-tree instead of scattering across it. A random UUIDv4
/// primary key fragments the index and dirties a new page per insert.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>Not enumerable.</b> Unlike an auto-increment integer, an id in a URL discloses
/// neither how many rows exist nor what the neighbouring one is. The legacy app's IDOR
/// defects were made trivially exploitable by guessable identifiers.
/// </description>
/// </item>
/// </list>
/// <para>
/// Equality is by identity, not by value: two instances are the same entity when they are
/// the same type with the same <see cref="Id"/>, however far their other fields have
/// drifted. Value objects get the opposite treatment and are records.
/// </para>
/// </remarks>
public abstract class Entity : IEquatable<Entity>
{
    /// <summary>Creates an entity with a freshly generated UUIDv7 identifier.</summary>
    protected Entity() => Id = Guid.CreateVersion7();

    /// <summary>Creates an entity with a caller-supplied identifier.</summary>
    protected Entity(Guid id) => Id = id;

    /// <summary>
    /// The entity's identity. The setter is <see langword="protected"/> rather than
    /// <see langword="private"/> only so EF Core can rehydrate it during materialisation;
    /// nothing outside the type hierarchy can reassign an identity.
    /// </summary>
    public Guid Id { get; protected set; }

    public static bool operator ==(Entity? left, Entity? right) => Equals(left, right);

    public static bool operator !=(Entity? left, Entity? right) => !Equals(left, right);

    /// <inheritdoc />
    public bool Equals(Entity? other) =>
        other is not null && other.GetType() == GetType() && other.Id == Id;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Entity other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Id.GetHashCode();
}
