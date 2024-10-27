import os
import tempfile
from zipfile import ZipFile
import requests
from crowdin_api import CrowdinClient
from lxml import etree as ET

pj = os.path.join
pn = os.path.normpath
script_path = os.path.dirname(os.path.realpath(__file__))
main_path = pn(script_path+"/..")
src_path = pj(main_path, "src")

def request(url):
    resp = requests.get(url, timeout = 30)
    if (resp.status_code == 200):
        return resp.content
    else:
        print("ERROR: " + str(resp.status_code) + " " + resp.reason)
        return None

class FirstCrowdinClient(CrowdinClient):
    TOKEN = os.getenv("crowdin_token")
    PROJECT_ID = os.getenv("crowdin_project_id")

client = FirstCrowdinClient()
build = client.translations.build_project_translation(dict({"skipUntranslatedStrings": True}))["data"]
status = ''
while status != 'finished':
    info = client.translations.check_project_build_status(build['id'])
    status = info['data']['status']
    if (status != 'finished'):
        print(f"Build progress: {info['data']['progress']}")
download_info = client.translations.download_project_translations(build['id'])
url = download_info['data']['url']

os.chdir(main_path)

with tempfile.TemporaryDirectory() as tmpdirname:
    with open(pj(tmpdirname, 'temp.zip'), 'wb') as file:
        file.write(request(url))

    with ZipFile(pj(tmpdirname, "temp.zip"), 'r') as zObject:
        zObject.extractall(pj(tmpdirname))
    
    os.remove(pj(tmpdirname, "temp.zip"))
    xmlns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns_x = "http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns_sys = "clr-namespace:System;assembly=mscorlib"

    NSMAP = {None: xmlns,
            "sys": xmlns_sys,
            "x":  xmlns_x}
    # Copy only needed localizations and rename
    legendary_loc_keys = {}
    with open(pj(script_path, "config", "legendaryLocKeys.txt"),
          "r", encoding="utf-8") as legendary_loc_keys_content:
        for line in legendary_loc_keys_content:
            if line := line.strip():
                legendary_loc_keys[line] = ""
    
    legendary_loc_path = pj(tmpdirname)
    for filename in os.listdir(legendary_loc_path):
        path = os.path.join(legendary_loc_path, filename)
        if os.path.isdir(path):
            continue
        if "legendary" in filename:
            continue
        legendary_loc = ET.parse(pj(legendary_loc_path, filename))
    
        xml_root = ET.Element("ResourceDictionary", nsmap=NSMAP)
        xml_doc = ET.ElementTree(xml_root)
    
        for child in legendary_loc.getroot():
            key = child.get(ET.QName(xmlns_x, "Key"))
            if key in legendary_loc_keys:
                key_text = child.text
                if not key_text:
                    key_text = ""
                key = key.replace("Legendary", "GogOss")
                new_key = ET.Element(ET.QName(xmlns_sys, "String"))
                new_key.set(ET.QName(xmlns_x, "Key"), key)
                new_key.text = key_text.replace("Legendary", "GOG OSS")
                xml_root.append(new_key)
    
        ET.indent(xml_doc, level=0)
    
        with open(pj(src_path, "Localization", filename), "w", encoding="utf-8") as i18n_file:
            i18n_file.write(ET.tostring(xml_doc, encoding="utf-8", xml_declaration=True, pretty_print=True).decode())
