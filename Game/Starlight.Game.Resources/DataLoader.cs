using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Starlight.Common;
using Serilog;
using Starlight.Game.Resources.Binary;

namespace Starlight.Game.Resources;

internal static partial class DataLoader
{
    /// <summary>
    /// Invokes the data loaders here, then outputs the data in the class's fields.
    /// </summary>
    public static void Initialize(GameData output)
    {
        // First pass of data loading.
        Task.WaitAll(
            Task.Run(() => LoadScenePoints(output)),
            Task.Run(() => LoadExcels(output)),
            Task.Run(() => LoadAbilities(output)),
            Task.Run(() => LoadAbilityGroups(output)),
            Task.Run(() => LoadAbilityPaths(output)),
            Task.Run(() => LoadEntityConfigs(output)),
            Task.Run(() => LoadLevelEntityConfigs(output)),
            Task.Run(() => LoadGlobalCombat(output)),
            Task.Run(() => LoadTalentConfigs(output))
        );

        // Second pass of data loading.
        Task.WaitAll(
            Task.Run(() => LoadAvatars(output))
        );

        Log.Information("Finished loading all resources.");
    }

    /// <summary>
    /// Loads all ExcelBinOutput (xlsx -> json) files.
    /// </summary>
    private static void LoadExcels(GameData output)
    {
        var stopwatch = Stopwatch.StartNew();

        var resources = typeof(DataLoader).Assembly.GetTypes()
            .SelectMany(t => t.GetCustomAttributes<GameResource>()
                .Select(attr => (t, attr)))
            .OrderByDescending(t => t.attr.Priority)
            .ToList();

        foreach (var (type, info) in resources)
        {
            var filePath = $"ExcelBinOutput/{info.FileName}";
            var typeName = type.Name;

            if (typeof(GameData)
                    .GetField(typeName, BindingFlags.Public | BindingFlags.Instance)?
                    .GetValue(output) is not
                IDictionary dictionary)
            {
                Log.Warning("Resource {0} has an invalid type.", typeName);
                continue;
            }

            switch (filePath.FileExtension())
            {
                case "json":
                    var listType = typeof(List<>).MakeGenericType(type);

                    var data = Resources.Loader.ReadJson(filePath, listType);

                    if (data is not IList list)
                    {
                        Log.Warning("Failed to load resource file: {0}", filePath);
                        continue;
                    }

                    foreach (var item in list)
                    {
                        if (item is not Data resource) continue;

                        var id = resource.GetId();

                        resource.OnLoad();

                        if (dictionary.Contains(id))
                        {
                            Log.Warning("Resource {0} has a value in the dictionary!", id);
                        }
                        dictionary[id] = resource;
                    }
                    break;
                case "tsv":
                    throw new Exception("TSV files are not supported.");
                case "tsj":
                    throw new Exception("TSJ files are not supported.");
                default:
                    Log.Warning("Unknown resource file extension: {0}", filePath);
                    continue;
            }
        }

        Log.Information("Loading excel resources took {0}ms", stopwatch.ElapsedMilliseconds);
    }

    #region Binary Data

    private static void LoadAbilities(GameData output)
    {
        var stopwatch = Stopwatch.StartNew();

        foreach (var path in Resources.Loader.ListFiles("BinOutput/Ability", "*.json", recursive: true)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            byte[] raw;

            try
            {
                raw = Resources.Loader.ReadRaw(path);
            }
            catch (Exception exception)
            {
                Log.Debug(exception, "Failed to read ability resource {Path}", path);
                continue;
            }

            ScanServerGlobalValues(raw, output.ServerGlobalValueHashes);

            try
            {
                using var document = JsonDocument.Parse(raw);

                if (document.RootElement.ValueKind != JsonValueKind.Array)
                {
                    Log.Warning("Ability resource {Path} does not contain an array root.", path);
                    continue;
                }

                foreach (var element in document.RootElement.EnumerateArray())
                {
                    try
                    {
                        var entry = JsonSerializer.Deserialize<AbilityConfigEntry>(element.GetRawText(), Constants.JsonOptions);
                        var ability = entry?.Default;

                        if (ability is null || string.IsNullOrEmpty(ability.AbilityName))
                            continue;

                        ability.NameHash = AbilityResourceHash.Compute(ability.AbilityName);
                        ability.Initialize();
                        output.Abilities[ability.AbilityName] = ability;

                        if (!output.AbilitiesByHash.TryGetValue(ability.NameHash, out var collisions))
                            output.AbilitiesByHash[ability.NameHash] = collisions = [];
                        collisions.Add(ability);
                    }
                    catch (Exception exception)
                    {
                        Log.Debug(exception, "Failed to load an ability entry from {Path}", path);
                    }
                }
            }
            catch (JsonException exception)
            {
                Log.Debug(exception, "Failed to parse ability resource {Path}", path);
            }
        }

        Log.Information("Loaded {Count} abilities in {Elapsed}ms", output.Abilities.Count, stopwatch.ElapsedMilliseconds);
    }

