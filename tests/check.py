"""
In-game check of TightBeam: drives a running test game over the ScheduleMCP bridge (Workspace/tools/bridge/s1.py),
runs the Debug console words and reads their log lines. Prints PASS or FAIL per check; exit code 1 on any FAIL.

Needs a Debug build of TightBeam in the slot and a loaded save.

    python TightBeam/tests/check.py --slot mptest
"""
import argparse
import glob
import os
import re
import sys
import time


def _workspace_bridge():
    folder = os.path.dirname(os.path.abspath(__file__))
    for _ in range(6):
        candidate = os.path.join(folder, "Workspace", "tools", "bridge")
        if os.path.isfile(os.path.join(candidate, "s1.py")):
            return candidate
        folder = os.path.dirname(folder)
    raise SystemExit("[check] Workspace/tools/bridge/s1.py not found above " + os.path.dirname(os.path.abspath(__file__)))


sys.path.insert(0, _workspace_bridge())
from s1 import Game  # noqa: E402


class Run:
    def __init__(self, slot):
        for stream in (sys.stdout, sys.stderr):
            stream.reconfigure(encoding="utf-8", errors="replace")
        self.g = Game(slot)
        self.results = []

    def check(self, name, ok, detail=""):
        verdict = "PASS" if ok else "FAIL"
        self.results.append((name, verdict, detail))
        print("%s  %s%s" % (verdict, name, ("  - " + str(detail)) if detail else ""), flush=True)
        return ok

    def summary(self):
        fails = [r for r in self.results if r[1] == "FAIL"]
        print("\n===== %d PASS, %d FAIL" % (len(self.results) - len(fails), len(fails)))
        for name, _, detail in fails:
            print("FAIL  %s  - %s" % (name, detail))
        return 1 if fails else 0

    def log_path(self):
        found = glob.glob(os.path.join(self.g.root, "MelonLoader", "Logs", "*.log"))
        found += glob.glob(os.path.join(self.g.root, "MelonLoader", "Latest.log"))
        return max(found, key=os.path.getmtime) if found else None

    def cmd(self, command, wait=0.8):
        """Run a console word and return the log lines written while it ran."""
        path = self.log_path()
        offset = os.path.getsize(path) if path else 0
        self.g.run(command, settle_ms=int(wait * 1000), max_wait_ms=int(wait * 1000) + 8000)
        now = self.log_path()
        if not now:
            return []
        if now != path:
            offset = 0
        with open(now, "r", encoding="utf-8", errors="replace") as handle:
            handle.seek(offset)
            return handle.read().splitlines()

    def status(self):
        """tbstatus as a dict of key=value pairs plus the failures text."""
        lines = [l for l in self.cmd("tbstatus") if "[Debug] tbstatus:" in l]
        values = {}
        for line in lines:
            body = line.split("[Debug] tbstatus:", 1)[1]
            if body.strip().startswith("failures:"):
                values["failures"] = body.split("failures:", 1)[1].strip()
                continue
            for key, value in re.findall(r"([a-z]+)=(\S+)", body):
                values[key] = value
        return values, lines


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", required=True)
    a = ap.parse_args()
    r = Run(a.slot)

    help_lines = [l for l in r.cmd("tbhelp") if "[Debug]" in l]
    r.check("tbhelp lists the six words", sum(1 for w in ("tbhelp", "tbstatus", "tbon", "tboff", "tbfocus", "tbfx")
                                                if any(w in l for l in help_lines)) == 6, "%d lines" % len(help_lines))

    s, lines = r.status()
    r.check("tbstatus answers", len(lines) == 4, "%d lines" % len(lines))
    r.check("hotbar guard patched once", s.get("prefixes") == "1", "prefixes=%s" % s.get("prefixes"))
    r.check("mod enabled", s.get("enabled") == "True", "enabled=%s" % s.get("enabled"))

    r.cmd("tbon", wait=1.5)
    s, _ = r.status()
    r.check("tbon switches the flashlight on", s.get("on") == "True", "on=%s" % s.get("on"))
    r.check("beam rig exists while on", s.get("rig") == "True", "rig=%s" % s.get("rig"))

    r.cmd("tbfocus 0", wait=2.5)
    narrow, _ = r.status()
    r.cmd("tbfocus 1", wait=2.5)
    wide, _ = r.status()
    try:
        na, wa = float(narrow["angle"]), float(wide["angle"])
        nr, wr = float(narrow["range"]), float(wide["range"])
        r.check("focus 1 gives a wider cone than focus 0", wa > na, "angle %.1f -> %.1f" % (na, wa))
        r.check("focus 0 throws farther than focus 1", nr > wr, "range %.1f -> %.1f" % (nr, wr))
    except (KeyError, ValueError) as e:
        r.check("focus values readable", False, "%s: narrow=%s wide=%s" % (e, narrow, wide))

    for fx in ("blink", "flicker", "pulse", "stop"):
        out = r.cmd("tbfx " + fx, wait=1.0)
        r.check("tbfx %s starts" % fx, any("[Debug] tbfx %s: started" % fx in l for l in out),
                next((l for l in out if "tbfx" in l), "no line"))
        errors = [l for l in out if re.search(r"\[ERROR\]|Exception", l)]
        r.check("tbfx %s logs no error" % fx, not errors, errors[0] if errors else "")

    # The game's own flashlight light must never be lit while TightBeam runs, not even in the frame the lamp
    # switches on. 10 on/off cycles of 20 frames each, sampled after TightBeam's LateUpdate.
    out = r.cmd("tbflashprobe 10", wait=15.0)
    probe = next((l for l in out if "[Debug] tbflashprobe: PASS" in l or "[Debug] tbflashprobe: FAIL" in l), None)
    r.check("no vanilla flashlight frame on toggle", probe is not None and "PASS" in probe,
            probe.split("tbflashprobe:", 1)[1].strip() if probe else "no result line within 15 s")

    time.sleep(1.0)
    r.cmd("tboff", wait=1.5)
    s, _ = r.status()
    r.check("tboff switches the flashlight off", s.get("on") == "False", "on=%s" % s.get("on"))
    r.check("no contained failures", s.get("failures") == "no contained failures", s.get("failures"))

    sys.exit(r.summary())


if __name__ == "__main__":
    main()
