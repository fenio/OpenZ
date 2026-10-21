# How to play OpenZ

Capture **flags** to take whole territories: every building and gun in a territory changes sides with its flag. The
more territory you hold, the faster your fort and factories build. Factories keep building the unit you chose.
Any robot can board an empty vehicle or gun, and snipers can shoot a driver out of his seat.

**Winning:** get a robot through the gate into the enemy fort, or destroy the fort. Either one eliminates that team.
A team with no robots, vehicles or guns left also loses its fort.

## Main menu

| Key | Action |
|---|---|
| Arrows / W A S D | Move the focus |
| Enter / Space | Confirm |
| Esc / Backspace | Back |
| ← / → (level select) | Pick the level |
| Enter (level select) | Deploy |
| T (level select) | HIGH SCORES: your ten best victories per level and difficulty |
| C (level select) | Difficulty: **Easy** (every unit unlocked; the CPU waits 45 s, thinks slower and attacks with smaller groups), **Medium** (the default: the Zod CPU, your factories two tech levels up) or **Hard** (the Zod CPU, Z's build lists, and a missile cannon on a front tower of each fort) |
| H (level select) | Head start on/off (off by default): level 2 adds 2 light tanks, 2 jeeps and a medium tank at your fort, level 17 an MLRS and a heavy tank. This is an OpenZ addition. |

Options (in the main menu and the pause menu, on four tabs: MAIN, GAME, GRAPHICS, ADVANCED): volumes, HUD scale,
fullscreen, edge scroll, health bars, screen shake, calm screen, graphics quality and more.

## Camera

| Input | Action |
|---|---|
| W A S D / arrow keys | Scroll the map (W A S D pause while Ctrl or Alt is held, so Ctrl+A etc. stay orders) |
| Mouse at the screen edge | Edge scroll (can be turned off in Options) |
| Middle mouse drag | Drag the view |
| Mouse wheel | Smooth zoom, from a close tactical view out to the whole map |
| Minimap, left click / drag | Jump the view there |

## Selecting

| Input | Action |
|---|---|
| Left click | Select a unit (a click on a squad member selects its squad) |
| Left drag | Box select |
| Shift + click / drag | Add units to the selection or remove them |
| Double-click | Every one of your units of that type on screen |
| Ctrl + R / V / C / A | All robots / all vehicles / all guns / all robots and vehicles |
| R / V / G | Jump to your next robot / vehicle / gun |
| Ctrl + 1 … 9, 0 | Save the selection as a group |
| 1 … 9, 0 | Select that group. Press again to move the camera to it |

## Orders (right mouse button)

| Input | Action |
|---|---|
| Right click | Context order: move (attack-move unless the unit is already next to enemies), attack, capture a flag, board an empty vehicle or gun, enter the enemy fort through its gate, repair at a repair station, crane repair, pick up grenades from a crate |
| Ctrl + right click | Force attack-move |
| Ctrl + right click on an empty vehicle, gun or building | Attack an empty vehicle, gun or building |
| Alt + right click | Plain move (no attack-move) |
| Shift + right clicks | Queue waypoints (shown as markers); releasing Shift sends them |
| Z + right click | Only the selected unit closest to the target obeys |
| Right click on the minimap | Same orders, sent to that spot on the map |

## Buildings and production

| Input | Action |
|---|---|
| Left click your fort or a factory | Open its production window. You can also use the factory list or a "manufactured" message |
| Click a unit tile | Build it now. The last unit in the queue keeps repeating |
| Right click / Shift + click a unit tile | Add it to the queue (up to 5; click an entry to remove it) |
| SET RALLY / CLEAR (window) | Set or clear the factory's rally point. Shift chains several points; Esc cancels |
| Right click on the map with the window open and no units selected | Set a rally point (Shift chains several) |
| B | Factory list |
| C | Place a stored gun (Esc or right click cancels) |
| Space | Jump to the last message's event |
| F10 | Hide or show the HUD |
| U | Hide or show the lower interface panels |
| - / + | Game speed: slower / faster |
| Esc | Pause menu (resume, options, controls, restart, quit). It also closes the production window or cancels a tool |

A production window shows the build time using Zod's formula. Each flag you own makes production faster; damage to
the building makes it slower.

## Command line and capture kit (for recording; normal players never need it)
| Option / key | What it does |
|---|---|
| `-level L02` / `-level L17` | Start straight in that level (skips the menu) |
| `-take` | Recording run: music off, Easy CPU and the L02 head start preselected in the menu, exactly 60 fps |
| `-demo` | The CPU plays both sides |
| `-cinematic` | Start in the low fort-to-fort flyover camera |
| `-maptop` | Render a 4096 px top-down map of the level into a `captures` folder next to the game, then quit |
| `-showcase` or Ctrl+Shift+F12 | Enable the capture kit keys below |
| F8 | Demo mode on/off (CPU vs CPU) |
| F9 | Cinematic camera: orbit, chase, flyover, crane-up, back to the RTS camera |
| F10 | Hide / show the HUD |
| F11 | Pause the CPU players only |
| `+` / `-` | Game speed 1x / 2x / 4x |
| `[` / `]` | Cinematic camera slower / faster |
| N | A full day/night cycle in 60 s (again: normal clock) |
| F7, then 1-9, 0 or `-` | Showcase scenes staged with the real simulation: 1 vehicles manoeuvre, 2 driver snipe + steal, 3 rocks and sandbags blasted, 4 flag capture, 5 blue fort falls, 6 bot vs bot 4x, 7 lone robot walks into the blue fort, 8 robot line-up, 9 explosion sizes (turret toss), 0 building breaks apart, `-` weapon line-up |

[Back to the main page](../README.md)
