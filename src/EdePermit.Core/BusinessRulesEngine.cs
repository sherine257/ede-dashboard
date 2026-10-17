namespace EdePermit.Core;

public sealed class RulesContext
{
    public ExtractedApplication Application { get; init; } = new();
    public ApplicantProfile SignedIn { get; init; } = new();
    public IReadOnlyList<string> UploadedDocumentTypes { get; init; } = Array.Empty<string>();
    public DateTime SubmissionUtc { get; init; } = DateTime.UtcNow;
    public bool HasActiveApplication { get; init; }
    public bool RepeatWithin30Days { get; init; }
}

public sealed class BusinessRulesEngine
{
    readonly MedicineRegistry _registry;

    public BusinessRulesEngine(MedicineRegistry registry) => _registry = registry;

    public ValidationResult Evaluate(RulesContext ctx)
    {
        var app = ctx.Application;
        var result = new ValidationResult();
        var resident = app.PatientResident == true;
        result.Classification = app.PatientResident is null
            ? "Unknown"
            : resident ? "Resident" : "Traveller";

        if (app.PatientResident == true)
            result.Findings.Add(Info("BR-01", "Resident Classification", "Applicant classified as Resident."));
        else if (app.PatientResident == false)
            result.Findings.Add(Info("BR-02", "Traveller Classification", "Applicant classified as Traveller."));

        if (ctx.HasActiveApplication)
        {
            result.Blocked = true;
            result.BlockReason = "An active application already exists (BR-14).";
            result.ApplicationOutcome = ApplicationStatuses.Blocked;
            result.Findings.Add(Fail("BR-14", "Active Applications", result.BlockReason));
            return result;
        }

        result.MissingFields = GapFinder.MissingFields(app);
        result.RequiredDocuments = RequiredDocuments(app, ctx);
        result.MissingDocuments = result.RequiredDocuments
            .Where(d => !ctx.UploadedDocumentTypes.Contains(d, StringComparer.OrdinalIgnoreCase))
            .ToList();

        foreach (var med in app.Medicines.Where(m => !string.IsNullOrWhiteSpace(m.Name)))
            result.Medicines.Add(EvaluateMedicine(med, resident, ctx));

        if (result.Blocked)
            result.ApplicationOutcome = ApplicationStatuses.Blocked;
        else if (result.MissingFields.Count > 0 || result.MissingDocuments.Count > 0)
            result.ApplicationOutcome = ApplicationStatuses.FollowUp;
        else if (result.Medicines.Count == 0)
            result.ApplicationOutcome = ApplicationStatuses.FollowUp;
        else if (result.Medicines.All(m => m.AutoEligible))
            result.ApplicationOutcome = ApplicationStatuses.AutoApproved;
        else if (result.Medicines.Any(m => m.AutoEligible) && result.Medicines.Any(m => !m.AutoEligible))
            result.ApplicationOutcome = ApplicationStatuses.Mixed;
        else
            result.ApplicationOutcome = ApplicationStatuses.OfficerReview;

        return result;
    }

