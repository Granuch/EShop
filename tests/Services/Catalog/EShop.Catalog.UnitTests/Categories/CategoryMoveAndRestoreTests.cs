using EShop.BuildingBlocks.Domain.Exceptions;
using EShop.Catalog.Domain.Entities;

namespace EShop.Catalog.UnitTests.Categories;

/// <summary>
/// <c>Category.MoveTo</c>, <c>Restore</c> and <c>SetDisplayOrder</c> (Admin panel S5). A category's
/// parent was fixed at creation from Catalog audit Stage 8 onward, because the cycle check that
/// existed then walked the in-memory navigation and could not be trusted.
/// </summary>
[TestFixture]
public class CategoryMoveAndRestoreTests
{
    private static Category NewRoot(string name = "Root")
        => Category.Create(name, null, null);

    #region MoveTo

    [Test]
    public void MoveTo_SetsTheNewParentId()
    {
        var category = NewRoot("Movable");
        var target = Guid.NewGuid();

        category.MoveTo(target, []);

        Assert.That(category.ParentCategoryId, Is.EqualTo(target));
    }

    [Test]
    public void MoveTo_Null_PromotesToRoot()
    {
        var parent = NewRoot("Parent");
        var child = Category.Create("Child", null, parent);
        Assert.That(child.ParentCategoryId, Is.EqualTo(parent.Id), "precondition");

        child.MoveTo(null, []);

        Assert.That(child.ParentCategoryId, Is.Null);
    }

    [Test]
    public void MoveTo_ClearsTheStaleParentNavigation()
    {
        // The caller passed an id, not an entity, so the loaded navigation now points at the wrong
        // parent. Leaving it would let EF write the old relationship back.
        var parent = NewRoot("Parent");
        var child = Category.Create("Child", null, parent);

        child.MoveTo(Guid.NewGuid(), []);

        Assert.That(child.ParentCategory, Is.Null);
    }

    [Test]
    public void MoveTo_ItsOwnId_IsRefused()
    {
        var category = NewRoot();

        Assert.Throws<DomainException>(() => category.MoveTo(category.Id, []));
    }

    [Test]
    public void MoveTo_UnderItsOwnDescendant_IsRefused()
    {
        // The cycle the ancestor chain exists to catch: moving A under C, where C's chain is
        // [B, A]. The aggregate cannot see its descendants — it sees the target's ancestors, and
        // finding itself there is the same fact from the other end.
        var a = NewRoot("A");
        var descendantChain = new[] { Guid.NewGuid(), a.Id };

        Assert.Throws<DomainException>(() => a.MoveTo(Guid.NewGuid(), descendantChain));
    }

    [Test]
    public void MoveTo_WithTheCategoryAbsentFromTheChain_IsAllowed()
    {
        var a = NewRoot("A");
        var unrelatedChain = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var target = Guid.NewGuid();

        a.MoveTo(target, unrelatedChain);

        Assert.That(a.ParentCategoryId, Is.EqualTo(target));
    }

    [Test]
    public void MoveTo_ToRoot_IgnoresTheAncestorChain()
    {
        // Promoting to root cannot create a cycle whatever the chain says, and passing a stale one
        // must not block it.
        var category = NewRoot();
        var chainContainingItself = new[] { category.Id };

        Assert.DoesNotThrow(() => category.MoveTo(null, chainContainingItself));
    }

    [Test]
    public void MoveTo_TheSameParent_IsANoOp()
    {
        var parent = NewRoot("Parent");
        var child = Category.Create("Child", null, parent);

        child.MoveTo(parent.Id, []);

        Assert.Multiple(() =>
        {
            Assert.That(child.ParentCategoryId, Is.EqualTo(parent.Id));
            Assert.That(child.ParentCategory, Is.Not.Null, "a no-op must not clear the navigation");
        });
    }

    [Test]
    public void MoveTo_OnADeletedCategory_IsRefused()
    {
        var category = NewRoot();
        category.Deactivate();

        Assert.Throws<DomainException>(() => category.MoveTo(Guid.NewGuid(), []));
    }

    [Test]
    public void MoveTo_WithANullChain_Throws()
    {
        var category = NewRoot();

        Assert.Throws<ArgumentNullException>(() => category.MoveTo(Guid.NewGuid(), null!));
    }

    #endregion

    #region Restore

    [Test]
    public void Restore_ReactivatesTheCategory()
    {
        var category = NewRoot();
        category.Deactivate();

        category.Restore();

        Assert.That(category.IsActive, Is.True);
    }

    [Test]
    public void Restore_OnALiveCategory_IsANoOp()
    {
        var category = NewRoot();

        category.Restore();

        Assert.That(category.IsActive, Is.True);
    }

    [Test]
    public void Restore_DoesNotTouchTheParent()
    {
        // Restoring must not cascade: whether the parent comes back too is a bigger decision than
        // this request expressed, and the handler refuses rather than guessing.
        var parent = NewRoot("Parent");
        var child = Category.Create("Child", null, parent);
        parent.Deactivate();
        child.Deactivate();

        child.Restore();

        Assert.Multiple(() =>
        {
            Assert.That(child.IsActive, Is.True);
            Assert.That(parent.IsActive, Is.False);
        });
    }

    [Test]
    public void Restore_MakesTheCategoryMovableAgain()
    {
        var category = NewRoot();
        category.Deactivate();
        Assert.Throws<DomainException>(() => category.MoveTo(Guid.NewGuid(), []), "precondition");

        category.Restore();

        Assert.DoesNotThrow(() => category.MoveTo(Guid.NewGuid(), []));
    }

    #endregion

    #region SetDisplayOrder

    [Test]
    public void SetDisplayOrder_Sets()
    {
        var category = NewRoot();

        category.SetDisplayOrder(7);

        Assert.That(category.DisplayOrder, Is.EqualTo(7));
    }

    [Test]
    public void SetDisplayOrder_Negative_IsRefused()
    {
        var category = NewRoot();

        Assert.Throws<DomainException>(() => category.SetDisplayOrder(-1));
    }

    #endregion
}
