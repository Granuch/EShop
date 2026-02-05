namespace EShop.Identity.IntegrationTests.Helpers;

/// <summary>
/// Test user constants for integration tests
/// </summary>
public static class TestUsers
{
    public static class Admin
    {
        public const string Email = "admin@test.com";
        public const string Password = "Admin@123456";
        public const string FirstName = "Admin";
        public const string LastName = "Test";
        public const string Role = "Admin";
    }

    public static class RegularUser
    {
        public const string Email = "user@test.com";
        public const string Password = "User@123456";
        public const string FirstName = "Regular";
        public const string LastName = "User";
        public const string Role = "User";
    }

    public static class InactiveUser
    {
        public const string Email = "inactive@test.com";
        public const string Password = "Inactive@123456";
        public const string FirstName = "Inactive";
        public const string LastName = "User";
        public const string Role = "User";
    }

    public static class UnconfirmedUser
    {
        public const string Email = "unconfirmed@test.com";
        public const string Password = "Unconfirmed@123456";
        public const string FirstName = "Unconfirmed";
        public const string LastName = "User";
        public const string Role = "User";
    }

    public static class Roles
    {
        public const string Admin = "Admin";
        public const string User = "User";
    }
}