    ValidatedMedicine EvaluateMedicine(ExtractedMedicine src, bool resident, RulesContext ctx)
    {
        var match = _registry.Match(src.Name, src.ActiveIngredient);
        var rec = match.Record;
        var modeName = match.Matched ? rec!.DispensingMode : "N/A";
        var policy = DispensingCatalog.Resolve(modeName);
        var form = match.Matched ? rec!.Form : src.ProductForm ?? "";
        var injectable = DispensingCatalog.IsInjectable(form, rec?.SubForm, rec?.Strength);
        var hiv = MedicineRegistry.IsHiv(rec, src.Name, src.ActiveIngredient);
        var restricted = MedicineRegistry.IsRestricted(rec, src.Name, src.ActiveIngredient) || policy.Controlled;
        var quantity = src.Quantity ?? 0;
        var duration = src.DurationDays ?? 0;
        var maxDays = (resident ? policy.ResidentMaxMonths : policy.TravellerMaxMonths) * 30;

        var vm = new ValidatedMedicine
        {
            Name = match.Matched ? rec!.PackName : src.Name.Trim(),
            ActiveIngredient = match.Matched ? rec!.ActiveIngredient : src.ActiveIngredient,
            Quantity = quantity,
            Unit = src.Unit,
            DurationDays = duration,
            ProductForm = string.IsNullOrWhiteSpace(form) ? src.ProductForm ?? "" : form,
            DispensingMode = policy.Canonical,
            Strength = rec?.Strength ?? "",
            RegistryId = rec?.AltId ?? "",
            Matched = match.Matched,
            Controlled = policy.Controlled,
            Injectable = injectable,
            Hiv = hiv,
            Restricted = restricted
        };

        void fail(string id, string name, string msg)
        {
            vm.FailedRules.Add($"{id}: {msg}");
            vm.AutoEligible = false;
        }
        void pass(string msg) => vm.PassedGates.Add(msg);

        vm.AutoEligible = true;

        if (!policy.AutoApproval || policy.Controlled)
            fail("BR-03/BR-21", "Controlled / mode", "This dispensing mode is not eligible for auto-approval and requires officer review.");
        else pass("Dispensing mode allows auto-approval.");

        if (duration > 0 && duration > maxDays)
            fail("BR-04/BR-05", "Quantity limits", $"Duration {duration} days exceeds the {(resident ? "Resident" : "Traveller")} limit of {maxDays} days for this mode.");
        else if (duration > 0) pass("Duration within classification limit.");

        if (policy.Controlled && duration > 90)
            fail("BR-06", "Maximum prescription duration", "Controlled prescribed duration greater than 90 days is not eligible for auto-approval.");

        if (quantity > 180)
            fail("BR-07", "Maximum quantity threshold", "Quantity greater than 180 units is not eligible for auto-approval.");
        else if (quantity > 0) pass("Quantity does not exceed 180 units.");

        if (!match.Matched)
            fail("BR-08/BR-09", "Unregistered medicines", "Medicine or active ingredient was not matched to ALL_REG_PACKS.xlsx.");
        else pass("Product exists in the EDE register.");

        if (MedicineRegistry.IsRestricted(rec, src.Name, src.ActiveIngredient))
            fail("BR-10", "Restricted product", "Restricted product package is not eligible for auto-approval.");

        if (injectable)
            fail("BR-11", "Product form restriction", "Injectable, vial, or ampoule products always require officer review.");

        if (ctx.RepeatWithin30Days)
            fail("BR-12", "Repeat applicant restriction", "An application in the previous 30 days routes this case to officer review.");
        else pass("No application in the previous 30 days.");

        if (hiv)
            fail("BR-15", "HIV medicine restriction", "HIV registry medicines always require officer review.");

        if (policy.Controlled)
        {
            if (!TryParseDate(src.PrescriptionDate, out var rx) || (ctx.SubmissionUtc.Date - rx.Date).TotalDays > 90)
                fail("BR-16", "Prescription validation", "Controlled medicines require a prescription issued within 90 days.");
            else pass("Controlled-medicine prescription date is within 90 days.");
        }

        if (policy.Controlled && !ctx.UploadedDocumentTypes.Contains("medical-report", StringComparer.OrdinalIgnoreCase))
            fail("BR-17", "Medical report requirement", "Controlled medicines require both a prescription and a medical report.");

        if (injectable && !ctx.UploadedDocumentTypes.Contains("doctor-declaration", StringComparer.OrdinalIgnoreCase))
            fail("BR-19", "Doctor declaration requirement", "Injectable / vial / ampoule products require a doctor declaration.");

        var identityOk = IdentityMatches(ctx);
        if (!identityOk)
            fail("BR-20", "Identity matching", "Patient name or date of birth does not match UAE PASS / identity document.");
        else pass("Identity validation succeeded.");

        return vm;
    }

    public static bool IdentityMatches(RulesContext ctx)
    {
        var app = ctx.Application;
        if (string.IsNullOrWhiteSpace(app.PatientName) || string.IsNullOrWhiteSpace(app.DateOfBirth)) return false;
        if (!string.Equals(app.ApplyingFor, "myself", StringComparison.OrdinalIgnoreCase)) return true;
        return TextNorm.Normalize(app.PatientName) == TextNorm.Normalize(ctx.SignedIn.UaePassName)
            && DatesEqual(app.DateOfBirth, ctx.SignedIn.DateOfBirth);
    }

