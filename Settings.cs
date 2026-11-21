#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Windows.Forms;
using ExileCore.Shared.Attributes;
using ExileCore.Shared.Interfaces;
using ExileCore.Shared.Nodes;
using ImGuiNET;
using Newtonsoft.Json;
using SharpDX;


namespace AltarHelper
{
    [SupportedOSPlatform("windows")]
    public class Settings : ISettings
    {




        public ToggleNode Enable { get; set; } = new ToggleNode(false);
        public TextNode SelectedProfile { get; set; } = new TextNode("3.30-STD");
        public AltarSettings AltarSettings { get; set; } = new AltarSettings();
        public DebugSettings DebugSettings { get; set; } = new DebugSettings();

        [JsonIgnore]
        public int _selectedLeagueIndex = 0;
        [JsonIgnore]
        public float _minionMultiplier = 10.0f;
        [JsonIgnore]
        public float _bossMultiplier = 1.0f;
        [JsonIgnore]
        public float _playerMultiplier = 1.0f;

        [JsonIgnore]
        public CustomNode Tribes { get; }

        public Settings()
        {

            var unitFilter = "";
            var profileName = "";
            var selectedProfileIndex = 0;
            var _pluginDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins", "Compiled", "AltarHelper");
            var _profilesDir = Path.Combine(_pluginDir, "Profiles");

            try
            {
                if (!Directory.Exists(_profilesDir)) Directory.CreateDirectory(_profilesDir);
                var sourceProfilesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins", "Source", "AltarHelper", "Profiles");
                if (Directory.Exists(sourceProfilesDir))
                {
                    foreach (var file in Directory.GetFiles(sourceProfilesDir, "*.json"))
                    {
                        var dest = Path.Combine(_profilesDir, Path.GetFileName(file));
                        if (!File.Exists(dest)) File.Copy(file, dest);
                    }
                }
            } catch { }

#pragma warning disable CA1416 // Validar a compatibilidade da plataforma
            Tribes = new CustomNode
            {
                DrawDelegate = () =>
                {
                    if (ImGui.TreeNode("Mods & Weight"))
                    {
                        var profiles = new List<string>();
                        if (Directory.Exists(_profilesDir))
                        {
                            profiles = Directory.GetFiles(_profilesDir, "*.json")
                                .Select(Path.GetFileNameWithoutExtension)
                                .Where(x => !string.IsNullOrEmpty(x))
                                .Select(x => x!)
                                .ToList();
                        }
                        
                        ImGui.InputTextWithHint("##ProfileName", "New Profile Name", ref profileName, 100);
                        ImGui.SameLine();
                        if (ImGui.Button("Save Profile") && !string.IsNullOrWhiteSpace(profileName))
                        {
                            var savePath = Path.Combine(_profilesDir, $"{profileName}.json");
                            var profileData = new { Tiers = ModTiers, Alerts = ModAlerts };
                            File.WriteAllText(savePath, JsonConvert.SerializeObject(profileData, Formatting.Indented));
                            profileName = ""; // clear input
                        }

                        if (profiles.Count > 0)
                        {
                            var targetProfile = SelectedProfile?.Value ?? "3.30-STD";
                            var foundIdx = profiles.IndexOf(targetProfile);
                            if (foundIdx >= 0)
                            {
                                selectedProfileIndex = foundIdx;
                            }
                            else if (selectedProfileIndex >= profiles.Count || selectedProfileIndex < 0)
                            {
                                selectedProfileIndex = 0;
                            }

                            if (ImGui.Combo("##ProfileSelect", ref selectedProfileIndex, profiles.ToArray(), profiles.Count))
                            {
                                if (selectedProfileIndex >= 0 && selectedProfileIndex < profiles.Count && SelectedProfile != null)
                                {
                                    SelectedProfile.Value = profiles[selectedProfileIndex];
                                }
                            }
                            ImGui.SameLine();
                            if (ImGui.Button("Load Profile"))
                            {
                                if (selectedProfileIndex >= 0 && selectedProfileIndex < profiles.Count)
                                {
                                    var chosen = profiles[selectedProfileIndex];
                                    if (SelectedProfile != null) SelectedProfile.Value = chosen;
                                    LoadProfile(chosen);
                                }
                            }
                        }
                        ImGui.Separator();

                        ImGui.Text("PoE.Ninja Auto-Generator");
                        var leagues = new string[] { "Keepers", "Hardcore Keepers", "Standard", "Hardcore" };
                        // Note: Using static arrays / vars outside the UI loop correctly is needed to persist values between draws.
                        // Setting generic static fields or using the dictionary would be safer, but capturing locals from Settings() might be enough since it's instantiated once.
                        ImGui.Combo("League##Ninja", ref _selectedLeagueIndex, leagues, leagues.Length);
                        ImGui.InputFloat("Minion Multiplier##Ninja", ref _minionMultiplier);
                        ImGui.InputFloat("Boss Multiplier##Ninja", ref _bossMultiplier);
                        ImGui.InputFloat("Player Multiplier##Ninja", ref _playerMultiplier);

                        if (ImGui.Button("Generate PoE.Ninja Profile"))
                        {
                            string targetLeague = leagues[_selectedLeagueIndex];
                            float minMult = _minionMultiplier;
                            float bossMult = _bossMultiplier;
                            float playMult = _playerMultiplier;
                            string saveDir = _profilesDir;

                            System.Threading.Tasks.Task.Run(async () =>
                            {
                                try
                                {
                                    using var client = new System.Net.Http.HttpClient();
                                    var response = await client.GetStringAsync($"https://poe.ninja/api/data/itemoverview?league={targetLeague}&type=Scarab");
                                    var ninjaData = JsonConvert.DeserializeObject<dynamic>(response);

                                    if (ninjaData?.lines != null)
                                    {
                                        // Clear all prior ModTiers to ensure a clean slate, or let existing non-scarab weights persist?
                                        // Let existing weights persist, just overwrite Scarabs so player can configure everything else separately.
                                        
                                        // First, iterate over all Altar Mods that mention "Scarab" or "Thread"
                                        foreach (var mod in AltarModsConstants.AltarTypes)
                                        {
                                            if (mod.Name.Contains("Scarab", StringComparison.InvariantCultureIgnoreCase) || mod.Name.Contains("Thread", StringComparison.InvariantCultureIgnoreCase))
                                            {
                                                string baseName = "";
                                                var words = mod.Name.Split(' ');
                                                for (int i = 0; i < words.Length; i++)
                                                {
                                                    if (words[i].StartsWith("Scarab", StringComparison.InvariantCultureIgnoreCase) || words[i].StartsWith("Thread", StringComparison.InvariantCultureIgnoreCase))
                                                    {
                                                        string itemType = words[i].StartsWith("Scarab", StringComparison.InvariantCultureIgnoreCase) ? "Scarab" : "Thread";
                                                        if (i > 0)
                                                        {
                                                            string prevWord = words[i - 1];
                                                            if (prevWord.Equals("additional", StringComparison.InvariantCultureIgnoreCase) ||
                                                                prevWord.Equals("Miscellaneous", StringComparison.InvariantCultureIgnoreCase) ||
                                                                prevWord.Equals("drops", StringComparison.InvariantCultureIgnoreCase)) {
                                                                    baseName = itemType; 
                                                            } else {
                                                                baseName = $"{prevWord} {itemType}";
                                                            }
                                                        }
                                                        else baseName = itemType;
                                                        break;
                                                    }
                                                }

                                                if (string.IsNullOrEmpty(baseName)) continue;
                                                if (baseName == "Scarab" || baseName == "Thread") continue; // Skip generic catch-all duplication modifiers for now

                                                // Find all ninja lines containing this base name
                                                float totalChaos = 0;
                                                int matchCount = 0;

                                                foreach (var line in ninjaData.lines)
                                                {
                                                    string nameStr = line.name.ToString();
                                                    // This matches "Harvest Scarab", "Harvest Scarab of Doubling", etc.
                                                    if (nameStr.Contains(baseName, StringComparison.InvariantCultureIgnoreCase))
                                                    {
                                                        totalChaos += (float)line.chaosValue;
                                                        matchCount++;
                                                    }
                                                }

                                                if (matchCount > 0)
                                                {
                                                    float avgChaos = totalChaos / matchCount;
                                                    
                                                    float mult = 1.0f;
                                                    if (mod.Type.Equals("Minion", StringComparison.InvariantCultureIgnoreCase)) mult = minMult;
                                                    else if (mod.Type.Equals("Boss", StringComparison.InvariantCultureIgnoreCase)) mult = bossMult;
                                                    else if (mod.Type.Equals("Player", StringComparison.InvariantCultureIgnoreCase)) mult = playMult;

                                                    ModTiers[mod.Id] = (int)(avgChaos * mult);
                                                }
                                            }
                                        }

                                        var savePath = Path.Combine(saveDir, $"Ninja_{targetLeague}.json");
                                        var profileData = new { Tiers = ModTiers, Alerts = ModAlerts };
                                        File.WriteAllText(savePath, JsonConvert.SerializeObject(profileData, Formatting.Indented));
                                    }
                                }
                                catch { }
                            });
                        }
                        ImGui.Separator();

                        ImGui.InputTextWithHint("##UnitFilter", "Filter", ref unitFilter, 100);

                        Action<string, string> DrawTable = (faction, targetType) =>
                        {
                            var filtered = AltarModsConstants.AltarTypes.Where(t =>
                                (t.Faction == faction || t.Faction == "Both" || t.Faction == "Unknown") &&
                                t.Type.Equals(targetType, StringComparison.InvariantCultureIgnoreCase) &&
                                t.Name.Contains(unitFilter, StringComparison.InvariantCultureIgnoreCase)
                            ).ToList();

                            if (filtered.Count == 0) return;

                            if (ImGui.BeginTable($"UnitConfig_{faction}_{targetType}", 4, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.Borders))
                            {
                                ImGui.TableSetupColumn("Weight", ImGuiTableColumnFlags.WidthFixed, 200);
                                ImGui.TableSetupColumn("Mod");
                                ImGui.TableSetupColumn("Type");
                                ImGui.TableSetupColumn("Audio Alert");
                                ImGui.TableHeadersRow();

                                foreach (var (id, name, type, modFaction) in filtered)
                                {
                                    ImGui.PushID($"unit{id}{faction}{targetType}");
                                    ImGui.TableNextRow(ImGuiTableRowFlags.None);
                                    ImGui.TableNextColumn();
                                    ImGui.SetNextItemWidth(200);
                                    var currentValue = GetModTier(id);
                                    if (ImGui.InputInt($"", ref currentValue))
                                    {
                                        ModTiers[id] = currentValue;
                                        ModTiers[id.Replace("%%", "%")] = currentValue;
                                        ModTiers[id.Replace("%", "%%")] = currentValue;
                                    }
                                    ImGui.TableNextColumn();
                                    ImGui.Text(name.Replace('\u2013', '-').Replace('\u2014', '-'));
                                    ImGui.TableNextColumn();
                                    ImGui.Text(type);
                                    ImGui.SetNextItemWidth(50);
                                    ImGui.TableNextColumn();
                                    var currentAlertValue = GetModAlert(id);
                                    if (ImGui.Checkbox($"Alert", ref currentAlertValue))
                                    {
                                        ModAlerts[id] = currentAlertValue;
                                        ModAlerts[id.Replace("%%", "%")] = currentAlertValue;
                                        ModAlerts[id.Replace("%", "%%")] = currentAlertValue;
                                    }

                                    ImGui.PopID();
                                }

                                ImGui.EndTable();
                            }
                        };

                        if (ImGui.TreeNode("Eater of Worlds"))
                        {
                            if (ImGui.TreeNode("Final Boss")) { DrawTable("Eater", "Boss"); ImGui.TreePop(); }
                            if (ImGui.TreeNode("Eldritch Minions")) { DrawTable("Eater", "Minion"); ImGui.TreePop(); }
                            if (ImGui.TreeNode("Player")) { DrawTable("Eater", "Player"); ImGui.TreePop(); }
                            ImGui.TreePop();
                        }

                        if (ImGui.TreeNode("Searing Exarch"))
                        {
                            if (ImGui.TreeNode("Final Boss")) { DrawTable("Exarch", "Boss"); ImGui.TreePop(); }
                            if (ImGui.TreeNode("Eldritch Minions")) { DrawTable("Exarch", "Minion"); ImGui.TreePop(); }
                            if (ImGui.TreeNode("Player")) { DrawTable("Exarch", "Player"); ImGui.TreePop(); }
                            ImGui.TreePop();
                        }

                        ImGui.TreePop();
                    }
                }
            };
#pragma warning restore CA1416 // Validar a compatibilidade da plataforma

        }



