using GroceryEasy.Domain.Common;

namespace GroceryEasy.Domain.UnitTests.Common;

public sealed class EntityTests
{
    [Fact]
    public void NewEntity_GetsAnIdentifier()
    {
        var entity = new SampleEntity();

        entity.Id.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public void NewEntity_GetsAVersion7Identifier()
    {
        // UUIDv7 is not cosmetic: the leading 48 bits are a millisecond timestamp, which is
        // what keeps B-tree inserts dense. A regression to Guid.NewGuid() would be invisible
        // until an index started fragmenting in production, so it is asserted here.
        var entity = new SampleEntity();

        VersionOf(entity.Id).ShouldBe(7);
    }

    [Fact]
    public void NewEntities_GetDistinctIdentifiers()
    {
        var ids = Enumerable.Range(0, 1_000).Select(_ => new SampleEntity().Id).ToList();

        ids.Distinct().Count().ShouldBe(ids.Count);
    }

    [Fact]
    public void Entity_WithASuppliedIdentifier_KeepsIt()
    {
        Guid id = Guid.CreateVersion7();

        new SampleEntity(id).Id.ShouldBe(id);
    }

    [Fact]
    public void SameTypeAndId_AreEqual()
    {
        Guid id = Guid.CreateVersion7();
        var left = new SampleEntity(id);
        var right = new SampleEntity(id);

        left.Equals(right).ShouldBeTrue();
        (left == right).ShouldBeTrue();
        (left != right).ShouldBeFalse();
        left.GetHashCode().ShouldBe(right.GetHashCode());
    }

    [Fact]
    public void SameTypeDifferentId_AreNotEqual()
    {
        var left = new SampleEntity();
        var right = new SampleEntity();

        left.Equals(right).ShouldBeFalse();
        (left == right).ShouldBeFalse();
        (left != right).ShouldBeTrue();
    }

    [Fact]
    public void DifferentTypesSharingAnId_AreNotEqual()
    {
        // Without the GetType() check an order and a payment holding the same id would
        // compare equal, which silently corrupts any Dictionary or HashSet keyed on entities.
        Guid id = Guid.CreateVersion7();
        var entity = new SampleEntity(id);
        var other = new OtherSampleEntity(id);

        entity.Equals(other).ShouldBeFalse();
        (entity == other).ShouldBeFalse();
    }

    [Fact]
    public void Entity_IsNotEqualToNull()
    {
        var entity = new SampleEntity();

        entity.Equals(null).ShouldBeFalse();
        (entity == null).ShouldBeFalse();
        (null == entity).ShouldBeFalse();
    }

    [Fact]
    public void TwoNulls_AreEqual()
    {
        SampleEntity? left = null;
        SampleEntity? right = null;

        (left == right).ShouldBeTrue();
    }

    [Fact]
    public void Entity_IsNotEqualToAnUnrelatedObject()
    {
        var entity = new SampleEntity();

        entity.Equals("not an entity").ShouldBeFalse();
    }

    /// <summary>Reads the RFC 9562 version nibble, which is the high nibble of octet 6.</summary>
    private static int VersionOf(Guid id) => (id.ToByteArray(bigEndian: true)[6] >> 4) & 0x0F;

    private sealed class SampleEntity : Entity
    {
        public SampleEntity()
        {
        }

        public SampleEntity(Guid id) : base(id)
        {
        }
    }

    private sealed class OtherSampleEntity(Guid id) : Entity(id);
}
