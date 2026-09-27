using EShop.Catalog.Application.Categories;
using EShop.Catalog.Domain.Entities;

namespace EShop.Catalog.UnitTests.Categories;

/// <summary>
/// F-37 (frontend-contracts R5). <see cref="CategoryTree"/> builds both category reads from one flat
/// read. The handler tests cover depth and the root count; these cover the rules a flat build has to
/// get right on its own.
/// </summary>
[TestFixture]
public class CategoryTreeTests
{
    [Test]
    public void Siblings_KeepTheOrderTheyWereReadIn()
    {
        var root = Category.Create("Root", "root", null);
        var b = Category.Create("B", "b", root);
        var a = Category.Create("A", "a", root);
        var c = Category.Create("C", "c", root);

        // The repository orders by DisplayOrder, Name, Id; the builder must not re-sort. Passed in a
        // deliberately non-alphabetical order so a re-sort by name would show.
        var tree = CategoryTree.Roots([root, c, a, b]);

        Assert.That(tree.Single().ChildCategories!.Select(x => x.Name), Is.EqualTo(new[] { "C", "A", "B" }));
    }

    [Test]
    public void ACategoryWhoseParentIsNotInTheRead_IsLeftOut()
    {
        // What the IsActive filter produces if a live category ever sat under a deleted one: the
        // parent is absent from the read, so the child is unreachable from every root.
        var deletedParent = Category.Create("Deleted parent", "deleted-parent", null);
        var orphan = Category.Create("Orphan", "orphan", deletedParent);
        var root = Category.Create("Root", "root", null);

        var tree = CategoryTree.Roots([orphan, root]);

        Assert.That(tree.Select(x => x.Name), Is.EqualTo(new[] { "Root" }));
    }

    [Test]
    public void ARoot_HasNoParentName()
    {
        var root = Category.Create("Root", "root", null);

        Assert.That(CategoryTree.Subtree([root], root.Id)!.ParentCategoryName, Is.Null);
    }

    [Test]
    public void Subtree_OfAnAbsentCategory_IsNull()
        => Assert.That(CategoryTree.Subtree([Category.Create("Root", "root", null)], Guid.NewGuid()), Is.Null);

    /// <summary>
    /// A cycle cannot be written through the API (Category.MoveTo checks the persisted chain), but
    /// the builder starts below the roots for a detail read, so corrupt data must still produce a
    /// finite tree rather than a stack overflow. MoveTo is handed an empty ancestor chain here to
    /// make the cycle the real handler would refuse.
    /// </summary>
    [Test]
    public void ACycleInTheData_StillYieldsAFiniteTree()
    {
        var a = Category.Create("A", "a", null);
        var b = Category.Create("B", "b", a);
        a.MoveTo(b.Id, []);

        var subtree = CategoryTree.Subtree([a, b], a.Id)!;

        Assert.That(subtree.ChildCategories!.Single().Name, Is.EqualTo("B"));
        Assert.That(subtree.ChildCategories!.Single().ChildCategories, Is.Empty);
    }
}
