#!/usr/bin/env python3
"""b12-w: read the build stamp (BuildInfo.txt, written by Editor/Build.cs) out of a built player, with no Unity.

The stamp is a plain-text Resources asset inside resources.assets ("version=..\\ncommit=..\\ndirty=..\\n...development=..").
Works on a Windows player folder (CriticalMass_Data/resources.assets), a macOS .app (Contents/Resources/Data/resources.assets)
or a folder holding either. Prints one line:
  version=1.0.900 commit=a1b2c3d dirty=0 development=0 netbuild=a1b2c3d
netbuild is what the game sends a room host (OnlineConfig.Build: the commit without "+dirty", or the version when there is no
commit); a console peer that meets this player passes it as -build.
Usage: player-stamp.py PLAYER [--field version|commit|dirty|development|netbuild]   (exit 1 when no stamp is found)
"""
import os
import re
import sys

STAMP = re.compile(rb"version=([^\r\n]*)\r?\ncommit=([^\r\n]*)\r?\ndirty=([^\r\n]*)\r?\n(?:[^\r\n]*\r?\n)*?development=([^\r\n]*)")


def find_assets(root):
    if os.path.isfile(root):
        return root
    for r, _dirs, files in os.walk(root):
        if "resources.assets" in files:
            return os.path.join(r, "resources.assets")
    return None


def read(root):
    path = find_assets(root)
    if path is None:
        return None
    with open(path, "rb") as f:
        m = STAMP.search(f.read())
    if not m:
        return None
    version, commit, dirty, development = (x.decode("utf-8", "replace").strip() for x in m.groups())
    net = commit.split("+", 1)[0]
    return {"version": version, "commit": commit, "dirty": dirty, "development": development,
            "netbuild": net if net else version, "assets": path}


def main(argv):
    if not argv:
        print(__doc__)
        return 64
    s = read(argv[0])
    if s is None:
        print(f"player-stamp: no build stamp found under {argv[0]}", file=sys.stderr)
        return 1
    if "--field" in argv:
        print(s[argv[argv.index("--field") + 1]])
    else:
        print(" ".join(f"{k}={s[k]}" for k in ("version", "commit", "dirty", "development", "netbuild")))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