    private static void LoadAbilityGroups(GameData output)
    {
        foreach (var path in Resources.Loader.ListFiles("BinOutput/AbilityGroup", "*.json"))
        {
            var groups = Resources.Loader.ReadJson<Dictionary<string, AbilityGroupConfig>>(path);

            if (groups is null)
                continue;

            foreach (var (name, group) in groups)
            {
                output.AbilityGroups[name] = group;
            }
        }
    }

    private static void LoadAbilityPaths(GameData output)
    {
        foreach (var path in Resources.Loader.ListFiles("BinOutput/AbilityPath", "*.json"))
        {
            var config = Resources.Loader.ReadJson<AbilityPathConfig>(path);

            if (config is null)
                continue;

            foreach (var (name, abilities) in config.AbilityPaths)
            {
                output.AbilityPaths[name] = abilities;
            }
        }

        foreach (var path in Resources.Loader.ListFiles("BinOutput/GadgetPath", "*.json"))
        {
            var config = Resources.Loader.ReadJson<AbilityPathConfig>(path);

            if (config is null)
                continue;

            foreach (var (name, abilities) in config.AbilityPaths)
            {
                output.GadgetAbilityPaths[name] = abilities;
            }
        }
    }

    private static void LoadEntityConfigs(GameData output)
    {
        foreach (var path in Resources.Loader.ListFiles("BinOutput/Gadget", "*.json", recursive: true))
        {
            var configs = Resources.Loader.ReadJson<Dictionary<string, ConfigEntityGadget>>(path);

            if (configs is null)
                continue;

            foreach (var (name, config) in configs)
            {
                output.GadgetConfigs[name] = config;
            }
        }

        foreach (var path in Resources.Loader.ListFiles("BinOutput/Monster", "*.json"))
        {
            var config = Resources.Loader.ReadJson<ConfigEntityMonster>(path);

            if (config is null)
                continue;

            output.MonsterConfigs[Path.GetFileNameWithoutExtension(path).Replace("ConfigMonster_", string.Empty)] = config;
        }
    }

    private static void LoadLevelEntityConfigs(GameData output)
    {
        foreach (var path in Resources.Loader.ListFiles("BinOutput/LevelEntity", "*.json"))
        {
            var configs = Resources.Loader.ReadJson<Dictionary<string, ConfigLevelEntity>>(path);

            if (configs is null)
                continue;

            foreach (var (name, config) in configs)
            {
                output.LevelEntityConfigs[name] = config;
            }
        }
    }

    private static void LoadTalentConfigs(GameData output)
    {
        foreach (var path in Resources.Loader.ListFiles("BinOutput/Talent", "*.json", recursive: true)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            var talents = Resources.Loader.ReadJson<Dictionary<string, List<TalentConfigEntry>>>(path);

            if (talents is null)
                continue;

            foreach (var (name, entries) in talents)
            {
                output.Talents[name] = entries;
            }
        }

        var proudSkills = Resources.Loader.ReadJson<List<ProudSkillResourceData>>(
            "ExcelBinOutput/ProudSkillExcelConfigData.json") ?? [];

        foreach (var proudSkill in proudSkills)
        {
            if (proudSkill.ProudSkillId == 0)
                continue;

            output.ProudSkills[proudSkill.ProudSkillId] = proudSkill;

            if (proudSkill.ProudSkillGroupId != 0 && proudSkill.Level != 0)
                output.ProudSkillsByGroupAndLevel[(proudSkill.ProudSkillGroupId, proudSkill.Level)] = proudSkill;
        }

        var equipAffixes = Resources.Loader.ReadJson<List<EquipAffixResourceData>>(
            "ExcelBinOutput/EquipAffixExcelConfigData.json") ?? [];

        foreach (var affix in equipAffixes)
        {
            if (affix.Id == 0)
                continue;

            var level = affix.AffixId >= affix.Id * 10 ? affix.AffixId - affix.Id * 10 + 1 : 1;
            output.EquipAffixesByGroupAndLevel[(affix.Id, level)] = affix;
        }
    }

