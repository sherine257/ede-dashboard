namespace EdePermit.Core;

public sealed class ModePolicy
{
    public string Canonical { get; init; } = "N/A";
    public int TravellerMaxMonths { get; init; } = 6;
    public int ResidentMaxMonths { get; init; } = 3;
    public bool AutoApproval { get; init; } = true;
    public bool OfficerReview { get; init; }
    public bool Controlled { get; init; }
}

public static class DispensingCatalog
{
    static readonly Dictionary<string, ModePolicy> Map = Build();

    static Dictionary<string, ModePolicy> Build()
    {
        var map = new Dictionary<string, ModePolicy>(StringComparer.OrdinalIgnoreCase);
        void add(ModePolicy p, params string[] aliases)
        {
            foreach (var a in aliases.Prepend(p.Canonical))
                map[TextNorm.Normalize(a)] = p;
        }

        add(new ModePolicy { Canonical = "CD-Narcotic", TravellerMaxMonths = 3, ResidentMaxMonths = 1, AutoApproval = false, OfficerReview = true, Controlled = true },
            "CD Narcotic");
        add(new ModePolicy { Canonical = "Controlled Drug (CD)", TravellerMaxMonths = 3, ResidentMaxMonths = 1, AutoApproval = false, OfficerReview = true, Controlled = true },
            "Controlled Drug-CD", "Controlled Drug CD", "CD");
        add(new ModePolicy { Canonical = "SCD-OPH-H/C", TravellerMaxMonths = 3, ResidentMaxMonths = 1, AutoApproval = false, OfficerReview = true, Controlled = true });
        add(new ModePolicy { Canonical = "Semi Controlled Drug (SCD)", TravellerMaxMonths = 3, ResidentMaxMonths = 1, AutoApproval = false, OfficerReview = true, Controlled = true },
            "Semi Controlled Drug-SCD", "SCD");
        add(new ModePolicy { Canonical = "Hospitals Only", TravellerMaxMonths = 6, ResidentMaxMonths = 3, AutoApproval = true },
            "HOSPITALS ONLY", "HOSPITAL USE ONLY");
        add(new ModePolicy { Canonical = "POM-R (Dispense maximum for 2 weeks)", TravellerMaxMonths = 6, ResidentMaxMonths = 3, AutoApproval = true });
        add(new ModePolicy { Canonical = "POM-R (Dispense maximum for one month)", TravellerMaxMonths = 6, ResidentMaxMonths = 3, AutoApproval = true });
        add(new ModePolicy { Canonical = "POM-R (Gyn & Hospital)", TravellerMaxMonths = 6, ResidentMaxMonths = 3, AutoApproval = true });
        add(new ModePolicy { Canonical = "POM-R (Hospital only)", TravellerMaxMonths = 6, ResidentMaxMonths = 3, AutoApproval = true });
        add(new ModePolicy { Canonical = "POM-R (Specialist Gastro)", TravellerMaxMonths = 6, ResidentMaxMonths = 3, AutoApproval = true });
        add(new ModePolicy { Canonical = "POM-R (Specialist Ophthalmic)", TravellerMaxMonths = 6, ResidentMaxMonths = 3, AutoApproval = true },
            "POM-R (only by Specialist Ophthalmic use)");
        add(new ModePolicy { Canonical = "Prescription Only Medicine (POM)", TravellerMaxMonths = 6, ResidentMaxMonths = 3, AutoApproval = true },
            "POM");
        add(new ModePolicy { Canonical = "OTC-General Sale (OTC-G)", TravellerMaxMonths = 6, ResidentMaxMonths = 3, AutoApproval = true },
            "OTC-General Sale Supermarket Medicine (OTC-G)", "OTC-G");
        add(new ModePolicy { Canonical = "OTC-Pharmacy Medicine (OTC-P)", TravellerMaxMonths = 6, ResidentMaxMonths = 3, AutoApproval = true },
            "OTC-P");
        add(new ModePolicy { Canonical = "Pharmacist Only Medicine (Ph-OM)", TravellerMaxMonths = 6, ResidentMaxMonths = 3, AutoApproval = true },
            "Ph-OM");
        add(new ModePolicy { Canonical = "Pharmacy Only (P)", TravellerMaxMonths = 6, ResidentMaxMonths = 3, AutoApproval = true },
            "P");
        add(new ModePolicy { Canonical = "General sale / hypermarket only", TravellerMaxMonths = 6, ResidentMaxMonths = 3, AutoApproval = true });
        add(new ModePolicy { Canonical = "General Sale Supermarket GS(s)", TravellerMaxMonths = 6, ResidentMaxMonths = 3, AutoApproval = true },
            "GS(s)");
        add(new ModePolicy { Canonical = "Health Care Professional's Only", TravellerMaxMonths = 6, ResidentMaxMonths = 3, AutoApproval = true },
            "Health Care Professional’s only", "Health Care Professional's only", "Health Care Professionals only");
        add(new ModePolicy { Canonical = "N/A", TravellerMaxMonths = 6, ResidentMaxMonths = 3, AutoApproval = true },
            "", "NA");
        return map;
    }

    public static ModePolicy Resolve(string? mode)
    {
        var key = TextNorm.Normalize(mode);
        if (Map.TryGetValue(key, out var p)) return p;
        if (key.Contains("narcotic")) return Map[TextNorm.Normalize("CD-Narcotic")];
        if (key.Contains("controlled drug") || key == "cd") return Map[TextNorm.Normalize("Controlled Drug (CD)")];
        if (key.Contains("semi controlled") || key == "scd") return Map[TextNorm.Normalize("Semi Controlled Drug (SCD)")];
        if (key.Contains("hospital")) return Map[TextNorm.Normalize("Hospitals Only")];
        if (key.Contains("pom r") && key.Contains("ophthal")) return Map[TextNorm.Normalize("POM-R (Specialist Ophthalmic)")];
        if (key.Contains("prescription only") || key == "pom") return Map[TextNorm.Normalize("Prescription Only Medicine (POM)")];
        if (key.Contains("otc g") || key.Contains("general sale supermarket medicine")) return Map[TextNorm.Normalize("OTC-General Sale (OTC-G)")];
        if (key.Contains("pharmacy only")) return Map[TextNorm.Normalize("Pharmacy Only (P)")];
        if (key.Contains("health care professional")) return Map[TextNorm.Normalize("Health Care Professional's Only")];
        return Map[TextNorm.Normalize("N/A")];
    }

    public static bool IsInjectable(string? form, string? subForm, string? strength)
    {
        var blob = TextNorm.Normalize($"{form} {subForm} {strength}");
        string[] needles = ["injection", "infusion", "injectable", "vial", "ampoule", "ampule", "ampul"];
        return needles.Any(n => blob.Contains(n));
    }
}
