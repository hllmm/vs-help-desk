namespace VSHelpDesk.Domain.Enums;

/// <summary>Antivirus / macro scan outcome for an attachment (SEC-006).</summary>
public enum ScanVerdict
{
    Unscanned = 0,
    Allowed = 1,
    Quarantined = 2
}