        public void LoadProfile(string? profileName)
        {
            if (string.IsNullOrWhiteSpace(profileName)) return;
            var _pluginDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins", "Compiled", "AltarHelper");
            var _profilesDir = Path.Combine(_pluginDir, "Profiles");
            var loadPath = Path.Combine(_profilesDir, $"{profileName}.json");
            if (!File.Exists(loadPath))
            {
                var sourcePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins", "Source", "AltarHelper", "Profiles", $"{profileName}.json");
                if (File.Exists(sourcePath))
                {
                    try { File.Copy(sourcePath, loadPath, true); } catch { }
                    loadPath = sourcePath;
                }
            }
            if (File.Exists(loadPath))
            {
                try
                {
                    var json = File.ReadAllText(loadPath);
                    var loadedData = JsonConvert.DeserializeObject<dynamic>(json);
                    if (loadedData != null)
                    {
                        if (loadedData.Tiers != null)
                        {
                            ModTiers = JsonConvert.DeserializeObject<Dictionary<string, int>>(loadedData.Tiers.ToString()) ?? new Dictionary<string, int>();
                        }
                        if (loadedData.Alerts != null)
                        {
                            ModAlerts = JsonConvert.DeserializeObject<Dictionary<string, bool>>(loadedData.Alerts.ToString()) ?? new Dictionary<string, bool>();
                        }
                    }
                }
                catch { }
            }
        }

