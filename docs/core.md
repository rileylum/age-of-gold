**Goal.** Age of Gold is a Dark Souls mod for Elden Ring: it rebuilds Dark Souls's world and game in Elden Ring's engine. It is a mod, not a remake: it holds nothing from either game. It reads the player's own installed copies of both on the player's machine, so playing it needs both games bought and installed.

- Faithful: Lordran is the world Dark Souls built, place for place: its maps, its connections, its enemies, items and events where Dark Souls put them.
- Clean: the repository and every release contain only our own code and data. Nothing from either game is ever copied into them.
- Safe to install: neither game's files are ever changed. Removing the mod is deleting one directory.

**Scope.** A tool the player runs once, offline, that reads both installs and writes the build: a mod directory that Elden Ring loads through me3, the mod loader. The source is read only at build time, never live inside the running game. Single player, offline only. The source game is Dark Souls: Remastered; the host game is Elden Ring. Lordran replaces the host's world: the build's maps take the place of Elden Ring's, and the Lands Between are not reachable while it is loaded. Not in scope: online play, new content, Dark Souls II or III, other host games, and any download of game files from anywhere.

**Proof.** A capability is built when a test proves it against real installs of both games on the developer's machine: the tool reads the source install, writes a build, and the test checks the build by reading it back as the host game would. These tests find the installs through environment variables, write only under the system temp directory, and never run in CI, because CI has no copy of either game. A test that is skipped because an install is missing proves nothing. A capability is proven in play when Riley has played through it in Elden Ring on the build.

**Principles**

1. The player's copy is the only source. Every asset, map, param, script, text and sound in the build is read from the player's installs at build time. A capability that would need a game file we cannot read from an install is not built.
2. Dark Souls's world, Elden Ring's rules. Where the source and host disagree on what the world is (geometry, placement, connections, who is where), the source wins. Where they disagree on how the game plays (stats, healing, resting, equipment, movement, combat), the host's rules win by default, and keeping one of the source's rules is a logged decision. Where they disagree on how the engine works, the host's engine does the work and the translation is recorded.
3. Unknown fails loudly. A file, format version, record or value the tool cannot classify stops the build and names it. It is never skipped, guessed or replaced with a stand-in.
4. Deterministic builds. The same two installs and the same version of the tool give the same build, byte for byte. A build records the tool's version and a fingerprint of each install it read.
5. Translations are explicit. Each format is translated by one module that owns the mapping between the source's shape and the host's. Source concepts stop at the reader; host concepts start at the writer; our own model sits between them.
6. The repository teaches nothing it took. Format knowledge comes from our own reading of the files and from public documentation; no code or data from either game's executables is copied, quoted or decompiled into the repository.

**Constraints**

1. Nothing from either game is committed or released: no assets, extracted or converted files, executable code, decompiled code, text, or screenshots of either. Local development may use game data and third-party tools, including their game-specific metadata, provided neither is committed or shipped. This permits external camera controllers and local comparison captures. The released tool must still read or derive what it needs from the player's installs, without shipping third-party game-derived data. Test fixtures are either written by us from scratch or produced from the developer's own installs at test time and never committed. `.gitignore` keeps builds and extracted files out.
2. Both games must be installed. The tool refuses to build without a readable install of each and names what it could not find.
3. Neither install is written to. The tool opens game files read-only and writes only into the build directory and its own cache.
4. Offline only. The build is only loaded with Elden Ring's anti-cheat off and the game offline, as the mod loader runs it. Nothing we ship connects a modded game to FromSoftware's servers, and the README says so.
5. Neither game's name is part of the project's name, logo or anything that could suggest it is official; the names appear only to describe what the mod works with. Age of Gold is licensed under GNU GPL version 3 (GPL-3.0-only). `NOTICE` records the trademarks and the clean-room posture.
6. Runtime dependencies are few, and each is a logged decision; so is every third-party tool the build runs.

**Technology.** The mod loader is me3: the tool writes a me3 profile beside the build that sets a separate save file and leaves official servers blocked. The maintained tool uses C# on .NET, with SoulsFormatsNEXT (GPL-3.0) for the supported game formats and HKLib (MIT) for host collision serialization. Riley selected this stack and GPL-3.0 for Age of Gold on 9 November 2026. Dependencies do not permit shipping game-derived data; resource and metadata use still obeys constraint 1. Riley develops on Linux, where both games run under Proton; most players are on Windows. Both are supported unless a decision says otherwise. Elden Ring's files are Oodle-compressed and the player's install holds only the Windows Oodle library, so on Linux the tool, or at least its Oodle work, runs under Wine or Proton (`docs/research/2026-11-06-spike-archives.md`).

