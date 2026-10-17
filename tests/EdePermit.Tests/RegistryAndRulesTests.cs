using EdePermit.Api;
using EdePermit.Core;

namespace EdePermit.Tests;

public sealed class RegistryFixture
{
    public MedicineRegistry Registry { get; }
    public BusinessRulesEngine Rules { get; }

    public RegistryFixture()
    {
        var path = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "data", "ALL_REG_PACKS.xlsx"),
            "/workspace/data/ALL_REG_PACKS.xlsx"
        }.First(File.Exists);
        Registry = new MedicineRegistry(path);
        Rules = new BusinessRulesEngine(Registry);
    }
}

public class RegistryAndRulesTests : IClassFixture<RegistryFixture>
{
    readonly RegistryFixture _fx;
    public RegistryAndRulesTests(RegistryFixture fx) => _fx = fx;

    ApplicantProfile Profile => new();

    ExtractedApplication CompleteSelf(string medicine, double qty = 30, int days = 30) => new()
    {
        ApplyingFor = "myself",
        PatientResident = true,
        PatientName = "Sara Ahmed Al Mansoori",
        DateOfBirth = "1991-05-14",
        Nationality = "United Arab Emirates",
        DocumentNumber = "784-1991-1234567-1",
        DocumentType = "emirates-id",
        Email = "sara.a@example.ae",
        Mobile = "+971 50 123 4567",
        Medicines =
        [
            new ExtractedMedicine
            {
                Name = medicine,
                Quantity = qty,
                Unit = "tablets",
                DurationDays = days,
                PrescriptionDate = "2026-09-15"
            }
        ]
    };

    RulesContext Ctx(ExtractedApplication app, params string[] docs) => new()
    {
        Application = app,
        SignedIn = Profile,
        UploadedDocumentTypes = docs,
        SubmissionUtc = new DateTime(2026, 10, 17, 0, 0, 0, DateTimeKind.Utc)
    };

    [Fact]
    public void Loads_official_register()
    {
        Assert.True(_fx.Registry.Count >= 34000);
        Assert.Contains("ALL_REG_PACKS.xlsx", _fx.Registry.SourcePath);
    }

