import re

with open('poedb_mods.txt', 'r', encoding='utf-8') as f:
    poedb_lines = [l.strip() for l in f.readlines() if l.strip() and not l.startswith('//')]

# Build sets of Eater and Exarch strings
eater_keywords = [
    "Cold", "Lightning", "Chilled", "Shock", "Sap", "Eater", "Ichor", "Exalted", "Regal", 
    "Alteration", "Chromatic", "Jeweller", "Blessed", "Fusing", "Scouring", 
    "Breach", "Delirium", "Legion", "Blight", "Ritual", "Harvest", "Ultimatum", 
    "Abyss", "Expedition", "Betrayal", "Bestiary", "Incursion", "Sulphite", "Kalguuran",
    "Physical Damage Reduction", "Suppress", "Blind", "Maximum Energy Shield", 
    "Grasping Vine", "Punishment", "Frenzy Charge", "Recovery Rate", "Critical Strike Multiplier", 
    "Projectiles are fired in random directions", "Non-Damaging Ailments", "Tentacles", "Attack Speed", "Cast Speed", "Movement Speed"
]

exarch_keywords = [
    "Fire", "Chaos", "Ignite", "Poison", "Ash", "Scorch", "Exarch", "Ember", 
    "Annulment", "Binding", "Horizons", "Unmaking", "Vaal", "Enkindling", "Instilling", 
    "Regret", "Bauble", "Gemcutter", "Meteor", "Influence", "Cartography", "Divination", 
    "Anarchy", "Harbinger", "Miscellaneous", "Beyond", "Torment", "Ambush", "Domination", 
    "Essence", "Reliquary", "Titanic", "Armour", "Evasion Rating", "Consecrated Ground", 
    "Malediction", "Vulnerability", "Flask Charges", "Flask Effect", "Targeted by a Meteor", "Area of Effect"
]

def get_faction(text):
    text_lower = text.lower()
    eater_score = sum(1 for k in eater_keywords if k.lower() in text_lower)
    exarch_score = sum(1 for k in exarch_keywords if k.lower() in text_lower)
    if eater_score > exarch_score: return "Eater"
    elif exarch_score > eater_score: return "Exarch"
    return "Unknown"

with open('AltarModsConstants.cs', 'r', encoding='utf-8') as f:
    cs_content = f.read()

existing_mods = []
for match in re.finditer(r'\(\s*"([^"]+)",\s*"([^"]+)",\s*"([^"]+)"\s*\)', cs_content):
    id_str = match.group(1).replace('%%', '%')
    name_str = match.group(2)
    type_str = match.group(3)
    existing_mods.append((id_str, name_str, type_str))

# Add the two missing mods manually for this processing
existing_mods.append(("#% chance to drop an additional Kalguuran Scarab", "(1.6-3.2)%% chance to drop an additional Kalguuran Scarab", "Minion"))
existing_mods.append(("Final Boss drops # additional Kalguuran Scarabs", "Final Boss drops (2-4) additional Kalguuran Scarabs", "Boss"))
existing_mods.append(("#% chance to drop an additional Titanic Scarab", "(1.6-3.2)%% chance to drop an additional Titanic Scarab", "Minion"))
existing_mods.append(("Final Boss drops # additional Titanic Scarabs", "Final Boss drops (2-4) additional Titanic Scarabs", "Boss"))

final_mods = []
unknowns = []
for mod_id, mod_name, mod_type in existing_mods:
    context = ""
    clean_id = mod_id.replace('#', '')
    for line in poedb_lines:
        clean_line = re.sub(r'\(.*?\)', '', line)
        clean_line = re.sub(r'\d+', '', clean_line)
        if clean_id[:10] in line or clean_line[:10] in clean_id:
            context = line
            break
            
    faction = get_faction(mod_id + " " + context)
    
    if "Divine Orb" in mod_id or "Duplicated" in mod_id or "Item" in mod_id or "Quantity" in mod_id or "Rarity" in mod_id or "Experience" in mod_id or "Chaos Orb" in mod_id:
        faction = "Both"
        
    if faction == "Unknown":
        # manual fallback
        if "Damage Penetrates" in mod_id: faction = "Both"
        if "Item" in mod_id: faction = "Both"
        if "Unique Jewellery" in mod_id: faction = "Exarch"
        if "Cartographer's Chisel" in mod_id: faction = "Exarch" 
        
    final_mods.append((mod_id, mod_name, mod_type, faction))
    if faction == "Unknown":
        unknowns.append(mod_id)

with open('categorized_factions.txt', 'w', encoding='utf-8') as f:
    f.write(f"UNKNOWNS: {len(unknowns)}\n")
    for u in unknowns:
        f.write(" - " + u + "\n")
    
    # Let's generate the C# code replacement
    f.write("\n\n// C# REPLACEMENT FOR AltarTypes\n")
    f.write("public static readonly IReadOnlyList<(string Id, string Name, string Type, string Faction)> AltarTypes = new List<(string, string, string, string)>\n{\n")
    for m in final_mods:
        f.write(f'    ("{m[0].replace("%", "%%")}", "{m[1].replace("%", "%%")}", "{m[2]}", "{m[3]}"),\n')
    f.write("};\n")
