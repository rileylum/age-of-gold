# Cloth on characters — 16 November 2026

What was learnt trying to make a built character's cloth move, and what the build does instead. An input, not a
spec.

## What the source does

A source character with cloth has a second physics file beside its first. It holds rigid bodies named for bones
of the model. Some have no inverse mass and follow the animation; the rest hang from them in chains, joined body to
body, and the model's bones of those names follow the bodies. The model rests those bones spread out — a skirt's
flaps stand almost level — for the chains to drop as the game starts.

Of the characters built for the area: the armoured hollow has six chains (two from the upper spine, two from the
pelvis, one from each thigh), another hollow has two from the head, and two have none.

## What the host does

A host character with cloth keeps, in its model binder, a cloth file and a small table beside it. Read from one
installed exemplar (a caped enemy), in the tool's own words:

- The cloth file holds, for each piece, a simulated mesh of particles, the bones it reads and writes, and a list of
  operators run in order each frame: skin the mesh to the animation, move the fixed particles, deform a display mesh
  from the bones, simulate, and gather results. Fixed particles have no inverse mass.
- Positions are packed as 16-bit integers with a shared exponent; vertices are grouped in blocks of sixteen, the
  last block padded with its last vertex. A bone's place in the skinned mesh is the inverse of its rest transform.
- A bone driven by the cloth takes a frame built from one triangle: two edges from the centroid, their cross
  product, and the centroid, multiplied by a stored local transform.
- The table beside the cloth file has a 40-byte header and 16 bytes for each mesh of the model; in the exemplar
  every entry is empty.

## What was tried

A cloth file generated from the source's chains (one particle a body, fixed where the body follows the animation,
driving the chain's bones) loads without complaint and is never run: the bones stay where the animation leaves
them. None of these changed that, each tried in the game: gravity turned sideways to make any motion plain; bone
offsets; keeping the exemplar's piece name; a table sized for the built model's meshes; the host's naming prefix for
cloth bones in the model, the physics file and the skeleton; and the exemplar's own cloth file retargeted to the
built character's bones, with and without its simplifications. An earlier note that the host had run a cloth file
for a built character was not borne out.

What decides whether the host runs a character's cloth file is not known. Candidates not yet examined: the
behaviour graph or action script of the exemplar the character borrows, a table row keyed by the character's
identity, and the display mesh the cloth file names.

## What the build does

Until that is known a build simulates nothing. It reads the source's cloth file, finds the chains, and for each
chain of two or more bones that rests more than thirty degrees from straight down, turns it about its top to twenty
degrees from straight down on the side it leans to, moving the vertices that follow those bones. The cloth hangs
but does not swing. The two angles were chosen by eye on the armoured hollow; the source's joints have limits of
their own, which are not read.

## Sources and licences

- Everything here comes from read-only inspection of Riley's installs with the tool's own readers and from running
  a build offline. The format library the tool already depends on (see the reproducible-builder note) parsed the
  host's cloth file; no further source was read.
