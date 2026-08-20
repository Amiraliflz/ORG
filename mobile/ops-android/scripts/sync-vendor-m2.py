#!/usr/bin/env python3
"""Copy ~/.gradle/caches into mobile/ops-android/vendor-m2 for offline builds."""
import os
import shutil
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CACHE = os.path.expanduser("~/.gradle/caches/modules-2/files-2.1")
DEST = os.path.join(ROOT, "vendor-m2")

if not os.path.isdir(CACHE):
    print(f"Gradle cache not found: {CACHE}", file=sys.stderr)
    sys.exit(1)

count = 0
for group_dir in os.listdir(CACHE):
    gpath = os.path.join(CACHE, group_dir)
    if not os.path.isdir(gpath):
        continue
    group = group_dir.replace(".", "/")
    for artifact in os.listdir(gpath):
        apath = os.path.join(gpath, artifact)
        if not os.path.isdir(apath):
            continue
        for version in os.listdir(apath):
            vpath = os.path.join(apath, version)
            if not os.path.isdir(vpath):
                continue
            dest = os.path.join(DEST, group, artifact, version)
            os.makedirs(dest, exist_ok=True)
            for hashdir in os.listdir(vpath):
                hpath = os.path.join(vpath, hashdir)
                if not os.path.isdir(hpath):
                    continue
                for fname in os.listdir(hpath):
                    if not fname.endswith((".jar", ".pom", ".module", ".aar", ".zip")):
                        continue
                    src = os.path.join(hpath, fname)
                    dst = os.path.join(dest, fname)
                    if not os.path.exists(dst):
                        shutil.copy2(src, dst)
                        count += 1

print(f"Synced {count} artifacts to {DEST}")
