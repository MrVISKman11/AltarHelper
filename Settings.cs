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

            try { if (!Directory.Exists(_profilesDir)) Directory.CreateDirectory(_profilesDir); } catch { }

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
                            ImGui.Combo("##ProfileSelect", ref selectedProfileIndex, profiles.ToArray(), profiles.Count);
                            ImGui.SameLine();
                            if (ImGui.Button("Load Profile"))
                            {
                                if (selectedProfileIndex >= 0 && selectedProfileIndex < profiles.Count)
                                {
                                    var loadPath = Path.Combine(_profilesDir, $"{profiles[selectedProfileIndex]}.json");
                                    if (File.Exists(loadPath))
                                    {
                                        try
                                        {
                                            var json = File.ReadAllText(loadPath);
                                            var loadedData = JsonConvert.DeserializeObject<dynamic>(json);
                                            if (loadedData != null)
                                            {
                                                ModTiers = JsonConvert.DeserializeObject<Dictionary<string, int>>(loadedData.Tiers.ToString()) ?? new Dictionary<string, int>();
                                                ModAlerts = JsonConvert.DeserializeObject<Dictionary<string, bool>>(loadedData.Alerts.ToString()) ?? new Dictionary<string, bool>();
                                            }
                                        } catch { }
                                    }
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
                                        
                                        // First, iterate over all Altar Mods that mention "Scarab"
                                        foreach (var mod in AltarModsConstants.AltarTypes)
                                        {
                                            if (mod.Name.Contains("Scarab", StringComparison.InvariantCultureIgnoreCase))
                                            {
                                                // Extract the base scarab name from the mod text, e.g. "Harvest Scarab"
                                                // Regex match or simple string splitting. 
                                                // "Final Boss drops # additional Harvest Scarabs" -> "Harvest Scarab"
                                                // "(1.6-3.2)% chance to drop an additional Harvest Scarab" -> "Harvest Scarab"
                                                string baseName = "";
                                                var words = mod.Name.Split(' ');
                                                for (int i = 0; i < words.Length; i++)
                                                {
                                                    if (words[i].StartsWith("Scarab", StringComparison.InvariantCultureIgnoreCase))
                                                    {
                                                        if (i > 0)
                                                        {
                                                            string prevWord = words[i - 1];
                                                            if (prevWord.Equals("additional", StringComparison.InvariantCultureIgnoreCase) ||
                                                                prevWord.Equals("Miscellaneous", StringComparison.InvariantCultureIgnoreCase) ||
                                                                prevWord.Equals("drops", StringComparison.InvariantCultureIgnoreCase)) {
                                                                    // For generic "drop an additional Scarab" modes
                                                                    baseName = "Scarab"; 
                                                            } else {
                                                                baseName = $"{prevWord} Scarab";
                                                            }
                                                        }
                                                        else baseName = "Scarab";
                                                        break;
                                                    }
                                                }

                                                if (string.IsNullOrEmpty(baseName)) continue;
                                                if (baseName == "Scarab") continue; // Skip generic catch-all duplication modifiers for now

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
                                    }
                                    ImGui.TableNextColumn();
                                    ImGui.Text(name);
                                    ImGui.TableNextColumn();
                                    ImGui.Text(type);
                                    ImGui.SetNextItemWidth(50);
                                    ImGui.TableNextColumn();
                                    var currentAlertValue = GetModAlert(id);
                                    if (ImGui.Checkbox($"Alert", ref currentAlertValue))
                                    {
                                        ModAlerts[id] = currentAlertValue;
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



        public int GetModTier(string mod)
        {
            return ModTiers.GetValueOrDefault(mod ?? "", 0);
        }

        public Dictionary<string, int> ModTiers = new()
        {
        };

        public bool GetModAlert(string mod)
        {
            return ModAlerts.GetValueOrDefault(mod ?? "", false);
        }
        public Dictionary<string, bool> ModAlerts = new()
        {
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