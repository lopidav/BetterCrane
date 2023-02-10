# Example build script for mods. Feel free to modify the calls at the end of the file to fit your needs
# Intended use: copy this script into your_mod_project/scripts/
import os
import shutil
import subprocess
import time
import xml.etree.ElementTree as ET
from pathlib import Path

import psutil

# script configuration: edit these options to fit your setup
# these paths are relative to your project root. absolute paths also work
GAME_ROOT = Path("D:/SteamLibrary/steamapps/common/Stacklands").resolve()
# you probably dont need to touch these
STACKLANDS_EXE = GAME_ROOT / "Stacklands"
MOD_BIN = Path("bin/Debug/netstandard2.0")


def build_mod():
    start_time = time.time()
    p = subprocess.Popen("dotnet build", stdout=subprocess.PIPE, stderr=subprocess.PIPE, shell=True)
    stdout, stderr = p.communicate()
    if p.returncode != 0:
        print(stdout.decode())
        exit(p.returncode)
    print(f"built in {time.time() - start_time:.2f}s")


def launch_stacklands():
    subprocess.Popen(STACKLANDS_EXE)


def kill_stacklands():
    for proc in psutil.process_iter(["name", "pid"]):
        if proc.name() == "Stacklands.exe":
            proc.kill()
            proc.wait()


def sync_folder(src: Path, dst: Path):
    for file in dst.glob("**/*"):
        file_in_src = src / file.relative_to(dst)
        if file.is_dir() and not file_in_src.exists():
            shutil.rmtree(file)
        elif file.is_file() and (not file_in_src.exists() or file.stat().st_mtime < file_in_src.stat().st_mtime):
            os.remove(file)
    for file in src.glob("**/*"):
        file_in_dst = dst / file.relative_to(src)
        if not file_in_dst.exists():
            file_in_dst.parent.mkdir(parents=True, exist_ok=True)
            if file.is_file():
                shutil.copy(file, file_in_dst)
            elif file.is_dir():
                shutil.copytree(file, file_in_dst)


def copy_files():
    MOD_GAME_PATH.mkdir(exist_ok=True)
    shutil.copyfile(MOD_DLL, MOD_GAME_PATH / MOD_DLL.name)



if __name__ == "__main__":
    # this little hack makes the script callable from anywhere
    old_path = Path.cwd()
    try:
        os.chdir(Path(__file__).parent)
        found_csprojs = list(Path(".").glob("*.csproj"))
        if len(found_csprojs) != 1:
            raise RuntimeError("Can't find .csproj file")
        with open(found_csprojs[0], encoding="utf-8") as f:
            root = ET.parse(f).getroot()
            MOD_DLL = MOD_BIN / f"{root.find('./PropertyGroup/AssemblyName').text}.dll"
            MOD_GAME_PATH = Path(__file__).parent / f"{found_csprojs[0].stem}"

        build_mod()
        # kill_stacklands()
        copy_files()
        # launch_stacklands()
    finally:
        os.chdir(old_path)
