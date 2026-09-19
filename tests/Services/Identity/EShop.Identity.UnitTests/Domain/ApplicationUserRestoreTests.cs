using EShop.Identity.Domain.Entities;

namespace EShop.Identity.UnitTests.Domain;

/// <summary>
/// Admin panel S7, decision Q1a — <see cref="ApplicationUser.Restore"/> and its relationship with
/// the three transitions that already existed.
/// </summary>
/// <remarks>
/// The property worth protecting is not "restore clears IsDeleted", which is obvious, but
/// <b>"restore does not re-enable"</b>. Q1a chose that deliberately: a deleted account was disabled
/// when it was deleted, and bringing it back has to be two decisions, or one misclick resurrects a
/// working login. It reads like an omission — the natural instinct when writing this method is to
/// set <c>IsActive = true</c> as well — so it is pinned here rather than left to a comment.
/// </remarks>
[TestFixture]
public class ApplicationUserRestoreTests
{
    private static ApplicationUser NewUser() => new()
    {
        Id = "user-1",
        Email = "user@test.com",
        UserName = "user@test.com"
    };

    [Test]
    public void Restore_ClearsTheDeleteFlags()
    {
        var user = NewUser();
        user.SoftDelete();

        user.Restore();

        Assert.Multiple(() =>
        {
            Assert.That(user.IsDeleted, Is.False);
            Assert.That(user.DeletedAt, Is.Null);
        });
    }

    [Test]
    public void Restore_LeavesTheAccountDeactivated()
    {
        var user = NewUser();
        user.SoftDelete();

        user.Restore();

        Assert.That(user.IsActive, Is.False,
            "Q1a: restoring and re-enabling are two admin decisions, so a restored account must not "
            + "be able to sign in until someone activates it");
    }

    [Test]
    public void Restore_ThenActivate_BringsTheAccountFullyBack()
    {
        var user = NewUser();
        user.SoftDelete();

        user.Restore();
        user.Activate();

        Assert.Multiple(() =>
        {
            Assert.That(user.IsDeleted, Is.False);
            Assert.That(user.IsActive, Is.True);
        });
    }

    [Test]
    public void Activate_OnADeletedAccount_StillThrows()
    {
        // The guard Restore exists alongside, not one it replaces: reactivating a deleted account
        // in one step is still not a transition this model supports.
        var user = NewUser();
        user.SoftDelete();

        Assert.Throws<InvalidOperationException>(() => user.Activate());
    }

    [Test]
    public void Restore_OnALiveAccount_DoesNotDemoteIt()
    {
        // The mirror-image mistake of the one above. An unconditional
        // "IsDeleted = false; IsActive = false" would pass every test on this page except this one,
        // and would silently suspend a healthy account.
        var user = NewUser();

        user.Restore();

        Assert.Multiple(() =>
        {
            Assert.That(user.IsActive, Is.True);
            Assert.That(user.IsDeleted, Is.False);
        });
    }

    [Test]
    public void Restore_OnADeactivatedAccount_LeavesItDeactivated()
    {
        var user = NewUser();
        user.Deactivate();

        user.Restore();

        Assert.That(user.IsActive, Is.False, "restore has nothing to say about a suspension");
    }

    [Test]
    public void Restore_IsIdempotent()
    {
        var user = NewUser();
        user.SoftDelete();

        user.Restore();
        user.Restore();

        Assert.Multiple(() =>
        {
            Assert.That(user.IsDeleted, Is.False);
            Assert.That(user.DeletedAt, Is.Null);
        });
    }
}
