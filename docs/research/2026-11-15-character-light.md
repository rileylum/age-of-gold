# Light on characters — 15 November 2026

Characters in a build looked far too dark. This snapshot records where each game gets a
character's light from, how the host stores the part a build can replace, and how that was
measured. It is an input, not a spec.

## What the source does

Read from the remastered source's character pixel program (decompiled privately; nothing of it
is in the repository). A character is lit by:

- an environment cube read along the surface's facing, times a colour and a renderer-wide
  strength, darkened by the shadow map;
- a hemisphere colour running from a lower to an upper colour as the surface turns upward
  (weight `n.y / 2 + 1 / 2`);
- a specular environment cube, and point lights.

The three directional diffuse lights of a lighting row are not used by this program. The
colours and the cube come from the character lighting row that the collision under the
character selects. This is the same model the build's scenery programs already use, with the
character table in place of the scenery table.

## What the host does

Measured on a character dressed in flat mid-grey, rough, non-metal textures, by changing one
thing at a time and reading the shown frame back through the build's own display stage.

- The map's graphics parameters hold four light sets, each keyed by seven times of day.
  Characters at Firelink take one of them; the build's scenery takes none.
- Directional light 0 lights and shadows characters. Its colour's fourth value is a linear
  strength. The hemisphere colours have no visible effect on a character. Two further
  "local" directional lights light a whole body without shadow when enabled.
- Ambient light does not come from the light set. It comes from light baked beside each of the
  map's environment points: four volume textures per point (named W, X, Y and Z) and one
  reflection cube, in one binder for each of seven sky states and three quality words (`high`,
  `middle`, `low`). Baked for the host's own scenery, these give a body standing in rebuilt
  scenery almost nothing.
- A volume texel's red, green and blue are signed about mid-grey; its fourth value is a scale
  shared by the three. W is a constant term and X, Y and Z are terms along the world axes: a
  surface facing `n` is lit by about `W + X n.x + Y n.y + Z n.z` times its own linear colour.
  The axes agree with the source's.
- The scale is exponential. At full colour and scale 96, with the environment box multiplier at
  the host's own value of one, a surface shows 0.234 of its colour; each step of scale
  multiplies that by `e^0.0507` (2.25 for sixteen steps; measured at scales 64, 80, 96, 112 and
  128). The directional terms act at between 0.93 and 0.97 of the constant term's strength for
  the same stored value; the build takes one.
- An environment box's point-light multiplier scales this light, though by less than its own
  ratio (doubling it gave 1.76 times). A point's first unnamed value does not.
- The live light blends two neighbouring sky states.

## Reflections

- The host reflects, for each environment box, the cube baked beside the box's point, at the
  box's specular multiplier. A multiplier of zero removes reflections. The point's first
  unnamed value has no visible effect on them.
- The host's cubes here are dim, and with them every body carries a pale haze. The source
  reflects one specular sky cube, at the row's specular colour times the renderer-wide
  strength. Its levels are the same format as the host's, so a host cube can take the source
  cube's levels of its own size unchanged.
- The build's scenery took the host's reflections as well. It writes no surface colour, so the
  host's diffuse light never showed on it, but the host's specular light was still added. The
  host composes a frame as accumulated diffuse plus specular plus emitted light, and writes
  the emitted light times its weight to a second target for glow. The scenery's source
  lighting is the emitted light; marking it with a weight below nothing lets the composition
  stage show it alone.
- The two games' reflections cannot be matched by one strength. The source reflects from a
  surface that is not metal a share set by its specular map, around a hundredth for the
  hollow's leather; the host reflects a fixed share several times that. Compared with captures
  of the source game from the same eye, a box multiplier of 0.3 matches leather and skin and
  leaves metal dimmer than the source's; 1.0 is nearer on metal and leaves leather glossy. A
  steeper gloss curve does not help: a rougher surface reflects the blurred sky, which is
  bright.
- The host material's unnamed colour setting tints surface colour and reflections alike; it is
  not a separate reflectance.

## Surface colour

The source multiplies a surface's colour map by a colour of its material definition's, times
a power, before its display curve; where the specular map says metal it uses the definition's
specular colour and power instead. For the hollow's leather the factor is 0.6, a third in
linear light. The host's plain character definition has a colour setting applied to the
colour map in linear light (seen in game), but one for the whole surface.

## What the build does with it

- The source's character light for the arrival's row is restated as a constant and three
  linear terms (the closest fit over every facing) and written, uniformly, into every volume
  of every state and quality. The host's directional lights are put out. Closed loop on the
  grey body, the shown levels were within about a tenth of the levels predicted from the
  source's row on side-facing surfaces, and a little low on top.
- Every reflection cube takes the source sky's levels of its size, every box reflects at one
  strength, and the composition stage shows the scenery's marked light alone. Scenery in the
  three compared views stays within a few levels of the source game's.
- Each character surface is drawn by a definition of the build's own carrying the source's
  colour factor, the plain and metal factors mixed by the specular map's share of metal.

Not done: characters take no shadow from scenery and cast none on it; one row lights the
whole map; metal is dimmer than the source's; the colour factor is not told apart texel by
texel.

## Decoding the source's cubes

The source keeps its environment cubes in the unsigned HDR block format. The tool decodes it
from the format's published definition. The decoder was compared, privately, with an
independent decoder on two of the source's cubes, which between them use all fourteen block
modes: every value agreed.

## Sources and licences

- The Khronos Data Format Specification 1.3, BPTC chapter, for the HDR block format's bit
  layouts, region table, anchor table and blend weights (© The Khronos Group; specification
  text, read for the format's facts; the tables are restated in the tool's own notation and no
  text or code was copied).
- Microsoft's BC6H format page (Microsoft Learn documentation terms) for the mode list and the
  widening and finishing arithmetic.
- Pillow 13 (MIT-CMU licence) was used privately as the independent decoder; none of its code
  was read or incorporated.
- The fromsoft-formats 0.17.0 documentation (GPL-3.0-or-later) for the three quality words of
  an environment binder's path, checked against the install. It does not describe the
  volumes' contents; no code was read or incorporated.
- Everything about the host's and source's behaviour comes from read-only inspection of
  Riley's installs and from running a build offline.
