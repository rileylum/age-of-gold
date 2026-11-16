# Events at Firelink: an inventory — 16 November 2026

What the source's event script for the area does, what the host map's own script still does in
a build, and what translating one into the other would take. Groundwork for the events stage;
an input, not a spec. Nothing here is built yet.

## Both games' event scripts

Both games keep a map's events in one file of the same family of formats, which the format
library the tool depends on reads for both. A script is a list of events; an event is a list
of instructions; an instruction is a pair of numbers (a bank and a number within it) with a
block of argument bytes; an event started with parameters has some argument bytes filled in
from them. Event nought runs as the map loads and starts the others; event fifty runs before
it.

The two games number most of their instructions alike and give them much the same arguments;
the host's often take a few more. Which do, and how, has to be established instruction by
instruction.

## What the source's script does

Sixty-six events, about eleven hundred instructions, of sixty-five kinds. Two thirds of the
instructions are of five kinds: testing a flag, starting an event, combining conditions,
setting a flag, and showing or hiding a character. By what they are for:

- **Who is here.** Some forty events each watch a few flags and show or hide one person, or
  change whose side it is on, as the story moves. One event before load hides every person the
  story has not yet brought. This is what the build stands in for by naming the two people
  present from the start.
- **How enemies wait.** One event, started fifteen times, has an enemy wait in a pose of its
  own until the player comes within a distance, then leave it.
- **The resting point.** Registered with its flag and how far it has been kindled; a second
  registration applies once a flag is set.
- **The lift**, by where the player stands in four regions, playing the lift's own movements.
- **The nest**: a prompt, a pose, a wait, and a journey to another map.
- **Treasure tied to other maps**: one event, started some seventy times before load, each
  for a flag and an item, opens treasure kept here.
- **Chests and bodies with items**, and the states of a few objects.
- **People under attack**: one who cannot die and leaves when badly hurt; others turning
  hostile when struck.

Its flags are numbered in the source's own blocks for the area (eight digits, opening with the
area's number), with a few shared ones.

## What the host map's own script still does in a build

A build's event script is the host map's own with one instruction added: the registration of
the build's resting point. So the host's twenty events for that map still run: its opening
film and first arrival, the setting of the time of day, and its scripted first fight with the
journey that follows a death in it. None of the people or
things they name are in a build's map, so most do nothing; the arrival is the one a build
relies on, and the fight's entity number is why a build must not reuse that number.

## What translating would take

1. **The build's script its own.** Event nought holding only the build's registrations and
   the translated events; of the host's, only what a first arrival needs. This changes how a
   new game begins, which the capture harness does not exercise (it continues a save).
2. **Entities.** The source's events name characters, objects and regions by entity number;
   a build's placements carry none yet (enemies are placed with nought). Each needs a number
   in a range of the build's own, and a map from the source's.
3. **Flags.** The host keeps flags in blocks it must be told of; a build needs a block of its
   own and a map from the source's numbers. What a new game sets is part of this.
4. **Instructions.** A table from each source instruction to a host one with its arguments
   converted, grown event by event; an event using an instruction not yet in the table is
   left out and reported, as unbuilt surfaces are.
5. **What an event touches must exist.** The lift, the nest and the chests need their objects
   built with their own movements; the people need the others placed; journeys need the maps
   they go to.

A first slice that needs little else: the waiting poses of the hollows (one event, one
instruction beside the common ones, entity numbers for fifteen placements), which is also the
most visible difference from the source in a fresh game.

## Sources and licences

- Both scripts were read from Riley's installs with the format library the tool already
  depends on.
- Instruction names and argument layouts were read privately from the instruction
  descriptions bundled with a local Soulstruct checkout (GPL-3.0-or-later according to that
  checkout; the descriptions themselves originate with community documentation of the
  formats). Nothing of them is incorporated; what a build comes to rely on will be restated
  as positions and confirmed in the game.
