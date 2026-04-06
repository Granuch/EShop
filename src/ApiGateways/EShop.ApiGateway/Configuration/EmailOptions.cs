namespace EShop.ApiGateway.Configuration;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string From { get; set; } = "gateway@example.com";
    public string FromName { get; set; } = "EShop API Gateway";
    public bool CheckCertificateRevocation { get; set; }
}
