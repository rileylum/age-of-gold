# How enemies think — 16 November 2026

What was learnt carrying the source's enemy AI into a build, and what a build does with it. An
input, not a spec. A private trial on 15 November showed the approach works; this note records
it as landed.

## Where an enemy's thinking is kept

- Both games give a placed enemy a think row. A row names two scripts by number: what the enemy
  does out of battle, and what it does in battle. The rest of a row says how far and how widely
  it sees, how far it strays from home and when it turns back, how long it remembers a target,
  and more.
- Both games write those scripts in the same script language and ship them compiled, by the
  same release of the language into the same layout (their files open with the same 22 bytes).
- The source keeps a map's enemy scripts in one binder for the map and its shared scripts in
  another. The host keeps each enemy's battle script in a binder of its own, named by the
  battle number, holding one compiled script and a list of the global names the script sets;
  its shared scripts are in one binder.
- Most enemies of both games name the same out-of-battle number, ten thousand, which neither
  game's map binders hold a script for under an enemy's name: it is the common one (stand, and
  fight what is noticed). The host has a shared script of that number.

## The compiled script layout

In the tool's own words: a fixed opening (the language's mark and release, the sizes of its
numbers and of the parts of an instruction, and a test number), then one function. A function
is its source name, the line it starts on, four counts in a byte each (enclosed values,
parameters, whether it takes more, registers), a line number for each instruction, its named
locals, the names of its enclosed values, its constants (a number, text, or nothing), the
functions within it, and its instructions. Text is counted in eight bytes with its closing
nought; other counts take four. An instruction is 32 bits: a kind in the low six, a register
in the high eight, and between them one wide number or two narrow ones; a narrow number from
250 up names a constant instead of a register.

The tool reads a script whole and writes it back byte for byte. It changes only which global a
read or a write names.

## Running a source battle script in the host

- A source battle script leans on the source's shared scripts: choosing an act by odds,
  approaching, stepping aside. The host has helpers under many of the same names, with other
  arguments and behaviour. So each source shared script that defines a function the battle
  script reaches (or that a reached script reaches) is carried whole, and every global such a
  script sets is given a prefix of the build's own, in the shared scripts and the battle
  script alike. The host's helpers are neither used nor disturbed.
- A battle script also defines a helper or two of its own under names the host's shared
  scripts define. Those take the prefix too.
- A name the scripts read that the host's shared scripts set, and the source's shared
  functions do not, is the host's: the numbers of the engine's goals, of targets and
  directions.
- A number only the source sets (the goal's own number) is set first.
- A few names are read that nothing in either game sets (six in most of the scripts carried
  here, besides the engine's own functions). They read as nothing in the source too.
- The source defines a goal as plain functions named by a prefix (activate, update,
  terminate, interrupt, the last misspelt) and registers the prefix. The host keeps a goal as
  a table of functions under the goal's number in a table of goals, each taking the table,
  the character and the goal. A goal registered the source's way is never run by the host. So
  after the source script has run, a table is put under the goal's number whose entries hand
  the character and the goal to the source's functions.
- The host runs only the one script it expects in a battle binder, so all of this is one
  compiled script: set the numbers, run each carried shared script's top level, run the battle
  script's top level, install the table.

For the four enemy models built at Firelink that is seven battle scripts, each carrying two of
the source's shared scripts and setting some ninety globals.

## Think rows

A build gives each source think row its placements name a think row of its own: an installed
row of the host character the enemy borrows from, with what the source row says of the same
things. The source's field positions are from the table definition the source install ships.
The host does not ship one; the host's positions were taken from reference metadata (below)
and checked against the installed table's usual values, which fall where the names say (sight
distances of ten to thirty, angles up to 180, home distances of thirty to eighty-five).

Carried: the battle number; how long it pushes at a wall; how far it sees; where sound stops
and how far it smells; the three home distances; how long it idles; how long and how far it
looks back at a target going home; when it forgets what it saw and heard; how near a target
starts a battle; how it weighs joining an attack; how wide and how tall its sight is. The
out-of-battle number is the source's where the host has a shared script of that number, and
the common one otherwise.

Not carried: hearing distance (the host keeps a tenth of a metre in nearly every row and
hears by other means); calling for help; the goals taken on noticing something; which parts of
a navigation mesh it may use. Forgetting times are whole seconds in the source's row and
fractions in the host's; that both are seconds is taken from the two tables' usual values.

The difference matters: a hollow soldier sees thirty metres in a cone of 120 degrees; the host
character it borrows from sees eight in forty.

## Seen in the game

A hollow soldier placed by the resting point for the trial, with the build's packaged script
and think row:

- with the borrowed character's own out-of-battle script, or the common one, it closes on the
  player when it first sees them and kills them;
- that holds with every carried value, so long as its first sight of the player comes after
  the map has loaded.

**An enemy that can see the player at the moment the map loads never engages**: it stands
where it is, targetable, for as long as watched. Narrowed down by changing one value at a time
in the built think row: not the home distances, the forgetting times, the battle-start
distance, guarding while moving, path-finding or the out-of-battle script, but whether its
sight reaches the player's spawn (11.7 metres off in the trial: a sight of eleven fights, of
fourteen freezes). The host pauses after loading while it prepares the build's navigation
mesh; a battle begun in that pause may be what is lost. No enemy placed at Firelink is within
sight of the resting point, so it does not arise there, but it is not understood.

Not yet seen: an enemy noticing from far off and running in (the trial fights begin within
five metres); the other six battle scripts in play; the placed enemies themselves, which the
private harness cannot yet set the player down beside.

## Sources and licences

- The compiled layout is that of Lua 5.0 (MIT licence), recalled from its published reference
  implementation's description of its own format; no code of it was read or copied for this,
  and the tool's reader and writer are its own.
- The source's think row layout is from the table definition in Riley's install of the source.
- The host's think row field positions were read privately from the Paramdex metadata bundled
  with a local Soulstruct checkout (GPL-3.0-or-later according to that checkout), and are
  restated here only as positions confirmed against the installed table. No metadata file or
  code is incorporated.
- The scripts themselves are read from Riley's installs at build time and are never in the
  repository; what they were seen to do was read privately from their compiled form.
