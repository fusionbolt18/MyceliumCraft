# MyceliumCraft: installing the Mycelium part

MyceliumCraft lets you play Mycelium while Minecraft controls your spore. It has two parts: a
mod for Minecraft, and this mod for Mycelium. This file covers the Mycelium part.

## What you need

- Windows.
- Your own copy of Mycelium from Steam.
- This zip, `MyceliumCraft-for-Mycelium.zip`. It contains:
  - **BepInEx 5.4.23.6**, a free mod loader for Unity games, unchanged from
    https://github.com/BepInEx/BepInEx/releases.
  - **MyceliumCraft** (`BepInEx\plugins\MyceliumCraft`).
  - **Mycelium's newer fixes** (`BepInEx\patchers\MyceliumCraft`): code its developers wrote
    after the Steam release (enemy attack, lunge and StinkCloud fixes). It's used in place of the
    game's own code while the game runs; the game's files aren't changed.

## Install

1. In Steam, right-click **Mycelium** → **Manage** → **Browse local files**. A folder opens with
   `Mycelium.exe` in it.
2. Close Mycelium if it's running.
3. Unzip everything from the zip **into that folder**, next to `Mycelium.exe`. When you're done,
   that folder has these new files and folders beside the game's own:
   - `BepInEx`
   - `winhttp.dll`
   - `doorstop_config.ini`
   - `.doorstop_version`
   - `changelog.txt`
   - `MyceliumCraft-INSTALL.txt`
   - `BepInEx-LICENSE-NOTE.txt`

   No game files are replaced.
4. Start Mycelium from Steam as usual. The first start takes a little longer while BepInEx sets
   itself up.

## Check it worked

After starting Mycelium once, open `BepInEx\LogOutput.log` in the game folder. It should have:

```
[Info   :   BepInEx] Loading [MyceliumCraft 0.1.0]
[Info   :MyceliumCraft] MyceliumCraft: starting with scene 'Main Menu'
[Info   :MyceliumCraft] MyceliumCraft: Mycelium's code: the release with the newer commits (d64bf8d9, c5c14682)
```

If Mycelium gets a Steam update, the newer fixes switch themselves off (the last line then says
"the release's own"), so they never mix with a newer game.

The mod writes its own log and settings next to its DLL, in
`BepInEx\plugins\MyceliumCraft\MyceliumCraft.log` and `MyceliumCraft.cfg`. If something goes
wrong, send both logs to whoever gave you the mod.

## Playing

Start Minecraft with the MyceliumCraft Minecraft mod and open a world, then start Mycelium (or
the other way round). The two find each other on their own. Without Minecraft running, Mycelium
plays as normal.

**Your saves:** the mod runs inside your real Mycelium, so it uses your real Mycelium save.

## Turn it off or remove it

- **Turn it off for a while:** rename `winhttp.dll` to `winhttp.dll.off`. Without it, BepInEx
  and the mod don't load.
- **Remove it completely:** delete the files and folders listed in step 3. Steam's "Verify
  integrity of game files" doesn't remove them, because they aren't the game's files.
