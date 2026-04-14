using EShop.BuildingBlocks.Domain;
using EShop.Identity.Domain.Entities;
using EShop.Identity.Domain.Interfaces;
using EShop.Identity.Infrastructure.Configuration;
using EShop.Identity.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Moq;

namespace EShop.Identity.UnitTests.Services;

[TestFixture]
public class TokenServiceTransactionTests
{
    [Test]
    public async Task RotateRefreshTokenAsync_WhenOuterTransactionExists_ShouldNotCommitOrSaveDirectly()
    {
        var settings = Options.Create(new JwtSettings
        {
            SecretKey = "THIS_IS_A_TEST_ONLY_SECRET_KEY_32_CHARS_MINIMUM",
            Issuer = "issuer",
            Audience = "audience",
            RefreshTokenExpirationDays = 7
        });

        var refreshRepo = new Mock<IRefreshTokenRepository>();
        refreshRepo
            .Setup(x => x.RevokeTokenAtomicallyAsync(
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        refreshRepo
            .Setup(x => x.AddAsync(It.IsAny<RefreshTokenEntity>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(x => x.HasActiveTransaction).Returns(true);

        var service = new TokenService(
            settings,
            MockUserManager().Object,
            refreshRepo.Object,
            unitOfWork.Object);

        var oldToken = new RefreshTokenEntity
        {
            Token = "old-token",
            UserId = "user-1",
            ExpiresAt = DateTime.UtcNow.AddDays(1)
        };

        var rotated = await service.RotateRefreshTokenAsync(oldToken, "127.0.0.1", CancellationToken.None);

        Assert.That(rotated, Is.Not.Empty);
        unitOfWork.Verify(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
        unitOfWork.Verify(x => x.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static Mock<UserManager<ApplicationUser>> MockUserManager()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        return new Mock<UserManager<ApplicationUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
    }
}
