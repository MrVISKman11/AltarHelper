#nullable enable
using ExileCore;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Elements;
using ExileCore.Shared.Cache;
using SharpDX;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Runtime.Versioning;

namespace AltarHelper
{
    [SupportedOSPlatform("windows")]
    public class AltarHelperCore : BaseSettingsPlugin<Settings>
    {
        private const string FILTER_FILE = "Filter.txt";
        public List<FilterEntry> FilterList = new();
        public List<Tuple<RectangleF, Color, int>> RectangleDrawingList = new();
        public List<Tuple<string, Vector2, Color>> TextDrawingList = new();
        // Sound Area
        private readonly object _locker = new object();
        private DateTime _lastPlayed;
        // Sound Area
        private FrameCache<List<LabelOnGround>> LabelCache { get; set; }

        public override bool Initialise()
        {
            Name = "AltarHelper";
            Settings.AltarSettings.RefreshFile.OnPressed += ReadFilterFile;
            ReadFilterFile();
            //PlaySound();

            var defaultProfile = Settings.SelectedProfile?.Value ?? "3.30-STD";
            Settings.LoadProfile(defaultProfile);

            LabelCache = new FrameCache<List<LabelOnGround>>(UpdateAltarLabelList);
            return true;
        }
        private List<LabelOnGround> UpdateAltarLabelList()
        {
            var labels = GameController?.IngameState?.IngameUi?.ItemsOnGroundLabelsVisible;
            if (labels == null || labels.Count == 0) return new List<LabelOnGround>();

            return labels.Where(x => x?.ItemOnGround?.Metadata != null &&
                (x.ItemOnGround.Metadata.Contains("TangleAltar") ||
                 x.ItemOnGround.Metadata.Contains("CleansingFireAltar"))).ToList();
        }
        private void ReadFilterFile()
        {
            var path = $"{DirectoryFullName}\\{FILTER_FILE}";
            if (File.Exists(path))
            {
                ReadFile();
            }
            else
                CreateFilterFile();
        }

        private void CreateFilterFile()
        {
            var path = $"{DirectoryFullName}\\{FILTER_FILE}";
            if (File.Exists(path)) return;
            using (var streamWriter = new StreamWriter(path, true))
            {
                streamWriter.WriteLine("//Name|Weight|Choice");
                streamWriter.WriteLine("#Good");
                //  streamWriter.WriteLine("");
                streamWriter.WriteLine("Drops (1–3) additional Scarab|10|Boss");
                streamWriter.WriteLine("#Bad");
                streamWriter.WriteLine("");
                streamWriter.Close();
            }
        }

        private void ReadFile()
        {
            FilterList.Clear();
            List<string> lines = File.ReadAllLines($"{DirectoryFullName}\\{FILTER_FILE}").ToList();
            bool isGood = true;

            List<string> listAux = new List<string>();
            foreach (string line in lines)
            {
                if (line.Length < 4 || line.StartsWith("//")) continue;

                if (line.StartsWith("#Bad"))
                {
                    isGood = false;
                    continue;
                }
                if (line.StartsWith("#Good"))
                {
                    isGood = true;
                    continue;
                }

                string[] splitLine = line.Split('|');
                var mod = splitLine[0];

                if (mod.Length <= 0 || splitLine[1].Length <= 0) continue;

                FilterEntry filter = new()
                {
                    Mod = mod.Contains('(') && mod.Contains(')') ?
                        Regex.Replace(mod, @"\([^()]*\)", "#") :
                        Regex.Replace(mod, @"(\d+)(?:.\d)|\d+", "#"),
                    Weight = int.Parse(splitLine[1]),
                    IsUpside = isGood,
                    Target = splitLine.Length <= 2 ?
                        AffectedTarget.Any :
                        AltarModsConstants.FilterTargetDict[splitLine[2]],
                };
                FilterList.Add(filter);
                listAux.Add(mod);
            }
        }
        public override void Render()
        {
            foreach (var frame in RectangleDrawingList)
            {
                Graphics.DrawFrame(frame.Item1, frame.Item2, frame.Item3);
            }
            foreach (var text in TextDrawingList)
            {
                Graphics.DrawText(text.Item1, text.Item2, text.Item3);
            }
        }

