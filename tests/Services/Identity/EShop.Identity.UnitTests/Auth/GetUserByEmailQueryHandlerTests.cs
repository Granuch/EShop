using EShop.Identity.Application.Auth.Queries.GetUserByEmail;
using EShop.Identity.Domain.Entities;
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

    [Test]
    public async Task Handle_WithDeletedUser_ReturnsFailure()
    {
        // Arrange
        var query = new GetUserByEmailQuery { Email = "deleted@test.com" };
        var user = new ApplicationUser { Id = "1", Email = "deleted@test.com", IsDeleted = true };
        
        _userManagerMock.Setup(x => x.FindByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync(user);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo("Auth.UserNotFound"));
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
            IsActive = true,
            IsDeleted = false,
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
