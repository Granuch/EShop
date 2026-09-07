using EShop.Identity.Application.Auth.Queries.GetUserByEmail;
using EShop.Identity.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using EShop.Identity.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Moq;

namespace EShop.Identity.UnitTests.Auth;

[TestFixture]
public class GetUserByEmailQueryHandlerTests
{
    private Mock<UserManager<ApplicationUser>> _userManagerMock = null!;
    private GetUserByEmailQueryHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _userManagerMock = MockUserManager();
        _handler = new GetUserByEmailQueryHandler(_userManagerMock.Object);
    }

    [Test]
    public async Task Handle_WithNonExistentUser_ReturnsFailure()
    {
        // Arrange
        var query = new GetUserByEmailQuery { Email = "notfound@test.com" };
        
        _userManagerMock.Setup(x => x.FindByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((ApplicationUser?)null);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo("Auth.UserNotFound"));
    }

    /// <summary>
    /// This used to arrange a soft-deleted user and assert the handler rejected it. That check
    /// has moved out of the handler: <c>IdentityDbContext</c> now applies a global query filter
    /// (<c>!u.IsDeleted</c>) to <see cref="ApplicationUser"/>, so a soft-deleted user is never
    /// returned by <c>UserManager</c> in the first place — six handlers checked this by hand and
    /// five did not, which is precisely the inconsistency the filter removes.
    ///
    /// A mocked <c>UserManager</c> has no EF behind it, so it cannot exercise the filter and the
    /// old arrangement no longer represents anything reachable. What is worth pinning at this
    /// level is that the filter is actually configured, since deleting it would silently restore
    /// the old behaviour everywhere at once.
    /// </summary>
    [Test]
    public void SoftDeletedUsers_AreExcludedByAGlobalQueryFilter()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase($"filter-check-{Guid.NewGuid()}")
            .Options;

        using var context = new IdentityDbContext(options);
        var filter = context.Model.FindEntityType(typeof(ApplicationUser))?.GetQueryFilter();

        Assert.That(filter, Is.Not.Null,
            "ApplicationUser must keep its soft-delete query filter; without it IsDeleted is "
            + "advisory again and every handler has to re-check it by hand.");
        Assert.That(filter!.ToString(), Does.Contain(nameof(ApplicationUser.IsDeleted)));
    }

    [Test]
    public async Task Handle_WithExistingUser_ReturnsUserDetails()
    {
        // Arrange
        var query = new GetUserByEmailQuery { Email = "test@test.com" };
        var user = new ApplicationUser 
        { 
            Id = "1", 
            Email = "test@test.com", 
            FirstName = "John",
            LastName = "Doe",
            EmailConfirmed = true,
            TwoFactorEnabled = false,
            CreatedAt = DateTime.UtcNow
        };
        
        _userManagerMock.Setup(x => x.FindByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync(user);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.Id, Is.EqualTo("1"));
        Assert.That(result.Value.Email, Is.EqualTo("test@test.com"));
        Assert.That(result.Value.FirstName, Is.EqualTo("John"));
        Assert.That(result.Value.LastName, Is.EqualTo("Doe"));
    }

    private static Mock<UserManager<ApplicationUser>> MockUserManager()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        return new Mock<UserManager<ApplicationUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
    }
}
