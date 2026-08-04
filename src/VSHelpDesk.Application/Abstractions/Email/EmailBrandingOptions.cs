
namespace VSHelpDesk.Application.Abstractions.Email;

/// <summary>Configurable options for corporate email theme and branding.</summary>
public sealed class EmailBrandingOptions
{
    public const string SectionName = "EmailBranding";

    public string CompanyName { get; set; } = "VS Help Desk";
    public string SystemName { get; set; } = "Corporate Customer Support System";
    public string? LogoUrl { get; set; }
    public bool RequireLogo { get; set; }
    public string PrimaryColor { get; set; } = "#2563eb";
    public string HeaderGradientStart { get; set; } = "#1e293b";
    public string HeaderGradientEnd { get; set; } = "#0f172a";
    public string SupportEmail { get; set; } = "support@vshelpdesk.com";
    public string? SupportPhone { get; set; }
    public string? WebsiteUrl { get; set; }
    public string? Address { get; set; }
    public string FooterText { get; set; } = "© 2026 VS Help Desk. All rights reserved.";
}