        public override Job Tick()
        {
            RectangleDrawingList.Clear();
            TextDrawingList.Clear();

            //Mode switching
            if (Settings.AltarSettings.HotkeyMode.PressedOnce())
            {
                Settings.AltarSettings.SwitchMode.Value += 1;
                if (Settings.AltarSettings.SwitchMode.Value == 4) Settings.AltarSettings.SwitchMode.Value = 1;
                switch (Settings.AltarSettings.SwitchMode.Value)
                {
                    case 1:
                        DebugWindow.LogMsg("AltarHelper: Changed to Any Choice");
                        break;
                    case 2:
                        DebugWindow.LogMsg("AltarHelper: Changed to only Minions and Player Choices");
                        break;
                    case 3:
                        DebugWindow.LogMsg("AltarHelper: Changed to only bosses and Players Choices");
                        break;
                }
            }

            if (!CanRun()) return null;

            CompareWeights();

            return null;
        }

        private void CompareWeights()
        {
            try
            {
                var Altars = LabelCache.Value;
                if (Altars == null || Altars.Count <= 0) return;

                foreach (var altarlabel in Altars)
                {
                    if (altarlabel?.Label == null) continue;

                    var topOptionLabel = altarlabel.Label.GetChildAtIndex(0);
                    var bottomOptionLabel = altarlabel.Label.GetChildAtIndex(1);
                    if (topOptionLabel == null || bottomOptionLabel == null) continue;

                    string? topOptionText = GetOptionText(topOptionLabel);
                    string? bottomOptionText = GetOptionText(bottomOptionLabel);

                    if (Settings.DebugSettings.DebugRawText == true)
                    {
                        DebugWindow.LogError($"AltarTop : {topOptionText}");
                        DebugWindow.LogError($"AltarBottom : {bottomOptionText}");
                    }
                    if (string.IsNullOrWhiteSpace(topOptionText) || string.IsNullOrWhiteSpace(bottomOptionText)) continue;

                    Altar altar = new(GetSelectionData(topOptionText), GetSelectionData(bottomOptionText));
                    if (altar == null) continue;

                    if (altar.Top.UpsideWeight == 0 &&
                        altar.Bottom.UpsideWeight == 0 &&
                        altar.Top.DownsideWeight == 0 &&
                        altar.Bottom.DownsideWeight == 0) continue;

                    int topOptionWeight = 0;
                    int bottomOptionWeight = 0;

                    if (Settings.AltarSettings.SwitchMode.Value == 2)
                    {
                        if (altar.Top.Target == AffectedTarget.Minions || altar.Top.Target == AffectedTarget.Player)
                        {
                            topOptionWeight += altar.Top.UpsideWeight - altar.Top.DownsideWeight;
                        }
                        if (altar.Bottom.Target == AffectedTarget.Minions || altar.Bottom.Target == AffectedTarget.Player)
                        {
                            bottomOptionWeight += altar.Bottom.UpsideWeight - altar.Bottom.DownsideWeight;
                        }
                    }
                    else if (Settings.AltarSettings.SwitchMode.Value == 3)
                    {
                        if (altar.Top.Target == AffectedTarget.FinalBoss || altar.Top.Target == AffectedTarget.Player)
                        {
                            topOptionWeight += altar.Top.UpsideWeight - altar.Top.DownsideWeight;
                        }
                        if (altar.Bottom.Target == AffectedTarget.FinalBoss || altar.Bottom.Target == AffectedTarget.Player)
                        {
                            bottomOptionWeight += altar.Bottom.UpsideWeight - altar.Bottom.DownsideWeight;
                        }
                    }
                    else
                    {
                        topOptionWeight += altar.Top.UpsideWeight + altar.Top.DownsideWeight;
                        bottomOptionWeight += altar.Bottom.UpsideWeight + altar.Bottom.DownsideWeight;
                    }

                    if (altar.Top.Target == AffectedTarget.Minions) topOptionWeight += Settings.AltarSettings.MinionWeight.Value;
                    if (altar.Top.Target == AffectedTarget.FinalBoss) topOptionWeight += Settings.AltarSettings.BossWeight.Value;
                    if (altar.Bottom.Target == AffectedTarget.Minions) bottomOptionWeight += Settings.AltarSettings.MinionWeight.Value;
                    if (altar.Bottom.Target == AffectedTarget.FinalBoss) bottomOptionWeight += Settings.AltarSettings.BossWeight.Value;
                    if (altar.Bottom.PlayAlert || altar.Top.PlayAlert) PlaySound();

                    if (Settings.DebugSettings.DebugWeight)
                    {
                        TextDrawingList.Add(new(topOptionWeight.ToString(), new Vector2(topOptionLabel.GetClientRectCache.Center.X - 10, topOptionLabel.GetClientRectCache.Top - 25), Color.Cyan));
                        TextDrawingList.Add(new(bottomOptionWeight.ToString(), new Vector2(bottomOptionLabel.GetClientRectCache.Center.X - 10, bottomOptionLabel.GetClientRectCache.Bottom + 15), Color.Cyan));
                    }

                    if (topOptionWeight < 0 || bottomOptionWeight < 0)
                    {
                        if (topOptionWeight < 0) RectangleDrawingList.Add(new(topOptionLabel.GetClientRectCache, Settings.AltarSettings.BadColor, Settings.AltarSettings.FrameThickness));
                        if (bottomOptionWeight < 0) RectangleDrawingList.Add(new(bottomOptionLabel.GetClientRectCache, Settings.AltarSettings.BadColor, Settings.AltarSettings.FrameThickness));
                    }

                    if (topOptionWeight >= 0 || bottomOptionWeight >= 0)
                    {
                        if (topOptionWeight >= bottomOptionWeight && topOptionWeight > 0) RectangleDrawingList.Add(new(topOptionLabel.GetClientRectCache, GetColor(altar.Top.Target), Settings.AltarSettings.FrameThickness));
                        if (bottomOptionWeight > topOptionWeight && bottomOptionWeight > 0) RectangleDrawingList.Add(new(bottomOptionLabel.GetClientRectCache, GetColor(altar.Bottom.Target), Settings.AltarSettings.FrameThickness));
                    }
                }
            }
            catch (Exception ex)
            {
                DebugWindow.LogError($"AltarHelper CompareWeights Error: {ex.Message}");
            }
        }

