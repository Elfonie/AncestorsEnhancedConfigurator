namespace AncestorsEnhanced.Core.Inspection;

public sealed record InspectionNotice(
    InspectionSeverity Severity,
    string Code,
    string Message);
