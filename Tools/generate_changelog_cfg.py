#!/usr/bin/env python3
"""Generate GameData/JustReadTheInstructions/changelog.cfg from CHANGELOG.md.

The .cfg is a KERBALCHANGELOG node used by the optional Kerbal Changelog
mod. It is inert without that mod installed, making this a soft integration.
CHANGELOG.md (Keep a Changelog format) is the single source of truth; this
script is a sync step.

For future thought: is there a better way to do this?
It feels like reinventing the wheel a tiny little bit...
"""

import re
import sys
from pathlib import Path

MOD_NAME = "Just Read The Instructions"
LICENSE = "MIT"
AUTHOR = "Mathieu Relmy"
SHOW_CHANGELOG = "True"
INDENT = "    "

VERSION_RE = re.compile(r"^v?(\d+\.\d+\.\d+(?:\.\d+)?)(.*)$")
DATE_TAIL_RE = re.compile(r"\s*-\s*\d{4}-\d{2}-\d{2}\s*$")
LINK_RE = re.compile(r"\[([^\]]+)\]\([^)]+\)")
VERSION_VALID_RE = re.compile(r"^\d+\.\d+\.\d+(?:\.\d+)?$")

# It's funny how some of those chars will never be used in our changelog
# but oh well, we never know...
NON_ASCII = {
    "‘": "'", "’": "'", "“": '"', "”": '"',
    "–": "-", "—": "-", "→": "->",
}


def clean(text):
    for bad, good in NON_ASCII.items():
        text = text.replace(bad, good)
    text = LINK_RE.sub(r"\1", text)
    text = text.replace("**", "").replace("`", "")
    text = text.replace("//", "/ /")
    return " ".join(text.split()).strip()


def parse(changelog_text):
    versions = []
    current = None
    category = None
    for line in changelog_text.splitlines():
        if line.startswith("## "):
            head = line[3:].strip()
            current = None
            category = None
            if head.lower().startswith("unreleased"):
                continue
            match = VERSION_RE.match(head)
            if not match:
                continue
            rest = match.group(2)
            if rest.startswith("-"):
                continue
            current = {
                "version": match.group(1),
                "name": DATE_TAIL_RE.sub("", rest).strip(),
                "changes": [],
            }
            versions.append(current)
            continue
        if current is None:
            continue
        if line.startswith("### "):
            category = {"category": clean(line[4:].strip()), "subs": []}
            current["changes"].append(category)
            continue
        stripped = line.strip()
        if stripped.startswith("- ") and category is not None:
            sub = clean(stripped[2:])
            if sub:
                category["subs"].append(sub)
    return [v for v in versions if any(c["subs"] for c in v["changes"])]


def render(versions):
    out = ["KERBALCHANGELOG", "{"]
    out.append(f"{INDENT}showChangelog = {SHOW_CHANGELOG}")
    out.append(f"{INDENT}modName = {MOD_NAME}")
    out.append(f"{INDENT}license = {LICENSE}")
    out.append(f"{INDENT}author = {AUTHOR}")
    for version in versions:
        out.append("")
        out.append(f"{INDENT}VERSION")
        out.append(f"{INDENT}{{")
        out.append(f"{INDENT * 2}version = {version['version']}")
        if version["name"]:
            out.append(f"{INDENT * 2}versionName = {version['name']}")
        for change in version["changes"]:
            if not change["subs"]:
                continue
            out.append(f"{INDENT * 2}CHANGE")
            out.append(f"{INDENT * 2}{{")
            out.append(f"{INDENT * 3}change = {change['category']}")
            for sub in change["subs"]:
                out.append(f"{INDENT * 3}subchange = {sub}")
            out.append(f"{INDENT * 2}}}")
        out.append(f"{INDENT}}}")
    out.append("}")
    return "\n".join(out) + "\n"


def validate(text, versions):
    errors = []
    if text.count("{") != text.count("}"):
        errors.append(f"unbalanced braces: {text.count('{')} open, {text.count('}')} close")
    if "//" in text:
        errors.append("output contains '//' (KSP treats it as a comment)")
    if not versions:
        errors.append("no released versions parsed from CHANGELOG.md")
    for version in versions:
        if not VERSION_VALID_RE.match(version["version"]):
            errors.append(f"version '{version['version']}' is not Kerbal-Changelog-parseable")
    return errors


def main(argv):
    repo_root = Path(__file__).resolve().parent.parent
    changelog = Path(argv[1]) if len(argv) > 1 else repo_root / "CHANGELOG.md"
    out_path = (
        Path(argv[2]) if len(argv) > 2
        else repo_root / "GameData" / "JustReadTheInstructions" / "changelog.cfg"
    )

    versions = parse(changelog.read_text(encoding="utf-8"))
    text = render(versions)
    errors = validate(text, versions)
    if errors:
        for error in errors:
            print(f"[gen-changelog] ERROR: {error}", file=sys.stderr)
        return 1

    out_path.write_text(text, encoding="utf-8")
    print(f"[gen-changelog] wrote {len(versions)} versions to {out_path}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