        public static string NormalizeModText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;

            // 1. Replace unicode en-dash, em-dash, and minus signs with ascii hyphen
            string result = text.Replace('–', '-').Replace('—', '-').Replace('−', '-');

            // 2. Strip XML/PoE formatting tags <...> (like <link:...>, <hover:...>, <enchanted>, etc.)
            result = Regex.Replace(result, @"<[^>]+>", "");

            // 3. Strip braces and brackets
            result = result.Replace("{", "").Replace("}", "").Replace("[", "").Replace("]", "");

            // 4. Normalize parenthesized/bracketed number ranges or values e.g. (2-4), (1.6-3.2), (15-30) to #
            result = Regex.Replace(result, @"\([^()]*\)", "#");

            // 5. Normalize standalone numeric ranges like 1.6-3.2 or 2-4 to #
            result = Regex.Replace(result, @"\d+(?:\.\d+)?\s*-\s*\d+(?:\.\d+)?", "#");

            // 6. Normalize isolated decimal or integer numbers to #
            result = Regex.Replace(result, @"\d+(?:\.\d+)?", "#");

            // 7. Collapse multiple hashes e.g. #-# or ## or (#) -> #
            result = Regex.Replace(result, @"#\s*-\s*#", "#");
            result = Regex.Replace(result, @"#+", "#");
            result = Regex.Replace(result, @"%+ ", "% ");
            result = Regex.Replace(result, @"%+", "%");

            // 8. Collapse whitespace and trim
            result = Regex.Replace(result, @"\s+", " ").Trim();

            return result;
        }

