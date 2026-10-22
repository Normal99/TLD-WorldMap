#!/usr/bin/env python3
"""World Map in single player, through TLD RevaMP's test tools and debug bridge: the item spawned, picked up and held
in the right hand (the sheet on screen, its printed side to the eye, no book behind it), the map's own view opened
and closed (cursor free while open, locked after), routes drawn while walking, the route file saved, the item stored,
taken out and dropped. Screenshots go to the game copy's BepInEx/tldrevamp-evidence.

  TLD_REVAMP=~/tld-revamp tools/worldmap_sp.py [--no-build] [--keep]
Needs the test environment of the RevaMP checkout (TLD_DIR, TLD_TEST_COMPAT, TLD_BRIDGE_PORT for a copy)."""
import os, sys, time, pathlib, shutil, subprocess, re

HERE = pathlib.Path(__file__).resolve().parent.parent
REVAMP = pathlib.Path(os.path.expanduser(os.environ.get("TLD_REVAMP", str(HERE.parent / "tld-revamp")))).resolve()
sys.path.insert(0, str(REVAMP / "tools"))
import game, tldenv
from bench import Bridge

VERDICTS = []
def verdict(name, ok, detail=""):
    VERDICTS.append((name, ok)); print(f"{'PASS' if ok else 'FAIL'}  {name}  {detail}", flush=True)

def fresh_routes():
    # a test world starts unexplored: the routes of an earlier run on the same seed would be loaded
    for f in (tldenv.GAME_DIR / "BepInEx/config/WorldMap").glob("seed-*.txt"): f.unlink()

def build_and_install():
    r = subprocess.run(["dotnet", "build", str(HERE / "WorldMap.csproj"), "-c", "Release", "-nologo", "-v", "q",
                        f"-p:TLDRevampRepoPath={REVAMP}"], capture_output=True, text=True)
    if r.returncode != 0 or "0 Error(s)" not in r.stdout: print(r.stdout[-1500:]); sys.exit("building World Map failed")
    dst = tldenv.GAME_DIR / "BepInEx/plugins/WorldMap"; dst.mkdir(parents=True, exist_ok=True)
    shutil.copy2(HERE / "bin/Release/netstandard2.1/WorldMap.dll", dst / "WorldMap.dll")

def probe(b, what="State"):
    try: return str(b("call WorldMap.Probe." + what)["value"])
    except Exception as e: return "ERROR " + str(e)[:300]

def num(s, key):
    m = re.search(re.escape(key) + r" (-?[\d.]+)", s); return float(m.group(1)) if m else None

def viewport(s):
    m = re.search(r"viewport x (-?[\d.]+)\.\.(-?[\d.]+) y (-?[\d.]+)\.\.(-?[\d.]+)", s)
    return tuple(float(x) for x in m.groups()) if m else None

def shot(b, name): print(" shot:", b("shot " + name).get("name"))

def sheet_checks(label, s):
    vp = viewport(s); faces = num(s, "faces")
    print(f" {label}:", s)
    verdict(f"{label}: sheet drawn", "sheet visible True" in s, s[-160:])
    inside = vp is not None and vp[0] > -0.02 and vp[1] < 1.02 and vp[2] > -0.02 and vp[3] < 1.02 and "behind False" in s
    verdict(f"{label}: whole sheet inside the view", inside, str(vp))
    if "hand" in label:   # carried (picked up) it sits at the game's hold point, smaller; read in the hand or the M view
        verdict(f"{label}: sheet big enough to read", vp is not None and (vp[3] - vp[2]) > 0.25, str(vp))
    verdict(f"{label}: printed side faces the eye", faces is not None and faces > 0.6, f"faces {faces}")