**Architecture.** One pipeline, in stages: locate both installs; read and unpack the source's archives; parse each format into our own model; translate the model into the host's terms; write host formats into the build; check the build. Each format family (archives, models and textures, maps, collision, animation, params, event scripts, AI, talk scripts, text, sound) is one bounded context, with a reader, a model and a writer. Bounded contexts are the Language sections below; a package splits off only when it is built or replaced on its own. Collision is built by the tool itself, with no Havok tool: it reads an ordinary collision part from the player's Elden Ring install as a template for physics settings, writes our own compressed mesh into it, and places it through the map layout in a part whose hit filter matches its role (`docs/research/2026-11-06-spike-collision.md`).

**Open decisions**

Each is settled one at a time with Riley, and then moves into the section it belongs to.

Research behind these: `docs/research/` (landscape, archives spike, collision spike).

1. Navmesh: how the tool writes Elden Ring's navmesh, which no open tool converts from Remastered's. Not needed for the walking skeleton, which has only the player.
2. The language and format libraries. The ecosystem is almost entirely C#; SoulsFormatsNEXT, the most complete library, is GPL-3.0. Recommendation: C# on .NET with SoulsFormatsNEXT, which settles the licence as GPL-3.0; the alternative is writing our own readers and writers for every format. The spikes used SoulsFormatsNEXT and HKLib (MIT) for speed only; that settles nothing.
2. Elden Ring's archive keys, file-name hashes and paramdefs: how the tool gets them without depending on data that community tools copied from the game. Recommendation: read the archive keys from the player's executable, where they are plain PEM text, and hash the paths our own code names; both are proven by the archives spike. `regulation.bin`'s key and Elden Ring's paramdefs are still open.
3. Which of the source's rules, if any, are kept over the host's (principle 2), one at a time as each stage meets them.

**Language**

One word per concept, in speech, docs, code and tests.

_Games_

- Source game: Dark Souls, the game whose world is rebuilt. Host game: Elden Ring, whose engine runs it. Say the game's name when that is clearer; never "the original" or "the base game".
- Install: one game's installed files on the player's machine. The source install and the host install. Not copy, which is reserved for what we never do with them.
- Edition: which release of the source game an install is, such as Remastered.
- Player: the person who owns both games and runs the mod. Riley, while developing, is also a player.

_Building_

- Tool: our program, which reads both installs and writes the build.
- Build: the mod directory the tool writes, which the host game loads. One build per player, never shared. Building: running the tool.
- Cache: files the tool unpacks from an install to speed up the next build; it lives beside the build, never in the repository.
- Mod loader: the third-party program that makes the host game load the build instead of, or beside, its own files.
- Reader, model, writer: per format family, what parses the source's files, our representation between the games, and what writes the host's files.
- Translation: turning the model of a source thing into its host equivalent, such as a source map's collision into the host's. A translation that loses something records what.
- Fingerprint: the hash of an install's files that a build records.

_World_

- World: Lordran, the whole of what is rebuilt.
- Map: one of the source game's map units, as the files divide the world. Area: a named place within the world as a player would name it, such as Undead Burg, which may span several maps or share one.
- Connection: where the player passes from one map to another.
- Placement: where a map puts a thing: an enemy, an NPC, an item, an object, a spawn point.
- Event: a scripted behaviour of a map: a door, a lever, a boss fight, a trigger. Event script: the file that holds a map's events.
- Param: one table of the game's data, such as weapons or enemy stats. Row: one entry in it.

_Not used:_ remake, remaster or port for this project (it is a mod), assets as a noun for what we ship (we ship none), copy for what the tool does with an install (it reads), original or base game (source game), level (map or area), DLC for the source game's added areas (they are areas like any other).

**Roadmap**

In order. Each stage depends on the ones before it.

1. Research: the formats of both games, the mod loaders, the community libraries and their licences, and earlier projects that moved content between FromSoftware games. Written as dated snapshots in `docs/research/`; they inform the open decisions and are not specs. It ends with a spike proving that the tool can write collision Elden Ring accepts. Done: see the spike snapshots.
2. Walking skeleton, the first goal: a navigable Firelink Shrine with only the player in it. The tool finds both installs, reads Firelink Shrine's geometry and collision, and writes a build in which the player spawns there in Elden Ring and walks all of it, with no enemies, NPCs, items or events.
3. The world: every map's geometry, collision and connections, so the player can walk all of Lordran with nothing in it.
4. Things in the world: objects, items, bonfires and doors, each where the source placed it.
5. Enemies and NPCs: placement, models, animations, stats and AI.
6. Events and bosses: event scripts, fog walls, shortcuts, boss fights.
7. The game: progression, NPC talk and quests, text, equipment and the source's mechanics as decided.
8. Sound and music.
9. Release: a download of the tool alone, its instructions, and a check that the release holds nothing from either game.