        private string? GetOptionText(Element? optionLabel)
        {
            if (optionLabel == null) return null;

            var child1 = optionLabel.GetChildAtIndex(1);
            var child0 = optionLabel.GetChildAtIndex(0);

            Element? mainElement = null;
            string? mainText = null;

            // Check standard index 1 first
            if (child1 != null)
            {
                var t = child1.GetText(512);
                if (string.IsNullOrWhiteSpace(t)) t = child1.Text;
                if (!string.IsNullOrWhiteSpace(t))
                {
                    mainElement = child1;
                    mainText = t;
                }
            }

            // Fallback to index 0 if index 1 has no text
            if (string.IsNullOrWhiteSpace(mainText) && child0 != null)
            {
                var t = child0.GetText(512);
                if (string.IsNullOrWhiteSpace(t)) t = child0.Text;
                if (!string.IsNullOrWhiteSpace(t))
                {
                    mainElement = child0;
                    mainText = t;
                }
            }

            // Fallback to self
            if (string.IsNullOrWhiteSpace(mainText))
            {
                var t = optionLabel.GetText(512);
                if (string.IsNullOrWhiteSpace(t)) t = optionLabel.Text;
                if (!string.IsNullOrWhiteSpace(t))
                {
                    mainElement = optionLabel;
                    mainText = t;
                }
            }

            // Check if any child elements contain hyperlink text that was split into sub-elements
            var fullElement = mainElement ?? optionLabel;
            var subTexts = new List<string>();
            CollectSubElementTexts(fullElement, subTexts);

            if (string.IsNullOrWhiteSpace(mainText))
            {
                if (subTexts.Count > 0)
                {
                    return string.Join("\n", subTexts);
                }
                return null;
            }

            // If mainText is present, check if sub-elements have missing tokens (e.g. hyperlinked item names like "Breach Thread")
            foreach (var sub in subTexts)
            {
                var cleanSub = Regex.Replace(sub, @"<[^>]+>", "").Trim();
                if (!string.IsNullOrWhiteSpace(cleanSub) && !mainText.Contains(cleanSub, StringComparison.InvariantCultureIgnoreCase))
                {
                    // Hyperlink text was in a child element and not present in mainText! Append to ensure complete sentence
                    mainText = $"{mainText} {cleanSub}";
                }
            }

            return mainText;
        }

        private static void CollectSubElementTexts(Element? elem, List<string> collected)
        {
            if (elem == null || elem.Children == null) return;
            foreach (var child in elem.Children)
            {
                if (child == null) continue;
                var t = child.GetText(512);
                if (string.IsNullOrWhiteSpace(t)) t = child.Text;
                if (!string.IsNullOrWhiteSpace(t))
                {
                    collected.Add(t.Trim());
                }
                CollectSubElementTexts(child, collected);
            }
        }





        #region helperfunctons
        public bool CanRun()
        {
            if (GameController.Area.CurrentArea.IsHideout ||
                GameController.Area.CurrentArea.IsTown ||
                GameController.IngameState.IngameUi == null ||
                GameController.IngameState.IngameUi.ItemsOnGroundLabelsVisible == null ||
                LabelCache.Value.Count < 1)
                return false;
            return true;
        }