        public int GetModTier(string mod)
        {
            if (string.IsNullOrEmpty(mod)) return 0;
            if (ModTiers.TryGetValue(mod, out var val)) return val;
            var normalized = mod.Replace("%%", "%").Trim();
            if (ModTiers.TryGetValue(normalized, out val)) return val;
            var doublePercent = mod.Replace("%", "%%").Trim();
            if (ModTiers.TryGetValue(doublePercent, out val)) return val;

            var normMod = AltarHelperCore.NormalizeModText(mod);
            if (ModTiers.TryGetValue(normMod, out val)) return val;
            if (ModTiers.TryGetValue(normMod.Replace("%", "%%"), out val)) return val;

            foreach (var kvp in ModTiers)
            {
                var kNorm = kvp.Key.Replace("%%", "%").Trim();
                if (normalized.Contains(kNorm, StringComparison.InvariantCultureIgnoreCase) ||
                    kNorm.Contains(normalized, StringComparison.InvariantCultureIgnoreCase) ||
                    normMod.Contains(kNorm, StringComparison.InvariantCultureIgnoreCase) ||
                    kNorm.Contains(normMod, StringComparison.InvariantCultureIgnoreCase))
                {
                    return kvp.Value;
                }

                // Singular vs Plural check
                if (kNorm.TrimEnd('s').Equals(normalized.TrimEnd('s'), StringComparison.InvariantCultureIgnoreCase) ||
                    kNorm.TrimEnd('s').Equals(normMod.TrimEnd('s'), StringComparison.InvariantCultureIgnoreCase))
                {
                    return kvp.Value;
                }
            }

            return 0;
        }

