namespace EdePermit.Core;

public sealed class MedicineRecord
{
    public string AltId { get; set; } = "";
    public string PackName { get; set; } = "";
    public string ActiveIngredient { get; set; } = "";
    public string DispensingMode { get; set; } = "";
    public string Form { get; set; } = "";
    public string SubForm { get; set; } = "";
    public string Strength { get; set; } = "";
}

public sealed class MedicineMatch
{
    public bool Matched { get; set; }
    public string QueryName { get; set; } = "";
    public string QueryIngredient { get; set; } = "";
    public MedicineRecord? Record { get; set; }
    public string Reason { get; set; } = "";
}

public sealed class ApplicantProfile
{
    public string UaePassName { get; set; } = "Sara Ahmed Al Mansoori";
    public string DateOfBirth { get; set; } = "1991-05-14";
    public string EmiratesId { get; set; } = "784-1991-1234567-1";
    public string Nationality { get; set; } = "United Arab Emirates";
    public string Email { get; set; } = "sara.a@example.ae";
    public string Mobile { get; set; } = "+971 50 123 4567";
}

public sealed class ExtractedMedicine
{
    public string Name { get; set; } = "";
    public string ActiveIngredient { get; set; } = "";
    public double? Quantity { get; set; }
    public string Unit { get; set; } = "";
    public int? DurationDays { get; set; }
    public string? PrescriptionDate { get; set; }
    public string? ProductForm { get; set; }
}

public sealed class ExtractedApplication
{
    public string? ApplyingFor { get; set; }
    public bool? PatientResident { get; set; }
    public string? PatientName { get; set; }
    public string? DateOfBirth { get; set; }
    public string? Nationality { get; set; }
    public string? DocumentNumber { get; set; }
    public string? DocumentType { get; set; }
    public string? Email { get; set; }
    public string? Mobile { get; set; }
    public List<ExtractedMedicine> Medicines { get; set; } = new();
    public string? Notes { get; set; }
    public bool? UserConfirmedSummary { get; set; }
}

public sealed class ValidatedMedicine
{
    public string Name { get; set; } = "";
    public string ActiveIngredient { get; set; } = "";
    public double Quantity { get; set; }
    public string Unit { get; set; } = "";
    public int DurationDays { get; set; }
    public string ProductForm { get; set; } = "";
    public string DispensingMode { get; set; } = "N/A";
    public string Strength { get; set; } = "";
    public string RegistryId { get; set; } = "";
    public bool Matched { get; set; }
    public bool AutoEligible { get; set; }
    public bool Controlled { get; set; }
    public bool Injectable { get; set; }
    public bool Hiv { get; set; }
    public bool Restricted { get; set; }
    public List<string> FailedRules { get; set; } = new();
    public List<string> PassedGates { get; set; } = new();
}

public sealed class RuleFinding
{
    public string RuleId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Severity { get; set; } = "info";
    public string Message { get; set; } = "";
}

public sealed class ValidationResult
{
    public string Classification { get; set; } = "";
    public string ApplicationOutcome { get; set; } = "Incomplete";
    public bool Blocked { get; set; }
    public string BlockReason { get; set; } = "";
    public List<ValidatedMedicine> Medicines { get; set; } = new();
    public List<RuleFinding> Findings { get; set; } = new();
    public List<string> MissingFields { get; set; } = new();
    public List<string> RequiredDocuments { get; set; } = new();
    public List<string> MissingDocuments { get; set; } = new();
}

public sealed class ExtractionEnvelope
{
    public ExtractedApplication Data { get; set; } = new();
    public string Model { get; set; } = "";
    public bool UsedLlm { get; set; }
}

public static class ApplicationStatuses
{
    public const string Collecting = "Collecting";
    public const string FollowUp = "Follow-up required";
    public const string ReadyForSummary = "Ready for summary";
    public const string Submitted = "Submitted";
    public const string AutoApproved = "Auto-approved";
    public const string Mixed = "Mixed";
    public const string OfficerReview = "Officer review";
    public const string Completed = "Completed";
    public const string Rejected = "Rejected";
    public const string Correction = "Correction requested";
    public const string Blocked = "Blocked";
}