    private static void LoadGlobalCombat(GameData output)
    {
        output.GlobalCombat = Resources.Loader.ReadJson<ConfigGlobalCombat>("BinOutput/Common/ConfigGlobalCombat.json") ??
                              new ConfigGlobalCombat();
    }

    private static void ScanServerGlobalValues(byte[] raw, HashSet<uint> hashes)
    {
        try
        {
            using var document = JsonDocument.Parse(raw);
            ScanServerGlobalValues(document.RootElement, hashes);
        }
        catch (JsonException)
        {}
    }

    private static void ScanServerGlobalValues(JsonElement element, HashSet<uint> hashes)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var child in element.EnumerateArray())
                {
                    ScanServerGlobalValues(child, hashes);
                }
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name.StartsWith("SGV_", StringComparison.Ordinal))
                        hashes.Add(AbilityResourceHash.Compute(property.Name));
                    ScanServerGlobalValues(property.Value, hashes);
                }
                break;
            case JsonValueKind.String:
                if (element.GetString() is {} value && value.StartsWith("SGV_", StringComparison.Ordinal))
                    hashes.Add(AbilityResourceHash.Compute(value));
                break;
        }
    }

    /// <summary>
    /// Loads every avatar's <c>ConfigAvatar</c> file, keyed by avatar ID.
    /// </summary>
    private static void LoadAvatars(GameData output)
    {
        var regex = AvatarRegex();
        var stopwatch = Stopwatch.StartNew();

        var configs = Resources.Loader.ListFiles("BinOutput/Avatar", "ConfigAvatar_*.json")
            .Select((string? name, AvatarConfig? config) (p) => {
                var match = regex.Match(p);

                return !match.Success ?
                    (null, null) :
                    (match.Groups["name"].Value, Resources.Loader.ReadJson<AvatarConfig>(p));
            })
            .Where(p => p.config is not null)
            .ToDictionary(p => p.name!, p => p.config!);

        // Resolved in this direction because internal names aren't unique; 12 avatars are "Kate".
        foreach (var (avatarId, avatar) in output.AvatarData)
        {
            if (configs.TryGetValue(avatar.AvatarName, out var config))
            {
                output.Avatars[avatarId] = config;
            }
        }

        Log.Verbose("Loading avatars took {0}ms with {1} entries", stopwatch.ElapsedMilliseconds, output.Avatars.Count);
    }

    /// <summary>
    /// Loads all teleport waypoints for all scenes.
    /// </summary>
    private static void LoadScenePoints(GameData output)
    {
        var regex = ScenePointRegex();
        var stopwatch = Stopwatch.StartNew();

        Resources.Loader.ListFiles("BinOutput/Scene/Point", "scene*_point.json")
            .Select((uint sceneId, ScenePointConfig? data) (p) => {
                var match = regex.Match(p);

                if (!match.Success)
                {
                    return (uint.MinValue, null);
                }

                var sceneId = uint.Parse(match.Groups[1].Value);
                var data = Resources.Loader.ReadJson<ScenePointConfig>(p);

                if (data?.Points is null)
                {
                    return (uint.MinValue, null);
                }

                foreach (var (pointId, point) in data.Points)
                {
                    point.PointId = uint.Parse(pointId);
                    point.SceneId = sceneId;
                }

                return (sceneId, data);
            })
            .Where(d => d.data is not null)
            .Select(d => {
                var data = new Dictionary<uint, PointData>();

                foreach (var (_, point) in d.data!.Points)
                {
                    data.Add(point.PointId, point);
                }

                return (d.sceneId, data);
            })
            .ToList()
            .ForEach(d => output.ScenePoints[d.sceneId] = d.data);

        Log.Verbose("Loading scene points took {0}ms with {1} entries", stopwatch.ElapsedMilliseconds, output.ScenePoints.Count);
    }

    #endregion

    #region Expressions

    [GeneratedRegex(@"ConfigAvatar_(?<name>.+)\.json")]
    private static partial Regex AvatarRegex();

    [GeneratedRegex(@"scene([0-9]+)_point\.json")]
    private static partial Regex ScenePointRegex();

    #endregion
}