        public Dictionary<string, int> ModTiers = new()
        {
            // Tier 1: Jackpot Currency
            { "Final Boss drops # additional Divine Orbs", 1000000000 },
            { "#%% chance to drop an additional Divine Orb", 1000000000 },
            { "#% chance to drop an additional Divine Orb", 1000000000 },

            // Tier 2: Duplication Modifiers (Threads 4th, Unique Items last)
            { "Basic Currency Items dropped by slain Enemies have #%% chance to be Duplicated", 100000 },
            { "Basic Currency Items dropped by slain Enemies have #% chance to be Duplicated", 100000 },
            { "Maps dropped by slain Enemies have #%% chance to be Duplicated", 90000 },
            { "Maps dropped by slain Enemies have #% chance to be Duplicated", 90000 },
            { "Divination Cards dropped by slain Enemies have #%% chance to be Duplicated", 80000 },
            { "Divination Cards dropped by slain Enemies have #% chance to be Duplicated", 80000 },
            { "Threads dropped by slain Enemies have #%% chance to be Duplicated", 70000 },
            { "Threads dropped by slain Enemies have #% chance to be Duplicated", 70000 },
            { "Unique Items dropped by slain Enemies have #%% chance to be Duplicated", 60000 },
            { "Unique Items dropped by slain Enemies have #% chance to be Duplicated", 60000 },

            // Tier 3: Item Rarity (above Thread mods)
            { "#%% increased Rarity of Items found in this Area", 50000 },
            { "#% increased Rarity of Items found in this Area", 50000 },

            // Tier 4: Grand Eldritch (below dupe mods, no alerts)
            { "Final Boss drops # additional Grand Eldritch Embers", 40000 },
            { "Final Boss drops # additional Grand Eldritch Ember", 40000 },
            { "#%% chance to drop an additional Grand Eldritch Ember", 40000 },
            { "#% chance to drop an additional Grand Eldritch Ember", 40000 },
            { "Final Boss drops # additional Grand Eldritch Ichors", 0 },
            { "Final Boss drops # additional Grand Eldritch Ichor", 0 },
            { "#%% chance to drop an additional Grand Eldritch Ichor", 0 },
            { "#% chance to drop an additional Grand Eldritch Ichor", 0 },
            { "Final Boss drops # additional Greater Eldritch Ichors", 0 },
            { "Final Boss drops # additional Greater Eldritch Ichor", 0 },
            { "#%% chance to drop an additional Greater Eldritch Ichor", 0 },
            { "#% chance to drop an additional Greater Eldritch Ichor", 0 },
            { "Final Boss drops # additional Lesser Eldritch Ichors", 0 },
            { "Final Boss drops # additional Lesser Eldritch Ichor", 0 },
            { "#%% chance to drop an additional Lesser Eldritch Ichor", 0 },
            { "#% chance to drop an additional Lesser Eldritch Ichor", 0 },

            // Tier 5: Priority Threads (Legion, Abyss, Bestiary)
            { "Final Boss drops # additional Legion Threads", 30000 },
            { "#%% chance to drop an additional Legion Thread", 30000 },
            { "#% chance to drop an additional Legion Thread", 30000 },
            { "Final Boss drops # additional Abyss Threads", 30000 },
            { "#%% chance to drop an additional Abyss Thread", 30000 },
            { "#% chance to drop an additional Abyss Thread", 30000 },
            { "Final Boss drops # additional Bestiary Threads", 30000 },
            { "#%% chance to drop an additional Bestiary Thread", 30000 },
            { "#% chance to drop an additional Bestiary Thread", 30000 },

            // Tier 6: Other Threads (Breach, Blight, Ritual, Incursion, Expedition, Betrayal, Ultimatum)
            { "Final Boss drops # additional Breach Threads", 20000 },
            { "#%% chance to drop an additional Breach Thread", 20000 },
            { "#% chance to drop an additional Breach Thread", 20000 },
            { "Final Boss drops # additional Blight Threads", 20000 },
            { "#%% chance to drop an additional Blight Thread", 20000 },
            { "#% chance to drop an additional Blight Thread", 20000 },
            { "Final Boss drops # additional Ritual Threads", 20000 },
            { "#%% chance to drop an additional Ritual Thread", 20000 },
            { "#% chance to drop an additional Ritual Thread", 20000 },
            { "Final Boss drops # additional Incursion Threads", 20000 },
            { "#%% chance to drop an additional Incursion Thread", 20000 },
            { "#% chance to drop an additional Incursion Thread", 20000 },
            { "Final Boss drops # additional Expedition Threads", 20000 },
            { "#%% chance to drop an additional Expedition Thread", 20000 },
            { "#% chance to drop an additional Expedition Thread", 20000 },
            { "Final Boss drops # additional Betrayal Threads", 20000 },
            { "#%% chance to drop an additional Betrayal Thread", 20000 },
            { "#% chance to drop an additional Betrayal Thread", 20000 },
            { "Final Boss drops # additional Ultimatum Threads", 20000 },
            { "#%% chance to drop an additional Ultimatum Thread", 20000 },
            { "#% chance to drop an additional Ultimatum Thread", 20000 },

            // Legacy Scarabs (kept for backward compatibility with older profile saves)
            { "Final Boss drops # additional Breach Scarabs", 100000 },
            { "#%% chance to drop an additional Breach Scarab", 100000 },
            { "#% chance to drop an additional Breach Scarab", 100000 },
            { "Final Boss drops # additional Harvest Scarabs", 100000 },
            { "#%% chance to drop an additional Harvest Scarab", 100000 },
            { "#% chance to drop an additional Harvest Scarab", 100000 },
            { "Final Boss drops # additional Ambush Scarabs", 100000 },
            { "#%% chance to drop an additional Ambush Scarab", 100000 },
            { "#% chance to drop an additional Ambush Scarab", 100000 },
            { "Final Boss drops # additional Kalguuran Scarabs", 100000 },
            { "#%% chance to drop an additional Kalguuran Scarab", 100000 },
            { "#% chance to drop an additional Kalguuran Scarab", 100000 },
            { "Final Boss drops # additional Anarchy Scarabs", 100000 },
            { "#%% chance to drop an additional Anarchy Scarab", 100000 },
            { "#% chance to drop an additional Anarchy Scarab", 100000 },
            { "Final Boss drops # additional Ultimatum Scarabs", 100000 },
            { "#%% chance to drop an additional Ultimatum Scarab", 100000 },
            { "#% chance to drop an additional Ultimatum Scarab", 100000 },
            { "Final Boss drops # additional Cartography Scarabs", 100000 },
            { "#%% chance to drop an additional Cartography Scarab", 100000 },
            { "#% chance to drop an additional Cartography Scarab", 100000 },
            { "Final Boss drops # additional Bestiary Scarabs", 100000 },
            { "#%% chance to drop an additional Bestiary Scarab", 100000 },
            { "#% chance to drop an additional Bestiary Scarab", 100000 },
            { "Scarabs dropped by slain Enemies have #%% chance to be Duplicated", 20000 },
            { "Scarabs dropped by slain Enemies have #% chance to be Duplicated", 20000 }
        };

