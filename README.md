# World Map

An in-game map item that opens a north-up global-coordinate map with **M** while held or equipped. It draws shared
exploration trails, live multiplayer player markers, and ochre markers for nearby places the player has visited.
Zoom and pan controls let players browse beyond the default 6 km view. Route and landmark data is saved by map seed
in `BepInEx/config/WorldMap/`; the multiplayer API sends compact coordinate updates and throttled late-join snapshots.
Rendered map images stay local.

The saved item is cloned from the game's book item and added at low weight to the household miscellaneous loot
categories. It is searchable as `paper_worldmap` in the game's spawn/debug lists. Its held page displays the live map.
Hold or equip it to see the map on the paper in your hand. The in-game HUD lists its controls: **arrows** pan,
**Page Up / Page Down** zoom, **Home** centers, and **M** opens the larger view. In the larger view, drag with the mouse
to pan, use the mouse wheel or buttons to zoom, and press **M** or **Escape** to close it. The item uses standard
inventory and hand placement, with right-hand equip enabled.

Exploration is represented by sampled travel trails rather than surveyed terrain. Positions are sent at one metre
resolution; live markers update once a second, and route samples are sent every 24 metres. The host queues late-join
snapshot packets at a conservative rate. Visited places are detected within 120 metres of the player. Player markers
clear when players leave or the session ends.

## Build

The mod needs TLD Revamp's API assembly and the game's reference assemblies. By default, the project looks for a
`../tld-revamp` checkout. Build the API first, then build this mod:

```sh
dotnet build ../tld-revamp/src/TLDRevamp.Multiplayer/TLDRevamp.Multiplayer.csproj -c Release
dotnet build WorldMap.csproj -c Release
```

If the TLD Revamp checkout is elsewhere, pass its path:

```sh
dotnet build WorldMap.csproj -c Release -p:TLDRevampRepoPath=/path/to/tld-revamp
```

The built `WorldMap.dll` can be installed in `BepInEx/plugins/WorldMap/` beside the game's normal mod dependencies.
