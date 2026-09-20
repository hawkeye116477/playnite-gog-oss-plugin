#!/usr/bin/env python
# pylint: disable=C0103
# pylint: disable=C0301
"""Do this after succesful build with Visual Studio"""
import os
import sys
import get_extension_version
import subprocess
import shutil

pj = os.path.join
pn = os.path.normpath

scriptPath = os.path.dirname(os.path.realpath(__file__))
mainPath = pn(scriptPath + "/..")
compiledPath = pn(sys.argv[1])

version = get_extension_version.run()

with open(pj(compiledPath, "extension.yaml"), 'r', encoding='utf-8') as extManifest:
    data = extManifest.read()
    data = data.replace("_version_", version)
with open(pj(compiledPath, "extension.yaml"), 'w', encoding='utf-8') as extManifest:
    extManifest.write(data)


_VSWHERE = pn('C:/Program Files (x86)/Microsoft Visual Studio/Installer/vswhere.exe')
def find_vcvarsall():
    """Locate vcvarsall.bat for VS, regardless of edition."""
    if os.path.exists(_VSWHERE):
        result = subprocess.run([_VSWHERE, '-latest', '-products', '*',
			 '-requires', 'Microsoft.VisualStudio.Component.VC.Tools.x86.x64', '-property', 'installationPath'], 
             capture_output=True, encoding='oem', check=True)
        install_path = result.stdout.strip()
        if install_path:
            candidate = os.path.join(install_path, 'VC', 'Auxiliary', 'Build', 'vcvarsall.bat')
            if os.path.exists(candidate):
                return candidate
    raise RuntimeError(
		"Could not locate vcvarsall.bat for VS (checked vswhere)"
	)

def get_msvc_env(arch='x64'):
    """Return os.environ copy with MSVC toolchain loaded for the given arch."""
    vcvarsall = find_vcvarsall()
    cmd = f'"{vcvarsall}" {arch} && set'
    result = subprocess.run(cmd, capture_output=True, encoding='oem', shell=True, check=True)
    env = {}
    for line in result.stdout.splitlines():
        if '=' in line:
            k, v = line.split('=', 1)
            env[k.upper()] = v
    return env

env = {}
if os.name == 'nt':
    env = get_msvc_env("x86")

patch_lib_name = "NativeVcdiffPatch"
patch_path = pj(mainPath, "src", patch_lib_name)
os.chdir(patch_path)

if not os.path.exists(pj(compiledPath, f"{patch_lib_name}.dll")):
    # --wipe to regenerate
    subprocess.run(
        ["meson", "setup", "build"],
        env=env,
        check=True,
    )

    subprocess.run(
        ["meson", "compile", "-C", "build"],
        env=env,
        check=True,
    )
    shutil.copy2(pj(patch_path, "build", f"{patch_lib_name}.dll"), compiledPath)
