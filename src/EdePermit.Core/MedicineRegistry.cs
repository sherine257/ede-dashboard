using ClosedXML.Excel;

namespace EdePermit.Core;

public sealed class MedicineRegistry
{
    readonly List<MedicineRecord> _all = new();
    readonly Dictionary<string, List<MedicineRecord>> _byName = new();
    readonly Dictionary<string, List<MedicineRecord>> _byIngredient = new();
    static readonly string[] HivTokens =
    [
        "efavirenz", "tenofovir", "emtricitabine", "dolutegravir", "ritonavir", "lopinavir",
        "zidovudine", "lamivudine hiv", "nevirapine", "atazanavir", "darunavir", "raltegravir",
        "bictegravir", "abacavir", "maraviroc", "enfuvirtide", "cobicistat", "rilpivirine",
        "doravirine", "elvitegravir"
    ];

    public IReadOnlyList<MedicineRecord> All => _all;
    public int Count => _all.Count;
    public string SourcePath { get; }

    public MedicineRegistry(string excelPath)
    {
        SourcePath = excelPath;
        if (!File.Exists(excelPath))
            throw new FileNotFoundException("EDE medicine register Excel was not found.", excelPath);

        using var wb = new XLWorkbook(excelPath);
        var ws = wb.Worksheets.First();
        var header = ws.Row(1);
        int Col(params string[] names)
        {
            foreach (var cell in header.CellsUsed())
            {
                var h = TextNorm.Normalize(cell.GetString());
                if (names.Any(n => h == TextNorm.Normalize(n))) return cell.Address.ColumnNumber;
            }
            return -1;
        }

        var cId = Col("Alt ID", "AltID");
        var cName = Col("Pack Name", "MedicineName", "PackName");
        var cIng = Col("Act Ing", "ActiveIngredient", "Ingredient");
        var cMode = Col("Disp Mode", "DispensingMode", "Dispensing Mode");
        var cForm = Col("Form", "ProductForm");
        var cSub = Col("Sub Form", "SubForm");
        var cStr = Col("Strength");
        if (cName < 0) throw new InvalidDataException("Excel is missing Pack Name.");

        foreach (var row in ws.RowsUsed().Skip(1))
        {
            string val(int c) => c < 0 ? "" : TextNorm.Repair(row.Cell(c).GetString().Trim());
            var rec = new MedicineRecord
            {
                AltId = val(cId),
                PackName = val(cName),
                ActiveIngredient = val(cIng),
                DispensingMode = val(cMode),
                Form = val(cForm),
                SubForm = val(cSub),
                Strength = val(cStr)
            };
            if (string.IsNullOrWhiteSpace(rec.PackName)) continue;
            _all.Add(rec);
            Index(_byName, rec.PackName, rec);
            if (!string.IsNullOrWhiteSpace(rec.ActiveIngredient))
                Index(_byIngredient, rec.ActiveIngredient, rec);
        }
    }

    static void Index(Dictionary<string, List<MedicineRecord>> map, string key, MedicineRecord rec)
    {
        var n = TextNorm.Compact(key);
        if (n.Length == 0) return;
        if (!map.TryGetValue(n, out var list))
        {
            list = new List<MedicineRecord>();
            map[n] = list;
        }
        list.Add(rec);
    }

    public MedicineMatch Match(string? name, string? ingredient)
    {
        var qName = TextNorm.Compact(name);
        var qIng = TextNorm.Compact(ingredient);

        if (qName.Length > 0 && _byName.TryGetValue(qName, out var exactName))
        {
            var distinct = DistinctPacks(exactName);
            if (distinct.Count == 1)
                return Hit(distinct[0], name, ingredient, "Exact pack name in ALL_REG_PACKS.xlsx");
        }

        if (qIng.Length > 0 && _byIngredient.TryGetValue(qIng, out var exactIng) && DistinctPacks(exactIng).Count == 1)
            return Hit(exactIng[0], name, ingredient, "Exact active ingredient in ALL_REG_PACKS.xlsx");

        if (qName.Length >= 4)
        {
            var contains = DistinctPacks(_all.Where(r =>
            {
                var n = TextNorm.Compact(r.PackName);
                return n == qName || n.StartsWith(qName) || (qName.Length >= 6 && n.Contains(qName));
            }).ToList());
            if (contains.Count == 1)
                return Hit(contains[0], name, ingredient, "Unique pack-name match in ALL_REG_PACKS.xlsx");
            if (contains.Count > 1)
            {
                var options = string.Join("; ", contains.Take(8).Select(c => $"{c.PackName} {c.Strength}".Trim()));
                return new MedicineMatch
                {
                    Matched = false,
                    QueryName = name ?? "",
                    QueryIngredient = ingredient ?? "",
                    Reason = $"Multiple packs match in ALL_REG_PACKS.xlsx. Specify one of: {options}"
                };
            }
        }

        return new MedicineMatch
        {
            Matched = false,
            QueryName = name ?? "",
            QueryIngredient = ingredient ?? "",
            Reason = "No record found in ALL_REG_PACKS.xlsx"
        };
    }

    static List<MedicineRecord> DistinctPacks(IReadOnlyList<MedicineRecord> rows) =>
        rows.GroupBy(r => TextNorm.Compact(r.AltId + r.PackName + r.Strength)).Select(g => g.First()).ToList();

    static MedicineMatch Hit(MedicineRecord rec, string? name, string? ingredient, string reason) => new()
    {
        Matched = true,
        QueryName = name ?? "",
        QueryIngredient = ingredient ?? "",
        Record = rec,
        Reason = reason
    };

    public static bool IsHiv(MedicineRecord? rec, string? queryName, string? queryIngredient)
    {
        var blob = TextNorm.Compact($"{rec?.PackName} {rec?.ActiveIngredient} {queryName} {queryIngredient}");
        return HivTokens.Select(TextNorm.Compact).Any(t => t.Length > 0 && blob.Contains(t));
    }

    static readonly string[] RestrictedTokens =
    [
        "methotrexate", "misoprostol", "cisplatin", "mifepristone", "thalidomide", "lenalidomide"
    ];

    public static bool IsRestricted(MedicineRecord? rec, string? queryName, string? queryIngredient)
    {
        var blob = TextNorm.Compact($"{rec?.PackName} {rec?.ActiveIngredient} {queryName} {queryIngredient}");
        return RestrictedTokens.Select(TextNorm.Compact).Any(t => t.Length > 0 && blob.Contains(t));
    }
}
