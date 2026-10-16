"""Package repository runtime files and verify their deployed/archive hashes."""
import argparse
import hashlib
import json
from pathlib import Path
import zipfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
DEPLOYED = Path(r"D:\SteamLibrary\steamapps\common\RimWorld\Mods\RatkinDiningEtiquette")


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def digest(data):
    return hashlib.sha256(data).hexdigest().upper()


def files(root):
    return {p.relative_to(root).as_posix(): p for p in root.rglob("*") if p.is_file()}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--create", action="store_true")
    parser.add_argument("--verify-deployed", action="store_true")
    parser.add_argument("--status", choices=("local-game-validation-candidate", "release-prepared"), default="local-game-validation-candidate")
    args = parser.parse_args()
    about = ET.parse(ROOT / "About/About.xml").getroot()
    version = about.findtext("modVersion")
    require(version == ET.parse(ROOT / "Source/RatkinDiningEtiquette.csproj").getroot().findtext("PropertyGroup/Version"), "Project/About version mismatch")
    require(version == ET.parse(ROOT / "Source/RatkinDiningEtiquette.1.7.csproj").getroot().findtext("PropertyGroup/Version"), "1.7 project/About version mismatch")
    require([n.text for n in about.findall("supportedVersions/li")] == ["1.6", "1.7"], "Supported game versions mismatch")
    load = ET.parse(ROOT / "LoadFolders.xml").getroot()
    require([n.tag for n in load] == ["v1.6", "v1.7"], "Unexpected version load folders")
    for runtime in ("1.6", "1.7"):
        require([n.text for n in load.findall("v" + runtime + "/li")] == [runtime], "Each game version must load exactly its own runtime folder")
    source = {"LoadFolders.xml": ROOT / "LoadFolders.xml"}
    for folder in ("About", "1.6", "1.7"):
        source.update({folder + "/" + name: path for name, path in files(ROOT / folder).items()})
    baseline = json.loads((ROOT / "Releases/RatkinDiningEtiquette-1.0.0.manifest.json").read_text(encoding="utf-8-sig"))
    approved_inventory = {f["path"] for f in baseline["runtimeFiles"]}
    approved_inventory.update("1.7/" + name[4:] for name in list(approved_inventory) if name.startswith("1.6/"))
    require(set(source) == approved_inventory, "Runtime inventory must match baseline plus the matching 1.7 folder")
    for name, path in source.items():
        if name.startswith("1.6/") and name.endswith(".xml"):
            require(path.read_bytes() == source["1.7/" + name[4:]].read_bytes(), "Version content mismatch: " + name)
    require((ROOT / "About/PublishedFileId.txt").read_text().strip() == "3812440999", "Workshop identity changed")
    require((ROOT / "About/Preview.png").stat().st_size < 1_000_000, "Workshop preview too large")
    require({name for name in source if name.endswith(".dll")} == {v + "/Assemblies/RatkinDiningEtiquette.dll" for v in ("1.6", "1.7")}, "Dependency or extra DLL in runtime")
    archive = ROOT / "Releases" / f"RatkinDiningEtiquette-{version}.zip"
    manifest_path = archive.with_suffix(".manifest.json")
    if args.create:
        require(version != "1.0.0", "Never overwrite the published baseline")
        with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as output:
            for name, path in sorted(source.items()):
                output.write(path, "RatkinDiningEtiquette/" + name)
        manifest = {
            "name": about.findtext("name"), "version": version, "status": args.status,
            "rimWorldVersions": ["1.6", "1.7"], "targetFrameworks": {"1.6": "net472", "1.7": "net10.0"}, "packageId": about.findtext("packageId"), "publishedFileId": "3812440999",
            "assemblyVersion": version + ".0", "languages": baseline["languages"],
            "archive": archive.name, "archiveSha256": digest(archive.read_bytes()),
            "runtimeFiles": [{"path": name, "sha256": digest(path.read_bytes())} for name, path in sorted(source.items())],
        }
        manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    manifest = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
    require(manifest["archiveSha256"] == digest(archive.read_bytes()), "Archive hash mismatch")
    require(manifest["version"] == version and manifest["assemblyVersion"] == version + ".0", "Manifest version mismatch")
    require(manifest["rimWorldVersions"] == ["1.6", "1.7"], "Manifest supported-version mismatch")
    expected = {f["path"]: f["sha256"] for f in manifest["runtimeFiles"]}
    require(set(expected) == set(source), "Manifest inventory mismatch")
    with zipfile.ZipFile(archive) as packed:
        require(len(packed.namelist()) == len(source), "Extra or duplicate archive entries")
        require(set(packed.namelist()) == {"RatkinDiningEtiquette/" + name for name in source}, "Wrong archive root or inventory")
        for name, path in source.items():
            require(digest(path.read_bytes()) == expected[name], f"Repository hash mismatch: {name}")
            require(digest(packed.read("RatkinDiningEtiquette/" + name)) == expected[name], f"ZIP hash mismatch: {name}")
    if args.verify_deployed:
        deployed_files = files(DEPLOYED)
        require(set(deployed_files) == set(source), "Deployed inventory mismatch")
        for name, path in deployed_files.items():
            require(digest(path.read_bytes()) == expected[name], f"Deployment hash mismatch: {name}")
        print(f"PASS deployed {version}: {len(source)} files match repository and ZIP.")
    require(digest((ROOT / "Releases/RatkinDiningEtiquette-1.0.0.zip").read_bytes()) == baseline["archiveSha256"], "Published baseline changed")
    for historical_path in (ROOT / "Releases").glob("*.manifest.json"):
        if historical_path == manifest_path:
            continue
        historical = json.loads(historical_path.read_text(encoding="utf-8-sig"))
        historical_archive = ROOT / "Releases" / historical["archive"]
        require(digest(historical_archive.read_bytes()) == historical["archiveSha256"], "Historical archive changed: " + historical_archive.name)
    print(f"PASS {archive.name}: {len(source)} runtime-only files; manifest and all hashes verified; published 1.0.0 preserved.")
    print("SHA256 " + manifest["archiveSha256"])


if __name__ == "__main__":
    main()
