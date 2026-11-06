# Landscape: formats, tools, prior art and legal position

A snapshot as of 6 November 2026, not a spec. It informs the open decisions in `docs/core.md`.
Claims marked _(unconfirmed)_ came from search summaries, inference or secondhand reports. Licences
and repository activity were checked against GitHub on the day. Nothing here was read from either
game's files except the install layout noted under "Riley's installs".

## Summary

- Nobody has publicly ported Dark Souls's world into Elden Ring. The closest precedent is
  InfernoPlus's Morrowind port to Elden Ring. Its converter, JortPob, reads the player's Morrowind
  install and writes an Elden Ring mod at build time. It is public on GitHub with no licence, so it
  is all rights reserved: we can read it for ideas but copy nothing from it.
- Build-time conversion is what every comparable project does. No project was found that translates
  another game's files live inside a running FromSoftware game.
- me3 (Mod Engine 3) is the mod loader to target. It is maintained, Apache-2.0, supports Proton
  officially, blocks official servers by default and supports a separate save file.
- The hard problems are Havok collision and navmesh. Dark Souls: Remastered uses Havok 2015.
  Elden Ring uses Havok 2018, whose physics system changed fundamentally. No open tool converts
  between them, and the known working route depends on proprietary Havok tools (see Collision).
- The main format libraries are copyleft. SoulsFormatsNEXT is GPL-3.0, and soulstruct and
  soulstruct-havok declare GPL-3.0-or-later. Depending on them makes the tool GPL-3.0.
- Several community tools ship data taken from the games: archive keys, file-name dictionaries,
  paramdefs and vanilla dumps. To keep Age of Gold clean we must not depend on that data. We must
  read it or derive it from the player's installs instead.

## Riley's installs

Both are Steam installs under `/mnt/storage/SteamLibrary/steamapps/common/`, run under Proton:
Dark Souls: Remastered at build 10943698, and Elden Ring at build 25827938 with Shadow of the
Erdtree. Remastered ships its files as loose folders (`map`, `chr`, `obj`, `param`, `paramdef`,
`event`, `script`, `msg`, `mtd`, `sfx`, `sound` and others), with no outer archive to unpack. Its
audio uses FMOD. Elden Ring ships encrypted `Data0`–`Data3` and `DLC` archives (`.bhd` headers and
`.bdt` data) beside `regulation.bin`.

## Formats

| Family | Remastered | Elden Ring | Gap |
|---|---|---|---|
| Containers | BND3 binders, zlib DCX compression | BND4 inside encrypted BHD5/BDT archives; Oodle (KRAK) DCX, with zstd for DLC files | Medium; libraries cover both |
| Models | FLVER2 | FLVER2 at a later version; materials point to MATBIN | Medium: vertex layouts and materials |
| Textures | TPF with DDS | TPF with DDS | Low _(unconfirmed)_ |
| Materials | MTD | MATBIN, new in Elden Ring | High: a mapping to Elden Ring's shaders |
| Map layout | MSB1 | MSBE | High: different entity types and fields |
| Collision | Havok 2015, `hkp` physics | Havok 2018, `hknp` physics | Very high; see Collision |
| Navmesh | NVM, with MCG/MCP route graphs | Havok navmesh (`nvmhkt`) | Very high: must be regenerated |
| Animation | Havok 2015 animation; TAE events | Havok 2018 animation; TAE | Medium: soulstruct-havok converts the animation data |
| Params | PARAM, with paramdefs shipped in the install's `paramdef` folder | PARAM inside AES-encrypted `regulation.bin` | High: different schemas |
| Event scripts | EMEVD, Dark Souls instruction set | EMEVD, Elden Ring instruction set | High: instructions differ |
| AI | Lua _(unconfirmed)_ | HKS, Havok Script _(unconfirmed)_ | High |
| Talk | ESD | ESD | Medium to high: command IDs differ _(unconfirmed)_ |
| Text | FMG | FMG | Low |
| Effects | FFX | FXR | Very high |
| Sound | FMOD (FSB/FEV) | Wwise (BNK/WEM) | Very high: new Wwise media needs Wwise Authoring |
| Lighting | Lightmaps (BTAB), point lights (BTL) | Elden Ring's own lighting | Unknown: no write-up found |

