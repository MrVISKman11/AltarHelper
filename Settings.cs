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