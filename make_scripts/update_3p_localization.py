#!/usr/bin/env python
# pylint: disable=C0103
"""Update third-party localization"""
import os
import shutil
from lxml import etree as ET
import git

pj = os.path.join
pn = os.path.normpath

script_path = os.path.dirname(os.path.realpath(__file__))
main_path = pn(pj(script_path, ".."))
third_party_path = pj(main_path, "third_party")
localization_path = pj(third_party_path, "Localization")
src_path = pj(main_path, "src")

GOG_LOC_KEYS = ["LOCSettingsGOGUseGalaxy", "LOCGOGSettingsImportInstalledLabel", 
                "LOCGOGSettingsImportUninstalledLabel", "LOCGOGSettingsConnectAccount", 
                "LOCGOGLoginChecking", "LOCGOGNotLoggedIn", "LOCGOGNotLoggedInError", 
                "LOCGOGAuthenticateLabel",
                "LOCGOGTroubleShootingIssues", "LOCGOGStartUsingClient", 
                "LOCGOGMetadataLanguageLabel", "LOCGOGSettingsUseVerticalCovers", "LOCGOGLoggedIn", "LOCGOGMetadataLanguageLabel"]

PLAYNITE_LOC_KEYS = ["LOCSettingsGeneralLabel", "LOCSettingsAdvancedLabel",
                    "LOCSelectDirectoryTooltip", "LOCSelectFileTooltip", "LOCExecutableTitle"]

LEGENDARY_LOC_KEYS = ["LOCLegendaryLauncherCustomPath", "LOCLegendaryGamesInstallationPath"]

if os.path.exists(localization_path):
    shutil.rmtree(localization_path)
os.makedirs(localization_path)

xmlns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation"
xmlns_x = "http://schemas.microsoft.com/winfx/2006/xaml"
xmlns_sys = "clr-namespace:System;assembly=mscorlib"

NSMAP = {None: xmlns,
        "x":  xmlns_x,
        "sys": xmlns_sys}

# Copy localizations from Legendary
legendary_loc_path = pj(main_path, "..", "playnite-legendary-plugin", "src", "Localization")
for filename in os.listdir(legendary_loc_path):
    path = os.path.join(legendary_loc_path, filename)
    if os.path.isdir(path):
        continue
    legendary_loc = ET.parse(pj(legendary_loc_path, filename))

    comet_i18n_file = pj(src_path, "Localization", f'{filename}')
    if os.path.exists(comet_i18n_file):
        comet_tree = ET.parse(comet_i18n_file)
        comet_tree = comet_tree.getroot()

        for child in comet_tree:
            key = child.get(ET.QName(xmlns_x, "Key"))
            if key:
                key = key.replace("Comet", "Legendary")
                if key in LEGENDARY_LOC_KEYS:
                    comet_tree.remove(child)

        for child in legendary_loc.getroot():
            key = child.get(ET.QName(xmlns_x, "Key"))
            if key in LEGENDARY_LOC_KEYS:
                key_text = child.text
                if not key_text:
                    key_text = ""
                key = key.replace("Legendary", "Comet")
                new_key = ET.Element(ET.QName(xmlns_sys, "String"))
                new_key.set(ET.QName(xmlns_x, "Key"), key)
                new_key.text = key_text.replace("Legendary", "Comet")
                comet_tree.append(new_key)
        ET.indent(comet_tree, level=0)

        with open(pj(src_path, "Localization", f'{filename}'), "w", encoding="utf-8") as i18n_file:
            i18n_file.write(ET.tostring(comet_tree, encoding="utf-8", xml_declaration=True, pretty_print=True).decode())

# Copy localizations from Playnite
for filename in os.listdir(pj(main_path, "..", "PlayniteExtensions", "PlayniteRepo", "source", "Playnite", "Localization")):
    git_repo = git.Repo(
        pj(main_path, "..", "PlayniteExtensions"), search_parent_directories=True)
    commit = git_repo.head.object.hexsha
    source = git_repo.remotes.origin.url.replace(".git", f"/tree/{commit}")
    Playnite_git_repo = git.Repo(pj(main_path, "..", "PlayniteExtensions", "PlayniteRepo"), search_parent_directories=True)
    commit2 = Playnite_git_repo.head.object.hexsha
    source2 = Playnite_git_repo.remotes.origin.url.replace(".git", f"/tree/{commit2}")


    xml_root = ET.Element("ResourceDictionary", nsmap=NSMAP)
    xml_doc = ET.ElementTree(xml_root)

    new_filename = filename
    if filename not in ["LocSource.xaml", "LocalizationKeys.cs", "locstatus.json"]:
        if filename == "en_US.xaml":
            new_filename = "LocSource.xaml"
        playnite_loc = ET.parse(pj(main_path, "..", "PlayniteExtensions",
                                "PlayniteRepo", "source", "Playnite", "Localization", new_filename))
        for child in playnite_loc.getroot():
            key = child.get(ET.QName(xmlns_x, "Key"))
            if key in PLAYNITE_LOC_KEYS:
                key_text = child.text
                if not key_text:
                    key_text = ""
                new_key = ET.Element(ET.QName(xmlns_sys, "String"))
                new_key.set(ET.QName(xmlns_x, "Key"), key.replace("LOC", "LOCComet3P_Playnite"))
                new_key.text = key_text
                xml_root.append(new_key)

    if filename not in ["LocSource.xaml", "LocalizationKeys.cs", "locstatus.json"]:
        gog_loc = ET.parse(pj(main_path, "..", "PlayniteExtensions",
                            "source", "Libraries", "GOGLibrary", "Localization", filename))
        for child in gog_loc.getroot():
            key = child.get(ET.QName(xmlns_x, "Key"))
            if key in GOG_LOC_KEYS:
                key_text = child.text
                if not key_text:
                    key_text = ""
                if key == "LOCSettingsGOGUseGalaxy":
                    key_text = key_text.replace("GOG Galaxy", "Comet")
                    key = key.replace("Galaxy", "Comet")
                new_key = ET.Element(ET.QName(xmlns_sys, "String"))
                new_key.set(ET.QName(xmlns_x, "Key"), key.replace("LOCGOG", "LOCComet3P_GOG").replace("LOCSettingsGOG", "LOCComet3P_GOG"))
                new_key.text = key_text
                xml_root.append(new_key)

        ET.indent(xml_doc, level=0)
        with open(pj(localization_path, filename), "w", encoding="utf-8") as i18n_file:
            i18n_file.write("<?xml version='1.0' encoding='utf-8'?>\n")
            i18n_file.write(
                f'<!--\n  Automatically generated via update_3p_localization.py script using files from {source} and {source2}.\n  DO NOT MODIFY, CUZ IT MIGHT BE OVERWRITTEN DURING NEXT RUN!\n-->\n')
            i18n_file.write(ET.tostring(xml_doc, encoding="utf-8", pretty_print=True).decode())