Elden Ring map IDs: the open world is `m60_XX_YY_ZZ` tiles, legacy dungeons are `m10`–`m19`
(`m10` is Stormveil), and catacombs and caves start at `m30`. Dark Souls's maps are also `m10`–`m18`.
So map IDs collide, and Lordran's maps must be assigned Elden Ring slots, which fits replacing the
host's world. How to register new map IDs, and how to replace the world map menu, is undocumented.

Sources: SoulsFormatsNEXT's format classes and `FORMATS.md`, soulsmodding.com's format and map
reference pages, soulstruct-havok's per-game modules, and the EMEDF tables at
soulsmods.github.io/emedf.

## Collision and navmesh

- Grimrukh's soulstruct-blender README says Havok's collision system changed radically in 2014
  (from Bloodborne on). Without the Havok SDK he cannot generate the newer bounding-volume trees, so
  his collision export stops at Remastered.
- JortPob builds Elden Ring collision by exporting meshes to OBJ and passing them through bundled
  Windows executables. One is a Havok asset converter (`AssetCc2`, presumably from the proprietary
  Havok SDK); the others are community `obj2fsnp` and `hknp2fsnp` tools. We must not ship or
  require the proprietary Havok tool unless its licence is established.
- JortPob builds navmesh with ERNavmeshGen (Nordgaren, no licence). It calls the Havok code inside
  the player's own Elden Ring executable. That pattern fits our principles: it uses the player's own
  copy and ships nothing of the game. It might work for collision as well _(unconfirmed)_.
- HKLib (MIT, Havok 2018 read/write, work in progress) and ER_HKX2Navmesh (MIT) are the open
  building blocks.
- Collision and navmesh are where the walking skeleton will succeed or fail. They should be the
  first thing the research stage proves.

## Libraries and tools

| Name | Language | Licence | Use to us | Ships game-derived data |
|---|---|---|---|---|
| SoulsFormatsNEXT | C# | GPL-3.0, active | Read/write for nearly every format, both games | Has regulation key constants in the SoulsFormats lineage _(unconfirmed for NEXT)_; loads Oodle from a path you give it |
| Smithbox | C# | MIT (contains a GPL SoulsFormats fork) | Reference editor for maps, params, text | Yes: file-name dictionaries, Paramdex, keys |
| WitchyBND | C# | GPL-3.0, active | Unpack and repack | Yes: Elden Ring archive key, Paramdex |
| UXM Selective Unpack | C# | None | Unpacking Elden Ring | Yes: file-name dictionaries |
| soulstruct | Python | GPL-3.0-or-later (declared, no LICENSE file) | Strong Remastered support: EMEVD, ESD, MSB, params, Lua | The sister repo soulstruct-vanilla is vanilla game dumps: never use it |
| soulstruct-havok | Python | GPL-3.0-or-later (declared) | Havok 2010–2018 read/write; animation conversion to 2018 | Animation compression uses a closed-source `CompressAnim.exe` |
| HKLib | C# | MIT | Havok 2018 read/write | No _(unconfirmed)_ |
| ER_HKX2Navmesh | C# | MIT | Elden Ring navmesh from HKX | _(unconfirmed)_ |
| ERNavmeshGen | C# | None | Navmesh through the game's own Havok code | Uses the player's executable |
| Paramdex | XML | None | Paramdefs and row names | Yes: the Dark Souls defs are FromSoftware's shipped files |
| rewwise | Rust | MIT/Apache-2.0 with a no-AI-training clause | Wwise bank unpack/repack for Elden Ring | _(unconfirmed)_ |
| me3 | Rust | Apache-2.0, active | The mod loader | No |
| ModEngine 2 | C++ | MIT, archived July 2024 | Superseded by me3 | No |
| JortPob | C# | None | Reference: a whole Morrowind-to-Elden-Ring pipeline | Bundles game-adjacent resources and third-party binaries |

Oodle: the libraries load `oo2core_*.dll` from the player's Elden Ring install rather than bundling
it. Whether its licence allows that is unknown; it is common practice.

## Keys, dictionaries and paramdefs

- Elden Ring's archive headers are RSA-encrypted, and `regulation.bin` is AES-encrypted. The tools
  embed keys that were extracted from the game. The keys are small but still game-derived.