        public bool GetModAlert(string mod)
        {
            if (string.IsNullOrEmpty(mod)) return false;
            if (ModAlerts.TryGetValue(mod, out var val)) return val;
            var normalized = mod.Replace("%%", "%").Trim();
            if (ModAlerts.TryGetValue(normalized, out val)) return val;
            var doublePercent = mod.Replace("%", "%%").Trim();
            if (ModAlerts.TryGetValue(doublePercent, out val)) return val;

            var normMod = AltarHelperCore.NormalizeModText(mod);
            if (ModAlerts.TryGetValue(normMod, out val)) return val;
            if (ModAlerts.TryGetValue(normMod.Replace("%", "%%"), out val)) return val;

            foreach (var kvp in ModAlerts)
            {
                var kNorm = kvp.Key.Replace("%%", "%").Trim();
                if (normalized.Contains(kNorm, StringComparison.InvariantCultureIgnoreCase) ||
                    kNorm.Contains(normalized, StringComparison.InvariantCultureIgnoreCase) ||
                    normMod.Contains(kNorm, StringComparison.InvariantCultureIgnoreCase) ||
                    kNorm.Contains(normMod, StringComparison.InvariantCultureIgnoreCase))
                {
                    return kvp.Value;
                }

                if (kNorm.TrimEnd('s').Equals(normalized.TrimEnd('s'), StringComparison.InvariantCultureIgnoreCase) ||
                    kNorm.TrimEnd('s').Equals(normMod.TrimEnd('s'), StringComparison.InvariantCultureIgnoreCase))
                {
                    return kvp.Value;
                }
            }

            return false;
        }
        public Dictionary<string, bool> ModAlerts = new()
        {
            { "Final Boss drops # additional Divine Orbs", true },
            { "#%% chance to drop an additional Divine Orb", true },
            { "#% chance to drop an additional Divine Orb", true }
        };


    }




