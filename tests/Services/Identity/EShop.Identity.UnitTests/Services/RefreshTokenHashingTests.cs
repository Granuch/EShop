using EShop.BuildingBlocks.Domain;
using EShop.Identity.Domain.Entities;
using EShop.Identity.Domain.Interfaces;
using EShop.Identity.Domain.Security;
using EShop.Identity.Infrastructure.Configuration;
using EShop.Identity.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Moq;

namespace EShop.Identity.UnitTests.Services;

/// <summary>
/// SEC-04. Refresh tokens were stored raw in <c>refresh_tokens.Token</c>: 64 CSPRNG bytes,
/// base64-encoded, written straight to a uniquely-indexed column. Read access to that one table
/// was therefore full session takeover for every user with a live session — nothing to crack,
/// just replay. The posture was already self-contradictory, since <c>RevokedTokenCache</c>
/// SHA-256-hashed the same value before using it as a cache key, with a comment explaining why.
///
/// Tokens are now persisted only as a hash. These tests pin the property that matters — the
/// plaintext never reaches the entity — at both mint sites, because the generation code was
/// duplicated and fixing one site while missing the other would look identical from the outside.
/// </summary>
[TestFixture]
public class RefreshTokenHashingTests
{
    private Mock<IRefreshTokenRepository> _repositoryMock = null!;
    private Mock<IUnitOfWork> _unitOfWorkMock = null!;
    private List<RefreshTokenEntity> _persisted = null!;
    private TokenService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _persisted = [];
        _repositoryMock = new Mock<IRefreshTokenRepository>();
        _repositoryMock
            .Setup(x => x.AddAsync(It.IsAny<RefreshTokenEntity>(), It.IsAny<CancellationToken>()))
            .Callback<RefreshTokenEntity, CancellationToken>((entity, _) => _persisted.Add(entity))
            .Returns(Task.CompletedTask);
        _repositoryMock
            .Setup(x => x.RevokeTokenByHashAtomicallyAsync(
                It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _unitOfWorkMock.SetupGet(x => x.HasActiveTransaction).Returns(true);

        var settings = Options.Create(new JwtSettings
        {
            SecretKey = "THIS_IS_A_TEST_ONLY_SECRET_KEY_32_CHARS_MINIMUM",
            Issuer = "issuer",
            Audience = "audience",
            RefreshTokenExpirationDays = 7
        });

        _service = new TokenService(
            settings,
            MockUserManager().Object,
            _repositoryMock.Object,
            _unitOfWorkMock.Object,
            Mock.Of<ICachedUserRolesService>(),
            Mock.Of<IRevokedTokenCache>());
    }

    [Test]
    public async Task GenerateRefreshToken_PersistsOnlyTheHash()
    {
        var plaintext = await _service.GenerateRefreshTokenAsync("user-1", "127.0.0.1", CancellationToken.None);

        Assert.That(_persisted, Has.Count.EqualTo(1));
        var stored = _persisted[0];

        Assert.That(stored.TokenHash, Is.EqualTo(RefreshTokenHasher.Hash(plaintext)));
        Assert.That(stored.TokenHash, Is.Not.EqualTo(plaintext),
            "storing the token itself is the whole defect");
        AssertNoPlaintextAnywhere(stored, plaintext);
    }

    [Test]
    public async Task RotateRefreshToken_PersistsOnlyTheHash_AtTheSecondMintSite()
    {
        var oldToken = new RefreshTokenEntity
        {
            TokenHash = RefreshTokenHasher.Hash("old-token"),
            UserId = "user-1",
            ExpiresAt = DateTime.UtcNow.AddDays(1)
        };

        var plaintext = await _service.RotateRefreshTokenAsync(oldToken, "127.0.0.1", CancellationToken.None);

        Assert.That(_persisted, Has.Count.EqualTo(1));
        AssertNoPlaintextAnywhere(_persisted[0], plaintext);
        Assert.That(_persisted[0].TokenHash, Is.EqualTo(RefreshTokenHasher.Hash(plaintext)));
    }

    [Test]
    public async Task RotateRefreshToken_RevokesTheOldRowByHashAndRecordsTheSuccessorAsAHash()
    {
        var oldToken = new RefreshTokenEntity
        {
            TokenHash = RefreshTokenHasher.Hash("old-token"),
            UserId = "user-1",
            ExpiresAt = DateTime.UtcNow.AddDays(1)
        };

        var plaintext = await _service.RotateRefreshTokenAsync(oldToken, "127.0.0.1", CancellationToken.None);

        // ReplacedByToken used to hold the *new* token in the clear on the old row, so the
        // rotation chain leaked successors as well as the current token.
        _repositoryMock.Verify(x => x.RevokeTokenByHashAtomicallyAsync(
            oldToken.TokenHash,
            It.IsAny<DateTime>(),
            It.IsAny<string?>(),
            RefreshTokenHasher.Hash(plaintext),
            "Rotated",
            It.IsAny<CancellationToken>()), Times.Once);

        _repositoryMock.Verify(x => x.RevokeTokenByHashAtomicallyAsync(
            It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<string?>(),
            plaintext, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "the successor must be recorded as a hash, never as the token itself");
    }

    [Test]
    public void Entity_HasNoPlaintextTokenProperty()
    {
        // Re-adding a Token property would compile and quietly reintroduce the vulnerability
        // wherever someone assigned it, so guard the shape rather than only the behaviour.
        var propertyNames = typeof(RefreshTokenEntity).GetProperties().Select(p => p.Name).ToList();

        Assert.That(propertyNames, Does.Not.Contain("Token"));
        Assert.That(propertyNames, Does.Not.Contain("ReplacedByToken"));
        Assert.That(propertyNames, Does.Contain("TokenHash"));
        Assert.That(propertyNames, Does.Contain("ReplacedByTokenHash"));
    }

    [Test]
    public void Hash_IsStableAndFixedWidthHex()
    {
        var hash = RefreshTokenHasher.Hash("a-token");

        Assert.That(hash, Has.Length.EqualTo(RefreshTokenHasher.HashLength),
            "the column is char(64); a different width would pad or truncate silently");
        Assert.That(hash, Does.Match("^[0-9a-f]+$"), "lowercase hex, so comparisons are exact");
        Assert.That(RefreshTokenHasher.Hash("a-token"), Is.EqualTo(hash), "lookup depends on stability");
        Assert.That(RefreshTokenHasher.Hash("a-token "), Is.Not.EqualTo(hash));
    }

    [Test]
    public void HashOrNull_PreservesNull()
    {
        // ReplacedByTokenHash is absent until rotation; hashing "" would write a real digest and
        // make an un-rotated token look rotated.
        Assert.That(RefreshTokenHasher.HashOrNull(null), Is.Null);
        Assert.That(RefreshTokenHasher.HashOrNull(""), Is.Null);
        Assert.That(RefreshTokenHasher.HashOrNull("t"), Is.EqualTo(RefreshTokenHasher.Hash("t")));
    }

    private static void AssertNoPlaintextAnywhere(RefreshTokenEntity stored, string plaintext)
    {
        var stringValues = typeof(RefreshTokenEntity)
            .GetProperties()
            .Where(p => p.PropertyType == typeof(string))
            .Select(p => (string?)p.GetValue(stored));

        Assert.That(stringValues, Has.None.EqualTo(plaintext),
            "no column on the persisted row may contain the token itself");
    }

    private static Mock<UserManager<ApplicationUser>> MockUserManager()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        return new Mock<UserManager<ApplicationUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
    }
}