- Elden Ring's archives store only hashes of file names. Tools ship name lists compiled from the
  game.
- Remastered's paramdefs are in the player's install, so we read them from there. Elden Ring's
  paramdefs are not shipped with the game. Paramdex's are reconstructed _(unconfirmed)_. This needs
  a decision.
- For a clean design: read keys out of the player's own executable at build time, rather than
  committing them; hash the paths our own code names, rather than shipping dictionaries; and read
  Remastered's paramdefs from the install.

## Mod loading and play

- me3 profiles (`.me3`, TOML) list package folders of loose override files and native DLLs. No
  repacking is needed, but an override replaces a whole binder file. A `savefile` setting keeps the
  mod's saves apart. Official servers are blocked unless `start_online` is set.
- On Linux, me3 installs with its shell installer and launches through Proton via `me3 launch`.
- Overhauls ship one `regulation.bin`; only one can be active.

## Legal position

- Elden Ring's EULA forbids reverse engineering, modification and derivative works, and
  unauthorised third-party tools. It has no clause specific to mods. Enforcement has targeted online
  play, not offline modding _(unconfirmed)_.
- FromSoftware has said it does not actively oppose Seamless Co-op. A Bandai Namco producer
  jokingly asked a Tekken-in-Elden-Ring mod to stop, with no takedown. In 2014 a DMCA notice hit
  DSfix and was withdrawn as an agency error. No takedown of a FromSoftware cross-game port was
  found.
- The projects that survive ship nothing from the games and read the player's copy: OpenMW, Ship of
  Harkinian, Skyblivion, and InfernoPlus's Morrowind port. The SM64 PC port drew DMCA notices
  aimed largely at hosted builds that bundled the game. This is tolerance, not a legal safe harbour.
- Ownership checks in practice are install detection or file hashes. No project checks the
  purchase itself.
- Nexus forbids uploading assets ported from other games without permission. A tool that reads the
  player's install is a different thing.

## Ban risk

Going online with modified files is widely reported to bring a soft ban of about 180 days or a
flagged save _(unconfirmed; nothing official)_. me3's defaults (official servers blocked, separate
save) avoid it. The build should always set a separate save file, and never set `start_online`.

## Open questions this raised

1. Licence: depending on SoulsFormatsNEXT or soulstruct makes the tool GPL-3.0. Writing our own
   readers and writers avoids that, at large cost.
2. Collision: can the player's own Elden Ring executable generate `hknp` collision, the way
   ERNavmeshGen generates navmesh? If not, no clean route is known.
3. Elden Ring keys and name hashes: can we derive them from the player's executable reliably?
4. Elden Ring paramdefs: where they come from, if not Paramdex.
5. Language: the format ecosystem is C# (.NET) almost entirely. soulstruct is Python.

## Sources

- me3: github.com/garyttierney/me3 (README, `docs/`, releases)
- ModEngine 2: github.com/soulsmods/ModEngine2
- SoulsFormatsNEXT: github.com/soulsmods/SoulsFormatsNEXT
- Smithbox: github.com/vawser/Smithbox
- WitchyBND: github.com/ividyon/WitchyBND
- soulstruct, soulstruct-havok, soulstruct-blender: github.com/Grimrukh
- HKLib: github.com/The12thAvenger/HKLib
- ER_HKX2Navmesh: github.com/LordExelot/ER_HKX2Navmesh
- ERNavmeshGen, UXM Selective Unpack: github.com/Nordgaren
- Paramdex: github.com/soulsmods/Paramdex
- rewwise: github.com/vswarte/rewwise
- JortPob: github.com/infernoplus/JortPob
- Format and map references: soulsmodding.com; EMEDF tables at soulsmods.github.io/emedf
- InfernoPlus's Morrowind port: notebookcheck.net and gamesradar.com coverage, 2025–26
- Elden Ring EULA: store.steampowered.com/eula/1245620_eula_0
- FromSoftware on Seamless Co-op: pcgamer.com; DSfix DMCA: gamedeveloper.com; Harada: ggrecon.com
- OpenMW FAQ; Skyblivion's about page; Ship of Harkinian's README; SM64 port coverage on
  nintendolife.com