    [Submenu]
    [SupportedOSPlatform("windows")]
    public class AltarSettings
    {
       
        public ButtonNode RefreshFile { get; set; } = new ButtonNode();
        public RangeNode<int> FrameThickness { get; set; } = new RangeNode<int>(2, 1, 5);
        [Menu("Alert sound", "Wav file, localizend in PoeHelper/Sounds/")]
        public TextNode SoundFile { get; set; } = new TextNode("alert.wav");
        [Menu("Delay between Sounds", "1 Second = 1000")]
        public RangeNode<int> DelayBetweenAlerts { get; set; } = new RangeNode<int>(3000, 1000,10000);
        public ColorNode MinionColor { get; set; } = new ColorNode(SharpDX.Color.LightGreen);
        public ColorNode PlayerColor { get; set; } = new ColorNode(SharpDX.Color.LightCyan);
        public ColorNode BossColor { get; set; } = new ColorNode(SharpDX.Color.LightBlue);
        public ColorNode BadColor { get; set; } = new ColorNode(SharpDX.Color.Red);
        [Menu("Switch Mode", "1 = Anny | 2 =  Only Minions and Player | 3 = Only Boss and Players ")]
        public RangeNode<int> SwitchMode { get; set; } = new RangeNode<int>(1, 1, 3); // Any | Only Minions and Player | Only Boss and Player
        [Menu("Minion Weight", "Add this value to minions mod Type")]
        public RangeNode<int> MinionWeight { get; set; } = new RangeNode<int>(0, 0, 100);
        [Menu("Bosss Weight", "Add this value to boss mod Type")]
        public RangeNode<int> BossWeight { get; set; } = new RangeNode<int>(0, 0, 100);
        public HotkeyNode HotkeyMode { get; set; } = new HotkeyNode(Keys.F7);











    }
    [Submenu]
    [SupportedOSPlatform("windows")]
    public class FilterList
    {

        public ListNode ListFilter { get; set; } = new ListNode();




    }

    [Submenu]
    [SupportedOSPlatform("windows")]
    public class DebugSettings
    {
       
        public ToggleNode DebugRawText { get; set; } = new ToggleNode(false);
        public ToggleNode DebugBuffs { get; set; } = new ToggleNode(false);
        public ToggleNode DebugDebuffs { get; set; } = new ToggleNode(false);
        public ToggleNode DebugWeight { get; set; } = new ToggleNode(false);

    }







}