with game.ProtectedSession():
    if "--no-build" not in sys.argv:
        build_and_install(); game.build()
    game.quit_game(); fresh_routes(); game.launch(); game.newgame()
    b = Bridge(); time.sleep(3)
    log = (tldenv.GAME_DIR / "BepInEx/LogOutput.log").read_text(errors="replace")
    verdict("mod loaded", "World Map loaded" in log, "")
    verdict("item registered", "Map item" not in log or "not registered" not in log, "")

    p0 = b("gpos"); print(" at", p0)
    items = probe(b, "Items"); print(" items at the start:", items)
    verdict("a new game has a map in the start house", items.count("sheets 1") == 1, items)
    shot(b, "wm-start")
    if "sheets 1" not in items:
        print(" spawn:", b("devspawn 9400")); time.sleep(3)
        items = probe(b, "Items"); print(" items:", items)
    verdict("one map item, one sheet, book hidden", items.count("sheets 1") == 1 and "stock renderers on 0" in items, items)
    # as a player does: in front of the eyes (one lying further than dropDist from the hold point drops at once)
    print(" bring:", probe(b, "Bring")); print(" pick up:", b("mp pickupnear 3")); time.sleep(2)
    s = probe(b); verdict("picked up", "holding picked" in s, s[:80])
    sheet_checks("picked up", s); shot(b, "wm-picked")

    print(" grip:", b("call mainscript.s.player.RightHandGrip")); time.sleep(2)
    s = probe(b); verdict("in the right hand", "holding hand" in s, s[:80])
    sheet_checks("in hand", s); shot(b, "wm-hand")

    s = probe(b, "Open"); time.sleep(1.5); s = probe(b)
    verdict("M view opens, cursor free", "open True" in s and "crsrLocked False" in s and "cursor None" in s, s[:160])
    shot(b, "wm-open")
    print(" zoom:", probe(b, "Zoom 0.3")); time.sleep(1); shot(b, "wm-open-zoom")
    probe(b, "Zoom 3.33")
    s = probe(b, "Close"); time.sleep(1.5); s = probe(b)
    verdict("M view closes, cursor locked again", "open False" in s and "crsrLocked True" in s, s[:160])

    # walk 400 m in 25 m steps: the route grows, the map gets ink
    s = probe(b); before = num(s, "my points") or 0; ink0 = num(s, "ink") or 0
    gx, gz = p0["gx"], p0["gz"]
    for i in range(1, 17):
        b(f"tpground {gx + 25 * i} {gz + 6 * i}"); time.sleep(1.3)
    time.sleep(2)
    s = probe(b); after = num(s, "my points") or 0
    print(" after the walk:", s)
    verdict("route points added while walking", after >= before + 10, f"{before} -> {after}")
    verdict("the route is drawn on the map", (num(s, "ink") or 0) > ink0 + 10, f"ink {ink0} -> {num(s, 'ink')}")
    shot(b, "wm-hand-route")
    probe(b, "Save")
    seed = s.split("world ")[1].split()[0]
    f = tldenv.GAME_DIR / f"BepInEx/config/WorldMap/{seed}.txt"
    lines = f.read_text().splitlines() if f.exists() else []
    verdict("route file saved", sum(l.startswith("T|") for l in lines) >= after, f"{f.name}: {len(lines)} lines")

    # inventory: switch to the next slot (the game's scroll-wheel path) and back
    slot = b("get mainscript.s.player.selectedInv")["value"]
    inv = b(f"call mainscript.s.player.InvSwitchTo {(int(slot) + 1) % 4}"); time.sleep(1.5)
    s = probe(b); print(" stored:", inv, "|", s[:80])
    verdict("stored in the inventory", "holding none" in s, s[:80])
    print(" unstore:", b(f"call mainscript.s.player.InvSwitchTo {slot}")); time.sleep(1.5)
    s = probe(b); verdict("taken out into the hand again", "holding hand" in s, s[:80])
    sheet_checks("back in hand", s)

    b("call mainscript.s.player.RightHandRelease True"); time.sleep(1)
    print(" drop:", b("dropheld")); time.sleep(8)
    items = probe(b, "Items"); print(" dropped:", items)
    y = float(items.split(" at (")[1].split(",")[1]) if " at (" in items else -999
    g = b(f"ground {float(items.split(' at (')[1].split(',')[0])} {float(items.split(' at (')[1].split(',')[2].split(')')[0])}") if " at (" in items else {}
    verdict("dropped sheet rests on the ground, book hidden", abs(y - g.get("y", 1e9)) < 0.3 and "stock renderers on 0" in items, f"{items} | ground {g.get('y')}")
    errs = b("errors"); print(" errors:", str(errs)[:600])
    verdict("no World Map exceptions", "WorldMap" not in str(errs), "")

    # a new game on the same seed: a blank map (the walked one set aside, not deleted)
    game.newgame(); b = Bridge(); time.sleep(6)
    t = probe(b, "Trails"); print(" new game, same seed:", t)
    pts = sum(int(x) for x in re.findall(r"=(\d+)", t))
    kept = list((tldenv.GAME_DIR / "BepInEx/config/WorldMap").glob(f"{seed}-before-*.txt"))
    verdict("a new game on the same seed starts with a blank map", pts <= 3, t[:200])
    verdict("the old map was set aside", len(kept) >= 1, ", ".join(k.name for k in kept))
    if "--keep" not in sys.argv: game.quit_game()

print(f"{sum(ok for _, ok in VERDICTS)}/{len(VERDICTS)} passed")
