import re

with open('poedb_mods.txt', 'r', encoding='utf-8') as f:
    poedb_lines = [l.strip() for l in f.readlines() if l.strip() and not l.startswith('//')]

with open('AltarModsConstants.cs', 'r', encoding='utf-8') as f:
    cs_content = f.read()

known_mods = set()
for match in re.finditer(r'\(\s*"([^"]+)",\s*"(?:[^"\\]|\\.)*",\s*"(?:[^"\\]|\\.)*"\s*\)', cs_content):
    known_mods.add(match.group(1).replace('%%', '%'))

missing = []
for line in poedb_lines:
    if line.startswith("Map boss gains: "):
        line = line.replace("Map boss gains: ", "")
    elif line.startswith("Eldritch Minions gain: "):
        line = line.replace("Eldritch Minions gain: ", "")
    elif line.startswith("Player gains: "):
        line = line.replace("Player gains: ", "")
        
    original = line
    if '(' in line and ')' in line:
        modName = re.sub(r'\([^()]*\)', '#', line)
    else:
        modName = re.sub(r'(?:\d+\.\d+)|\d+', '#', line)
        
    if modName not in known_mods:
        missing.append(f"ORIGINAL: {original} \n-> PARSED AS: {modName}\n")

with open('missing_result.txt', 'w', encoding='utf-8') as f:
    f.write("--- MISSING MODS ---\n")
    for m in missing:
        f.write(m)
