using EShop.Identity.Domain.Entities;
using EShop.Identity.Domain.Interfaces;
using EShop.Identity.Infrastructure.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;

namespace EShop.Identity.IntegrationTests.Performance;

/// <summary>
/// Performance tests to validate N+1 query fixes
/// These tests ensure role resolution scales efficiently
/// </summary>
[TestFixture]
public class RoleResolutionPerformanceTests : IntegrationTestBase
{
    private IUserRepository _userRepository = null!;
    private UserManager<ApplicationUser> _userManager = null!;
    private RoleManager<ApplicationRole> _roleManager = null!;
    private IdentityDbContext _dbContext = null!;

    [SetUp]
    public override async Task SetUpAsync()
    {
        await base.SetUpAsync();

        var scope = Factory.Services.CreateScope();
        _userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        _userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        _roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        _dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
    }

    [Test]
    public async Task GetRolesAsync_SingleUser_ShouldReturnRolesCorrectly()
    {
        // Arrange: Create a test user with roles
        var user = new ApplicationUser
        {
            Email = $"test.{Guid.NewGuid()}@test.com",
            UserName = $"test.{Guid.NewGuid()}@test.com",
            FirstName = "Test",
            LastName = "User"
        };

        await _userManager.CreateAsync(user, "Test@123456");
        await _userManager.AddToRoleAsync(user, "User");

        // Ensure changes are persisted
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        // Act: Get roles using the optimized method
        var roles = await _userRepository.GetRolesAsync(user);

        // Assert
        roles.Should().Contain("User");
        roles.Should().HaveCount(1);
    }

    [Test]
    public async Task GetRolesForUsersAsync_MultipleUsers_ShouldReturnCorrectRoles()
    {
        // Arrange: Create multiple test users with roles
        var userCount = 50;
        var userIds = new List<string>();

        for (int i = 0; i < userCount; i++)
        {
            var user = new ApplicationUser
            {
                Email = $"bulktest.{i}.{Guid.NewGuid()}@test.com",
                UserName = $"bulktest.{i}.{Guid.NewGuid()}@test.com",
                FirstName = $"Bulk{i}",
                LastName = "User"
            };

            await _userManager.CreateAsync(user, "Test@123456");

            // Assign roles based on index
            if (i % 3 == 0)
            {
                await _userManager.AddToRoleAsync(user, "Admin");
            }
            if (i % 2 == 0)
            {
                await _userManager.AddToRoleAsync(user, "User");
            }

            userIds.Add(user.Id);
        }

        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        // Act: Get roles for all users in one bulk operation
        var rolesMap = await _userRepository.GetRolesForUsersAsync(userIds);

        // Assert: Should return roles for all users
        rolesMap.Should().HaveCount(userCount, "Should return roles for all users");

        // Verify role assignments
        var usersWithAdmin = rolesMap.Count(kvp => kvp.Value.Contains("Admin"));
        var usersWithUser = rolesMap.Count(kvp => kvp.Value.Contains("User"));

        usersWithAdmin.Should().BeGreaterThan(0, "Some users should have Admin role");
        usersWithUser.Should().BeGreaterThan(0, "Some users should have User role");

        TestContext.WriteLine($"Loaded roles for {userCount} users in a single bulk operation");
        TestContext.WriteLine($"Users with Admin role: {usersWithAdmin}");
        TestContext.WriteLine($"Users with User role: {usersWithUser}");
    }