        private void PlaySound()
        {
            lock (_locker)
            {
                if ((DateTime.Now - _lastPlayed).TotalMilliseconds > Settings.AltarSettings.DelayBetweenAlerts && (DateTime.Now - _lastPlayed).TotalMilliseconds > 500)
                {
                     GameController.SoundController.PlaySound(Path.Combine(@"..\Sounds\", Settings.AltarSettings.SoundFile.Value.Replace(".wav","")).Replace('\\', '/'));
                    _lastPlayed = DateTime.Now;
                }
            }
        }


        #endregion

        public Color GetColor(AffectedTarget choice)
        {
            Color color = Color.Transparent;
            if (choice == AffectedTarget.Minions) return Settings.AltarSettings.MinionColor;
            if (choice == AffectedTarget.FinalBoss) return Settings.AltarSettings.BossColor;
            if (choice == AffectedTarget.Player) return Settings.AltarSettings.PlayerColor;

            return color;
        }

        public Selection GetSelectionData(string altarLabelText)
        {
            AffectedTarget target = AffectedTarget.Any;
            List<string> downsides = new();
            List<string> upsides = new();

            if (string.IsNullOrWhiteSpace(altarLabelText))
            {
                return new Selection
                {
                    Target = target,
                    Downsides = downsides,
                    Upsides = upsides
                };
            }

            // Determine Target robustly without throwing exceptions on tags
            if (altarLabelText.Contains("Minion", StringComparison.OrdinalIgnoreCase))
                target = AffectedTarget.Minions;
            else if (altarLabelText.Contains("boss", StringComparison.OrdinalIgnoreCase))
                target = AffectedTarget.FinalBoss;
            else if (altarLabelText.Contains("Player", StringComparison.OrdinalIgnoreCase))
                target = AffectedTarget.Player;

            using (StringReader stringreader = new(altarLabelText))
            {
                string? line;
                bool upsideSectionReached = false;

                while ((line = stringreader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    // Skip the target header line
                    if (line.Contains("gain:", StringComparison.OrdinalIgnoreCase) ||
                        line.Contains("gains:", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // Clean all PoE markup tags <...> and braces/brackets { } [ ]
                    string cleanLine = Regex.Replace(line, @"<[^>]+>", "");
                    cleanLine = cleanLine.Replace("{", "").Replace("}", "").Replace("[", "").Replace("]", "").Trim();

                    if (string.IsNullOrWhiteSpace(cleanLine)) continue;

                    bool isLineUpside =
                        line.Contains("<enchanted>", StringComparison.OrdinalIgnoreCase) ||
                        line.Contains("<rgb(127,127,255)>", StringComparison.OrdinalIgnoreCase) ||
                        line.Contains("<currency>", StringComparison.OrdinalIgnoreCase) ||
                        line.Contains("<unique>", StringComparison.OrdinalIgnoreCase) ||
                        line.Contains("<divination>", StringComparison.OrdinalIgnoreCase) ||
                        line.Contains("<link", StringComparison.OrdinalIgnoreCase) ||
                        line.Contains("<hover", StringComparison.OrdinalIgnoreCase) ||
                        cleanLine.Contains("drops ", StringComparison.OrdinalIgnoreCase) ||
                        cleanLine.Contains("chance to drop", StringComparison.OrdinalIgnoreCase) ||
                        cleanLine.Contains("chance to be Duplicated", StringComparison.OrdinalIgnoreCase) ||
                        cleanLine.Contains("increased Quantity", StringComparison.OrdinalIgnoreCase) ||
                        cleanLine.Contains("increased Rarity", StringComparison.OrdinalIgnoreCase) ||
                        cleanLine.Contains("Thread", StringComparison.OrdinalIgnoreCase) ||
                        cleanLine.Contains("Scarab", StringComparison.OrdinalIgnoreCase);

                    if (isLineUpside)
                    {
                        upsideSectionReached = true;
                    }

                    if (upsideSectionReached)
                    {
                        upsides.Add(cleanLine);
                    }
                    else
                    {
                        downsides.Add(cleanLine);
                    }
                }
            }

            List<FilterEntry> UpsideFilterEntryMatches = new();
            List<FilterEntry> DownsideFilterEntryMatches = new();

            foreach (string entry in upsides)
            {
                var normalizedUpside = NormalizeModText(entry);
                if (Settings.DebugSettings.DebugBuffs) DebugWindow.LogMsg(normalizedUpside);

                var filterentry = GetEntry(normalizedUpside);
                if (filterentry == null || filterentry.Weight == 0)
                {
                    filterentry = GetEntry(entry);
                }
                if (filterentry == null) continue;

                UpsideFilterEntryMatches.Add(filterentry);
                if (Settings.DebugSettings.DebugBuffs) DebugWindow.LogMsg($"Good Mod: {filterentry.Mod}  | Weight {filterentry.Weight}");
            }

            foreach (string entry in downsides)
            {
                var normalizedDownside = NormalizeModText(entry);
                if (Settings.DebugSettings.DebugDebuffs) DebugWindow.LogMsg(entry);

                var filterentry = GetEntry(normalizedDownside);
                if (filterentry == null || filterentry.Weight == 0)
                {
                    filterentry = GetEntry(entry);
                }
                if (filterentry == null) continue;

                DownsideFilterEntryMatches.Add(filterentry);
                if (Settings.DebugSettings.DebugDebuffs) DebugWindow.LogMsg($"Bad Mod: {filterentry.Mod}  | Weight {filterentry.Weight}");
            }

            Selection selection = new()
            {
                Upsides = upsides,
                Downsides = downsides,
                Target = target,

                UpsideWeight = (UpsideFilterEntryMatches.Count > 0) ? UpsideFilterEntryMatches.Sum(x => x.Weight) : 0,
                DownsideWeight = (DownsideFilterEntryMatches.Count > 0) ? DownsideFilterEntryMatches.Sum(x => x.Weight) : 0,
                BuffGood = (UpsideFilterEntryMatches.FirstOrDefault(x => x.IsUpside) != null),
                DebuffGood = (DownsideFilterEntryMatches.FirstOrDefault(x => x.IsUpside) != null),
                PlayAlert = (UpsideFilterEntryMatches.FirstOrDefault(x => x.Alert == true) != null) || (DownsideFilterEntryMatches.FirstOrDefault(x => x.Alert == true) != null)
            };

            return selection;
        }

        public FilterEntry GetEntry(string mod)
        {
            var cleanMod = mod.Replace("%%", "%").Trim();
            var normalizedMod = NormalizeModText(mod);

            var matchedMod = AltarModsConstants.AltarTypes.FirstOrDefault(t =>
            {
                var tClean = t.Id.Replace("%%", "%").Trim();
                var tNorm = NormalizeModText(t.Id);
                var tNameNorm = NormalizeModText(t.Name);

                // Exact or normalized match
                if (t.Id.Equals(mod, StringComparison.InvariantCultureIgnoreCase) ||
                    tClean.Equals(cleanMod, StringComparison.InvariantCultureIgnoreCase) ||
                    tNorm.Equals(normalizedMod, StringComparison.InvariantCultureIgnoreCase) ||
                    tNameNorm.Equals(normalizedMod, StringComparison.InvariantCultureIgnoreCase) ||
                    t.Name.Equals(mod, StringComparison.InvariantCultureIgnoreCase))
                {
                    return true;
                }

                // Substring containment match
                if (tClean.Contains(cleanMod, StringComparison.InvariantCultureIgnoreCase) ||
                    cleanMod.Contains(tClean, StringComparison.InvariantCultureIgnoreCase) ||
                    tNorm.Contains(normalizedMod, StringComparison.InvariantCultureIgnoreCase) ||
                    normalizedMod.Contains(tNorm, StringComparison.InvariantCultureIgnoreCase))
                {
                    return true;
                }

                // Singular vs Plural match (e.g. Thread vs Threads)
                var normSingular = normalizedMod.TrimEnd('s');
                var tNormSingular = tNorm.TrimEnd('s');
                if (normSingular.Equals(tNormSingular, StringComparison.InvariantCultureIgnoreCase) ||
                    normSingular.Contains(tNormSingular, StringComparison.InvariantCultureIgnoreCase) ||
                    tNormSingular.Contains(normSingular, StringComparison.InvariantCultureIgnoreCase))
                {
                    return true;
                }

                return false;
            });

            var canonicalId = matchedMod.Id != null ? matchedMod.Id : mod;

            var modWeight = Settings.GetModTier(canonicalId);
            if (modWeight == 0) modWeight = Settings.GetModTier(cleanMod);
            if (modWeight == 0) modWeight = Settings.GetModTier(normalizedMod);
            if (modWeight == 0 && matchedMod.Id != null) modWeight = Settings.GetModTier(matchedMod.Id.Replace("%%", "%"));
            if (modWeight == 0 && matchedMod.Name != null) modWeight = Settings.GetModTier(matchedMod.Name);

            var modAlert = Settings.GetModAlert(canonicalId) || Settings.GetModAlert(cleanMod) ||
                           Settings.GetModAlert(normalizedMod) ||
                           (matchedMod.Id != null && Settings.GetModAlert(matchedMod.Id.Replace("%%", "%")));

            var modName = string.IsNullOrEmpty(matchedMod.Id) ? normalizedMod : matchedMod.Id;

            var modType = matchedMod.Type;

            FilterEntry filter = new()
            {
                Mod = modName,
                Weight = modWeight,
                IsUpside = modWeight > 0 ? true : false,
                Target = modType == null ?
                    AffectedTarget.Any :
                    (AltarModsConstants.FilterTargetDict.TryGetValue(modType, out var aff) ? aff : AffectedTarget.Any),
                Alert = modAlert
            };
            return filter;
        }

        public class FilterEntry
        {
            public string Mod { get; set; }
            public int Weight { get; set; }
            public AffectedTarget Target { get; set; }
            public bool IsUpside { get; set; }
            public bool? Alert { get; set; } = false;
        }


        public class Altar
        {
            public Selection Top { get; set; }
            public Selection Bottom { get; set; }
            public Altar(Selection top, Selection bottom)
            {
                Top = top;
                Bottom = bottom;
            }
        }

        public class Selection
        {
            public AffectedTarget Target { get; set; }
            public List<string> Downsides { get; set; }
            public List<string> Upsides { get; set; }
            public int UpsideWeight { get; set; }
            public int DownsideWeight { get; set; }
            public bool BuffGood { get; set; }
            public bool DebuffGood { get; set; }
            public bool PlayAlert { get; set; } = false;

        }
    }
}