    [Fact]
    public void Matches_lipitor_20mg_from_excel()
    {
        var hit = _fx.Registry.Match("Lipitor 20 mg", null);
        Assert.True(hit.Matched);
        Assert.Equal("Prescription Only Medicine (POM)", hit.Record!.DispensingMode);
        Assert.Contains("Atorvastatin", hit.Record.ActiveIngredient, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ambiguous_lipitor_is_not_invented()
    {
        var hit = _fx.Registry.Match("Lipitor", null);
        Assert.False(hit.Matched);
        Assert.Contains("Multiple packs", hit.Reason);
    }

    [Fact]
    public void Unknown_pack_is_no_record_found()
    {
        var hit = _fx.Registry.Match("Fakemedicinexyz 999mg", null);
        Assert.False(hit.Matched);
        Assert.Equal("No record found in ALL_REG_PACKS.xlsx", hit.Reason);
    }

    [Fact]
    public void GapFinder_lists_missing_checklist_fields_once()
    {
        var missing = GapFinder.MissingFields(new ExtractedApplication());
        Assert.Contains(missing, x => x.Contains("yourself or someone else"));
        Assert.Contains(missing, x => x.Contains("UAE resident"));
        Assert.Contains(missing, x => x.Contains("at least one medicine"));
    }

    [Fact]
    public void Pom_lipitor_can_auto_approve_when_complete()
    {
        var result = _fx.Rules.Evaluate(Ctx(CompleteSelf("Lipitor 20mg"), "prescription"));
        var med = Assert.Single(result.Medicines);
        Assert.True(med.Matched);
        Assert.True(med.AutoEligible);
        Assert.Equal(ApplicationStatuses.AutoApproved, result.ApplicationOutcome);
        Assert.Equal("Resident", result.Classification);
    }

    [Fact]
    public void Unmatched_medicine_routes_to_officer_review()
    {
        var result = _fx.Rules.Evaluate(Ctx(CompleteSelf("Fakemedicinexyz 999mg"), "prescription"));
        var med = Assert.Single(result.Medicines);
        Assert.False(med.Matched);
        Assert.False(med.AutoEligible);
        Assert.Equal("N/A", med.DispensingMode);
        Assert.Contains(med.FailedRules, x => x.Contains("BR-08") || x.Contains("BR-09"));
        Assert.Equal(ApplicationStatuses.OfficerReview, result.ApplicationOutcome);
    }

    [Fact]
    public void Controlled_medicine_is_never_auto_approved()
    {
        var result = _fx.Rules.Evaluate(Ctx(CompleteSelf("Rivotril 0.5mg"), "prescription", "medical-report"));
        var med = Assert.Single(result.Medicines);
        Assert.True(med.Matched);
        Assert.True(med.Controlled);
        Assert.False(med.AutoEligible);
        Assert.Equal(ApplicationStatuses.OfficerReview, result.ApplicationOutcome);
    }

    [Fact]
    public void Injectable_always_requires_officer_review()
    {
        var result = _fx.Rules.Evaluate(Ctx(CompleteSelf("RIVOTRIL 1mg/ml"), "prescription", "doctor-declaration"));
        var med = Assert.Single(result.Medicines);
        Assert.True(med.Injectable);
        Assert.False(med.AutoEligible);
        Assert.Contains(med.FailedRules, x => x.Contains("BR-11"));
    }

    [Fact]
    public void Active_application_blocks_new_submission()
    {
        var result = _fx.Rules.Evaluate(new RulesContext
        {
            Application = CompleteSelf("Lipitor 20mg"),
            SignedIn = Profile,
            UploadedDocumentTypes = ["prescription"],
            HasActiveApplication = true
        });
        Assert.True(result.Blocked);
        Assert.Contains("BR-14", result.BlockReason);
    }

    [Fact]
    public void Restricted_product_is_not_auto_eligible()
    {
        var result = _fx.Rules.Evaluate(Ctx(CompleteSelf("Methotrexate"), "prescription"));
        var med = Assert.Single(result.Medicines);
        Assert.False(med.AutoEligible);
        Assert.Contains(med.FailedRules, x => x.Contains("BR-10") || x.Contains("BR-08") || x.Contains("BR-09") || x.Contains("BR-03"));
    }

    [Fact]
    public void Quantity_over_180_fails_br07()
    {
        var result = _fx.Rules.Evaluate(Ctx(CompleteSelf("Lipitor 20mg", qty: 200), "prescription"));
        var med = Assert.Single(result.Medicines);
        Assert.Contains(med.FailedRules, x => x.Contains("BR-07"));
        Assert.False(med.AutoEligible);
    }

    [Fact]
    public void Health_care_professional_mode_resolves_curly_apostrophe()
    {
        var policy = DispensingCatalog.Resolve("Health Care Professional’s only");
        Assert.Equal("Health Care Professional's Only", policy.Canonical);
    }

    [Fact]
    public void Merger_does_not_reask_captured_fields()
    {
        var target = new ExtractedApplication { PatientName = "Sara Ahmed Al Mansoori", ApplyingFor = "myself" };
        ApplicationMerger.Merge(target, new ExtractedApplication
        {
            PatientResident = true,
            Medicines = [new ExtractedMedicine { Name = "Lipitor 20mg", Quantity = 30, Unit = "tablets", DurationDays = 30 }]
        });
        Assert.Equal("myself", target.ApplyingFor);
        Assert.Equal("Sara Ahmed Al Mansoori", target.PatientName);
        Assert.True(target.PatientResident);
        Assert.Single(target.Medicines);
    }
}

public class ExtractorTests
{
    [Fact]
    public void Heuristic_extracts_one_shot_checklist_reply()
    {
        var data = OpenAiExtractor.HeuristicExtract("""
            Applying for myself. The patient is a UAE resident.
            Patient full name Sara Ahmed Al Mansoori. Date of birth 1991-05-14.
            Nationality United Arab Emirates. Emirates ID 784-1991-1234567-1.
            Email sara.a@example.ae Mobile +971 50 123 4567.
            Medicine Lipitor 20 mg 30 tablets for 30 days.
            Prescription issue date 2026-09-15.
            """);
        Assert.Equal("myself", data.ApplyingFor);
        Assert.True(data.PatientResident);
        Assert.Equal("Sara Ahmed Al Mansoori", data.PatientName);
        Assert.Equal("1991-05-14", data.DateOfBirth);
        Assert.Equal("United Arab Emirates", data.Nationality);
        Assert.Contains("784", data.DocumentNumber);
        var med = Assert.Single(data.Medicines);
        Assert.Contains("Lipitor", med.Name, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(30, med.Quantity);
        Assert.Equal(30, med.DurationDays);
        Assert.Equal("2026-09-15", med.PrescriptionDate);
    }

    [Fact]
    public void Confirm_alone_is_enough()
    {
        Assert.True(OpenAiExtractor.LooksLikeConfirm("confirm"));
        var data = OpenAiExtractor.HeuristicExtract("confirm");
        Assert.True(data.UserConfirmedSummary);
        Assert.Empty(data.Medicines);
    }
}