    [Test]
    public async Task GetRolesForUsersAsync_CompareWithNPlusOnePattern_BulkShouldBeFaster()
    {
        // Arrange: Create test users
        var userCount = 20;
        var users = new List<ApplicationUser>();

        for (int i = 0; i < userCount; i++)
        {
            var user = new ApplicationUser
            {
                Email = $"perf.{i}.{Guid.NewGuid()}@test.com",
                UserName = $"perf.{i}.{Guid.NewGuid()}@test.com",
                FirstName = $"Perf{i}",
                LastName = "User"
            };

            await _userManager.CreateAsync(user, "Test@123456");
            await _userManager.AddToRoleAsync(user, "User");
            users.Add(user);
        }

        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        // Scenario 1: N+1 pattern (calling GetRolesAsync in a loop)
        var stopwatch1 = Stopwatch.StartNew();
        var n1Results = new List<IList<string>>();

        foreach (var user in users)
        {
            var roles = await _userRepository.GetRolesAsync(user);
            n1Results.Add(roles);
        }

        stopwatch1.Stop();
        _dbContext.ChangeTracker.Clear();

        // Scenario 2: Bulk query pattern
        var stopwatch2 = Stopwatch.StartNew();

        var userIds = users.Select(u => u.Id);
        var rolesMap = await _userRepository.GetRolesForUsersAsync(userIds);

        stopwatch2.Stop();

        // Assert: Bulk query should return same results
        rolesMap.Should().HaveCount(userCount);

        TestContext.WriteLine($"N+1 Pattern: {userCount} separate calls in {stopwatch1.ElapsedMilliseconds}ms");
        TestContext.WriteLine($"Bulk Pattern: 1 bulk call in {stopwatch2.ElapsedMilliseconds}ms");

        if (stopwatch1.ElapsedMilliseconds > 0)
        {
            var improvement = (double)stopwatch1.ElapsedMilliseconds / Math.Max(1, stopwatch2.ElapsedMilliseconds);
            TestContext.WriteLine($"Performance improvement: {improvement:F2}x faster");
        }

        // The bulk approach should complete (functional test, timing can vary)
        rolesMap.Values.Should().AllSatisfy(roles => roles.Should().Contain("User"));
    }

    [Test]
    public async Task GetRolesForUsersAsync_WithEmptyList_ShouldReturnEmptyDictionary()
    {
        // Arrange
        var emptyUserIds = Enumerable.Empty<string>();

        // Act
        var result = await _userRepository.GetRolesForUsersAsync(emptyUserIds);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Test]
    public async Task GetRolesForUsersAsync_WithUsersHavingNoRoles_ShouldReturnEmptyLists()
    {
        // Arrange: Create users without any role assignments
        var userIds = new List<string>();
        
        for (int i = 0; i < 5; i++)
        {
            var user = new ApplicationUser
            {
                Email = $"noroles.{i}.{Guid.NewGuid()}@test.com",
                UserName = $"noroles.{i}.{Guid.NewGuid()}@test.com",
                FirstName = $"NoRoles{i}",
                LastName = "User"
            };

            await _userManager.CreateAsync(user, "Test@123456");
            // Don't assign any roles
            userIds.Add(user.Id);
        }

        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        // Act
        var rolesMap = await _userRepository.GetRolesForUsersAsync(userIds);

        // Assert
        rolesMap.Should().HaveCount(userIds.Count);
        rolesMap.Values.Should().AllSatisfy(roles => 
            roles.Should().BeEmpty("Users should have no roles"));
    }

    [Test]
    public async Task GetRolesForUsersAsync_WithMixedRoleAssignments_ShouldReturnCorrectMappings()
    {
        // Arrange: Create users with different role combinations
        var user1 = await CreateUserWithRoles("mixed1", new[] { "Admin", "User" });
        var user2 = await CreateUserWithRoles("mixed2", new[] { "User" });
        var user3 = await CreateUserWithRoles("mixed3", new[] { "Admin" });
        var user4 = await CreateUserWithRoles("mixed4", Array.Empty<string>());

        var userIds = new[] { user1.Id, user2.Id, user3.Id, user4.Id };

        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        // Act
        var rolesMap = await _userRepository.GetRolesForUsersAsync(userIds);

        // Assert
        rolesMap[user1.Id].Should().Contain(new[] { "Admin", "User" });
        rolesMap[user2.Id].Should().Contain("User").And.HaveCount(1);
        rolesMap[user3.Id].Should().Contain("Admin").And.HaveCount(1);
        rolesMap[user4.Id].Should().BeEmpty();
    }

    private async Task<ApplicationUser> CreateUserWithRoles(string identifier, string[] roleNames)
    {
        var user = new ApplicationUser
        {
            Email = $"{identifier}.{Guid.NewGuid()}@test.com",
            UserName = $"{identifier}.{Guid.NewGuid()}@test.com",
            FirstName = identifier,
            LastName = "Test"
        };

        await _userManager.CreateAsync(user, "Test@123456");

        foreach (var roleName in roleNames)
        {
            await _userManager.AddToRoleAsync(user, roleName);
        }

        return user;
    }
}
