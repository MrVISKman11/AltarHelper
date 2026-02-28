import urllib.request
from bs4 import BeautifulSoup
import re

req = urllib.request.Request(
    'https://poedb.tw/us/Eldritch_Altar', 
    data=None, 
    headers={
        'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36'
    }
)

f = urllib.request.urlopen(req)
html = f.read().decode('utf-8')

soup = BeautifulSoup(html, 'html.parser')

eater_mods = []
exarch_mods = []

# Poedb usually has "The Eater of Worlds" and "The Searing Exarch" headers or tabs
# Let's just find Tables and their previous headers.
for table in soup.find_all('table'):
    # find previous h4 or h3 to see if it's eater or exarch
    prev = table.find_previous(['h3', 'h4', 'h5'])
    if not prev: continue
    
    table_text = table.get_text()
    faction = "Unknown"
    if "Eater of Worlds" in prev.get_text() or "Eater" in table_text or "Ichor" in table_text or "Cold" in table_text:
        faction = "Eater"
    elif "Searing Exarch" in prev.get_text() or "Exarch" in table_text or "Ember" in table_text or "Fire" in table_text:
        faction = "Exarch"
        
    print(f"[{faction}] Header: {prev.get_text().strip()}")
    # try to see what mods are in this table
    for tr in table.find_all('tr'):
        tds = tr.find_all('td')
        if len(tds) >= 2:
            mod_text = tds[1].get_text(separator=" ", strip=True)
            if faction == "Eater":
                eater_mods.append(mod_text)
            elif faction == "Exarch":
                exarch_mods.append(mod_text)

print(f"Eater mods found: {len(eater_mods)}")
print(f"Exarch mods found: {len(exarch_mods)}")

with open('faction_mods.txt', 'w', encoding='utf-8') as out:
    out.write("EATER\n")
    for m in eater_mods:
        out.write(m + "\n")
    out.write("\nEXARCH\n")
    for m in exarch_mods:
        out.write(m + "\n")
