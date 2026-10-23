#!/usr/bin/env python3
"""World Map with two players, through TLD RevaMP's two-machine test tools: the desktop hosts, the laptop joins over
the relay. Before the join the host walks a route; after it the laptop has that route (the host's snapshot) under the
host's name and a live marker for the host. Then the laptop walks: the host gets its route under the laptop's name.
Both maps keep their own route as their own. The laptop leaves and joins again: no second trail for it on the host.

  TLD_REVAMP=~/tld-revamp tools/worldmap_mp.py [--no-build] [--relay iad|off]
Needs the RevaMP checkout's two-machine environment (TLD_REMOTE_DIR etc.)."""
import os, sys, time, pathlib, shutil, subprocess, re

HERE = pathlib.Path(__file__).resolve().parent.parent
REVAMP = pathlib.Path(os.path.expanduser(os.environ.get("TLD_REVAMP", str(HERE.parent / "tld-revamp")))).resolve()
sys.path.insert(0, str(REVAMP / "tools"))
import game, tldenv
from mplib import Agent, ensure_both, seeded_both, join_both, relay_both
from bench import ssh_base, REMOTE

def arg(name, default): return sys.argv[sys.argv.index(name) + 1] if name in sys.argv else default
RELAY = arg("--relay", "iad")
VERDICTS = []
def verdict(name, ok, detail=""):
    VERDICTS.append((name, ok)); print(f"{'PASS' if ok else 'FAIL'}  {name}  {detail}", flush=True)

def call(side, what):
    try: return str(side("call WorldMap.Probe." + what)["value"])
    except Exception as e: return "ERROR " + str(e)[:300]

def trails(side):
    out = {}
    for part in call(side, "Trails").split("; "):
        if "=" in part: k, v = part.rsplit("=", 1); out[k] = int(v)
    return out

def markers(side): return [p[7:] for p in call(side, "Trails").split("; ") if p.startswith("marker ")]

def install():
    r = subprocess.run(["dotnet", "build", str(HERE / "WorldMap.csproj"), "-c", "Release", "-nologo", "-v", "q",
                        f"-p:TLDRevampRepoPath={REVAMP}"], capture_output=True, text=True)
    if r.returncode != 0 or "0 Error(s)" not in r.stdout: print(r.stdout[-1500:]); sys.exit("building World Map failed")
    dll = HERE / "bin/Release/netstandard2.1/WorldMap.dll"
    for g in (tldenv.GAME_DIR,):
        d = g / "BepInEx/plugins/WorldMap"; d.mkdir(parents=True, exist_ok=True); shutil.copy2(dll, d / "WorldMap.dll")
        for f in (g / "BepInEx/config/WorldMap").glob("seed-*.txt"): f.unlink()
    rgame = tldenv.rsync_path(tldenv.REMOTE_GAME)
    second = tldenv.need_second()
    r = subprocess.run(["rsync", "-a", "-s", "--mkpath", "-e", " ".join(ssh_base()[:-1]), str(dll), f"{second}:{rgame}/BepInEx/plugins/WorldMap/"], capture_output=True, text=True)
    if r.returncode != 0: sys.exit("copying World Map to the laptop failed: " + r.stderr)
    subprocess.run(ssh_base()[:-1] + [second, f'rm -f "{tldenv.REMOTE_GAME}"/BepInEx/config/WorldMap/seed-*.txt'], capture_output=True)

def walk(side, n, dx, dz, pause=1.3):
    p = side("gpos")
    for i in range(1, n + 1):
        side(f"tpground {p['gx'] + dx * i} {p['gz'] + dz * i}"); time.sleep(pause)

os.environ["TLD_REUSE"] = "0"   # fresh games: the World Map files were just copied
install()
L = Agent()
ensure_both("--no-build" not in sys.argv)
with game.ProtectedSession():
    h = seeded_both(L)
    for s_ in (h, L): s_("set TLDRevamp.TestSafety.BlockAutoSave True"); s_("set kaposztaleves.s.settings.god True")
    print(" host before the join:", call(h, "Trails"), flush=True)
    walk(h, 12, 28, 10)
    host_own = trails(h).get("self", 0)
    verdict("host's route recorded alone", host_own >= 10, call(h, "Trails"))
    lap_own0 = trails(L).get("self", 0)
    if RELAY != "off": relay_both(h, L, RELAY)
    if not join_both(h, L): sys.exit("LAPTOP NEVER JOINED")
    L("set TLDRevamp.TestSafety.BlockAutoSave True")
    t0 = time.time(); got = {}
    while time.time() - t0 < 60:
        got = trails(L)
        if any(k.startswith("p:") and v >= host_own for k, v in got.items()): break
        time.sleep(2)
    print(f" laptop after the join ({time.time() - t0:.0f} s):", call(L, "Trails"), flush=True)
    hosts = [k for k, v in got.items() if k.startswith("p:") and v >= host_own]
    verdict("laptop got the host's route under the host's name", len(hosts) == 1 and hosts[0] != "p:Player 0", str(got))
    verdict("laptop's own route stays its own", got.get("self", 0) >= lap_own0 and got.get("self", 0) < host_own, str(got))
    time.sleep(3)
    verdict("laptop shows a live marker for the host", len(markers(L)) == 1, str(markers(L)))
    verdict("host shows a live marker for the laptop", len(markers(h)) == 1, str(markers(h)))
    walk(L, 10, -26, 14)
    time.sleep(3)
    ht = trails(h); print(" host after the laptop walked:", call(h, "Trails"), flush=True)
    laps = [k for k, v in ht.items() if k.startswith("p:") and v >= 8]
    verdict("host got the laptop's route under its name", len(laps) == 1, str(ht))
    verdict("host's own route unchanged by the laptop's", ht.get("self") == host_own or ht.get("self", 0) >= host_own and ht.get("self", 0) < host_own + 3, str(ht))
    # leave and join again: the laptop's new id must not start a second trail on the host
    print(" laptop leaves:", L("mp stop"), flush=True); time.sleep(5)
    verdict("host drops the laptop's marker when it leaves", len(markers(h)) == 0, str(markers(h)))
    if not join_both(h, L, rehost=False): sys.exit("LAPTOP NEVER JOINED AGAIN")
    walk(L, 4, 0, 30); time.sleep(3)
    ht2 = trails(h); print(" host after the rejoin:", call(h, "Trails"), flush=True)
    verdict("rejoin: the laptop's route goes on in the same trail", len([k for k in ht2 if k.startswith("p:")]) == len([k for k in ht if k.startswith("p:")]), str(ht2))
    for side, name in ((h, "host"), (L, "laptop")):
        e = side("errors"); print(f" {name} errors:", str(e)[:400])
    for s_ in (h, L):
        try: s_("mp stop")
        except Exception: pass

print(f"{sum(ok for _, ok in VERDICTS)}/{len(VERDICTS)} passed")