    static List<string> RequiredDocuments(ExtractedApplication app, RulesContext ctx)
    {
        var docs = new List<string> { "prescription" };
        var anyControlled = app.Medicines.Any(m =>
        {
            // Documents needed based on extracted names are resolved later; keep prescription always.
            return false;
        });
        _ = anyControlled;
        if (!string.Equals(app.ApplyingFor, "myself", StringComparison.OrdinalIgnoreCase) && app.ApplyingFor is not null)
            docs.Add(app.PatientResident == true ? "emirates-id" : "passport");
        return docs.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static List<string> DocumentsForValidated(ValidationResult validation, ExtractedApplication app)
    {
        var docs = new List<string> { "prescription" };
        if (validation.Medicines.Any(m => m.Controlled)) docs.Add("medical-report");
        if (validation.Medicines.Any(m => m.Injectable)) docs.Add("doctor-declaration");
        if (!string.Equals(app.ApplyingFor, "myself", StringComparison.OrdinalIgnoreCase) && app.ApplyingFor is not null)
            docs.Add(app.PatientResident == true ? "emirates-id" : "passport");
        return docs.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    static bool TryParseDate(string? value, out DateTime date) => DateTime.TryParse(value, out date);

    static bool DatesEqual(string? a, string? b)
    {
        if (DateTime.TryParse(a, out var da) && DateTime.TryParse(b, out var db))
            return da.Date == db.Date;
        return TextNorm.Normalize(a) == TextNorm.Normalize(b);
    }

    static RuleFinding Info(string id, string name, string msg) => new() { RuleId = id, Name = name, Severity = "info", Message = msg };
    static RuleFinding Fail(string id, string name, string msg) => new() { RuleId = id, Name = name, Severity = "block", Message = msg };
}

public static class GapFinder
{
    public static List<string> MissingFields(ExtractedApplication app)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(app.ApplyingFor)) missing.Add("whether you are applying for yourself or someone else");
        if (app.PatientResident is null) missing.Add("whether the patient is a UAE resident");
        if (string.IsNullOrWhiteSpace(app.PatientName)) missing.Add("patient full name");
        if (string.IsNullOrWhiteSpace(app.DateOfBirth)) missing.Add("patient date of birth");
        if (string.IsNullOrWhiteSpace(app.Nationality)) missing.Add("patient nationality");
        if (string.IsNullOrWhiteSpace(app.DocumentNumber))
            missing.Add(app.PatientResident == true ? "Emirates ID number" : "passport number");
        if (app.Medicines.Count == 0) missing.Add("at least one medicine (name, quantity, unit, duration)");
        else
        {
            for (var i = 0; i < app.Medicines.Count; i++)
            {
                var m = app.Medicines[i];
                var label = string.IsNullOrWhiteSpace(m.Name) ? $"medicine {i + 1}" : m.Name;
                if (string.IsNullOrWhiteSpace(m.Name)) missing.Add($"name for {label}");
                if (m.Quantity is null or <= 0) missing.Add($"quantity for {label}");
                if (string.IsNullOrWhiteSpace(m.Unit)) missing.Add($"unit for {label}");
                if (m.DurationDays is null or <= 0) missing.Add($"prescribed duration in days for {label}");
            }
        }
        return missing;
    }
}

public static class ApplicationMerger
{
    public static void Merge(ExtractedApplication target, ExtractedApplication incoming)
    {
        if (!string.IsNullOrWhiteSpace(incoming.ApplyingFor)) target.ApplyingFor = incoming.ApplyingFor;
        if (incoming.PatientResident is not null) target.PatientResident = incoming.PatientResident;
        if (!string.IsNullOrWhiteSpace(incoming.PatientName)) target.PatientName = incoming.PatientName;
        if (!string.IsNullOrWhiteSpace(incoming.DateOfBirth)) target.DateOfBirth = incoming.DateOfBirth;
        if (!string.IsNullOrWhiteSpace(incoming.Nationality)) target.Nationality = incoming.Nationality;
        if (!string.IsNullOrWhiteSpace(incoming.DocumentNumber)) target.DocumentNumber = incoming.DocumentNumber;
        if (!string.IsNullOrWhiteSpace(incoming.DocumentType)) target.DocumentType = incoming.DocumentType;
        if (!string.IsNullOrWhiteSpace(incoming.Email)) target.Email = incoming.Email;
        if (!string.IsNullOrWhiteSpace(incoming.Mobile)) target.Mobile = incoming.Mobile;
        if (!string.IsNullOrWhiteSpace(incoming.Notes)) target.Notes = incoming.Notes;
        if (incoming.UserConfirmedSummary is not null) target.UserConfirmedSummary = incoming.UserConfirmedSummary;

        if (incoming.Medicines.Count == 0) return;
        if (target.Medicines.Count == 0)
        {
            target.Medicines = incoming.Medicines;
            return;
        }
        foreach (var med in incoming.Medicines)
        {
            var existing = target.Medicines.FirstOrDefault(m =>
                TextNorm.Normalize(m.Name) == TextNorm.Normalize(med.Name) && !string.IsNullOrWhiteSpace(med.Name));
            if (existing is null)
            {
                if (!string.IsNullOrWhiteSpace(med.Name)) target.Medicines.Add(med);
                continue;
            }
            if (!string.IsNullOrWhiteSpace(med.ActiveIngredient)) existing.ActiveIngredient = med.ActiveIngredient;
            if (med.Quantity is not null) existing.Quantity = med.Quantity;
            if (!string.IsNullOrWhiteSpace(med.Unit)) existing.Unit = med.Unit;
            if (med.DurationDays is not null) existing.DurationDays = med.DurationDays;
            if (!string.IsNullOrWhiteSpace(med.PrescriptionDate)) existing.PrescriptionDate = med.PrescriptionDate;
            if (!string.IsNullOrWhiteSpace(med.ProductForm)) existing.ProductForm = med.ProductForm;
        }
    }
}
