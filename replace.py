import os

cs_path = 'AltarModsConstants.cs'

with open(cs_path, 'r', encoding='utf-8') as f:
    orig = f.read()

hdr_marker = "public static readonly IReadOnlyList<(string Id, string Name, string Type)> AltarTypes = new List<(string, string, string)>"
ftr_marker = "    }\n\n    public enum AffectedTarget"

if hdr_marker in orig and ftr_marker in orig:
    part1 = orig.split(hdr_marker)[0]
    part2 = orig.split(ftr_marker)[1]
    
    with open('categorized_factions.txt', 'r', encoding='utf-8') as f:
        factionsTxt = f.read()
    
    new_array = factionsTxt.split('// C# REPLACEMENT FOR AltarTypes\n')[1]
    
    # We must preserve the Faction type, so the top array definition must be updated to 4 elements.
    # The txt file already has: public static readonly IReadOnlyList<(string Id, string Name, string Type, string Faction)> AltarTypes = new List<(string, string, string, string)>

    final_cs = part1 + new_array + "\n    public enum AffectedTarget" + part2
    
    with open(cs_path, 'w', encoding='utf-8') as f:
        f.write(final_cs)
    print("Replaced!")
else:
    print("Markers not found.")
