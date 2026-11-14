using System.Text;

namespace XunxianDpkViewer.Core;

public static partial class SelfTest
{
    internal static string VerifyEquipmentUpdates(GameInfoRepository repository, string resourceFolder)
    {
        GameInfoCategory category = repository.Categories.Single(value => value.Id == "equipment");
        GameInfoRecord[] records = category.Stages.SelectMany(stage => stage.Records).ToArray();
        if (records.DistinctBy(record => record.Id).Count() != records.Length)
        {
            throw new InvalidDataException("Equipment stages contain duplicate items.");
        }

        using var reader = new DpkReader(Path.Combine(Directory.GetParent(resourceFolder)!.FullName, "mb.dpk"));
        var entries = reader.ReadEntries().ToDictionary(entry => entry.Path, StringComparer.OrdinalIgnoreCase);
        string[][] Rows(string path) => Encoding.GetEncoding("GB18030").GetString(reader.Extract(entries[path]))
            .Split('\n').Select(line => line.TrimEnd('\r').Split('\t')).ToArray();
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string path in new[] { "item/item_list.txt", "item/item_list2.txt", "item/item_list3.txt" })
        {
            foreach (string[] row in Rows(path).Where(row => row.Length > 1 && int.TryParse(row[0], out _)))
            {
                names[row[0]] = row[1];
            }
        }

        var seriesNames = records.Select(record => record.Name.Split('【')[0]).ToHashSet(StringComparer.Ordinal);
        string[][] legendRows = Rows("life/legend_equip/legend_equip_list.txt")
            .Where(row => row.Length > 1 && int.TryParse(row[0], out _)).ToArray();
        var legendIds = legendRows.Select(row => row[0]).ToHashSet(StringComparer.Ordinal);
        var expectedIds = legendRows
            .Where(row => names.GetValueOrDefault(row[0], row[1]).Contains('【') &&
                          seriesNames.Contains(names.GetValueOrDefault(row[0], row[1]).Split('【')[0]))
            .Select(row => row[0]).ToHashSet(StringComparer.Ordinal);
        if (!expectedIds.SetEquals(records.Select(record => record.Id)))
        {
            throw new InvalidDataException("Displayed equipment does not match the client legend equipment table.");
        }

        foreach (string[] row in Rows("life/legend_equip/legend_equip_convert_to_new.txt")
                     .Where(row => row.Length > 2 && names.ContainsKey(row[1]) && names.ContainsKey(row[2])))
        {
            if (records.Any(record => record.Id == row[1]) && legendIds.Contains(row[2]) && !expectedIds.Contains(row[2]) &&
                names[row[2]].IndexOf('【') > 0)
            {
                throw new InvalidDataException($"Client upgrade target is missing from equipment stages: {row[2]}.");
            }
        }

        GameInfoRecord MakeRecord(string id, string name) => new(id, name, null, string.Empty, string.Empty, null);
        GameEquipmentUpgrade MakeUpgrade(string fromId, string fromName, string toId, string toName) =>
            new(fromId, fromName, toId, toName, Array.Empty<GameEquipmentMaterial>(), null, 0, string.Empty, false);
        GameInfoRecord[] futureRecords =
        {
            MakeRecord("1", "星陨【test】"), MakeRecord("2", "future-one【test】"),
            MakeRecord("3", "future-two【test】"), MakeRecord("4", "unrelated【test】")
        };
        var futureUpgrades = new GameEquipmentUpgradeCatalog(string.Empty, new[]
        {
            MakeUpgrade("2", futureRecords[1].Name, "3", futureRecords[2].Name),
            MakeUpgrade("1", futureRecords[0].Name, "2", futureRecords[1].Name),
            MakeUpgrade("3", futureRecords[2].Name, "2", futureRecords[1].Name),
            MakeUpgrade("2", futureRecords[1].Name, "3", futureRecords[2].Name)
        });
        string[] discoveredIds = GameInfoRepository.BuildClientEquipmentStages(futureRecords, futureUpgrades)
            .SelectMany(stage => stage.Records).Select(record => record.Id).ToArray();
        if (!discoveredIds.SequenceEqual(new[] { "1", "2", "3" }))
        {
            throw new InvalidDataException("Future equipment stages are missing, duplicated or out of upgrade order.");
        }

        return string.Join(Environment.NewLine, category.Stages.Select(stage => $"{stage.Name}: {stage.Records.Count}")) +
               $"\nEquipment total: {records.Length}\nEQUIPMENT UPDATE CHECK PASSED\n";
    }
}
