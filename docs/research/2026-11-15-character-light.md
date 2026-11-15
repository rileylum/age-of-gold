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
- The host's reflection scale, measured on a white, rough, all-metal body under cubes of one
  grey: the shown value is the cube's value times the box's specular multiplier times 20.8.
  The source shows the cube's value times its specular gain, so a source gain converts to a
  box multiplier by dividing by 20.8. A first setting made by eye was five times too weak and
  left metal black.
- The host reflects at least 4 % from anything that is not metal. The source reflects a fifth
  of the specular map's blue channel times the brightness of the definition's specular colour:
  about 1 % for the hollow's leather, up to 20 % in patches of the warrior's mail whose colour
  map is black there. The build's maps therefore carry four times the source's reflectance and
  the boxes reflect four times more weakly, which puts the host's floor at the source's 1 %.
- The host material's unnamed colour setting tints surface colour and reflections alike; of
  its two unnamed numbers one overrides metal and the other frosts the surface. None is a
  separate reflectance.
- For a rough surface the source looks its reflection up near the surface's facing, well
  blurred, so it reads as a second soft light. The host's reflections are sharper. Changing
  the gloss curve made little visible difference.

## Shade

In the source's character program the shadow map multiplies the whole environment term,
diffuse and specular. The area's shadow rows give a black shadow colour at full density, so a
character in shadow keeps only the hemisphere colour: soft, bluish, without reflections. One
row reaches only ten metres from the camera. The build does not shade characters, so one
sitting in shade is lit and reflects as if in the open; with correct reflectance this is the
largest remaining fault. The host bakes its light for characters into volumes fine enough
(about a metre) to hold a baked shadow.

## Surface colour

The source multiplies a surface's colour map by a colour of its material definition's, times
a power, before its display curve; where the specular map says metal it uses the definition's
specular colour and power instead. For the hollow's leather the factor is 0.6, a third in
linear light. The host's plain character definition has a colour setting applied to the
colour map in linear light (seen in game), but one for the whole surface.

The host lights a surface's colour times what is not metal, and reflects 4 % blended towards the colour by metal. Maps
made texel by texel to match the source under those rules were tried first: they cannot go below the host's floor, a
closest fit by least squares turns dark leather into black metal, and nothing in them shades. They are not used.

## A character program of the build's own

The host's own character program cannot be made to match: it has a 4 % reflectance floor, sharper reflections, and no
way to shade a surface the way the source does. So a build draws its characters with a program family of its own, made
from the host's plain character family. What was found on the way:

- The host's deferred character stage writes the same five targets as its scenery stage: an encoded normal (half the
  unit normal plus a little over a half), metal and gloss, the surface colour, an unused target, and emitted light with a
  weight. It reads only the material's textures and constants: no cubes, no baked light, no shadow map. A family's
  metadata must say that it writes emitted light, or the light does not show.
- A family's metadata declares its texture inputs by slot, and a slot is the texture register the stages read. Three
  inputs appended to the plain family's three (declared as a host family with many inputs declares its own) are bound:
  a cube put in the fourth slot showed on a body (seen in game). So the source's diffuse cube, its sky cube and its
  reflectance table can be read directly.
- The host's vertex stages take a vertex's position relative to the camera before projecting it, so the vector to the
  camera is in hand there and can be forwarded to the pixel stage.
- The material's two colour settings reach the stage unchanged, in a constant buffer the stage already opens.
- How the host lights a deferred surface, read from its own stages. A stage for each directional light adds, to an
  accumulation of diffuse light, the light's colour times how far the stored normal faces it times a shadow value (the
  lesser of the first two values of a screen-sized shadow mask), with a view-dependent diffuse term; it takes the
  light's direction and colour from the light set whose number the surface stores beside its colour. Stages for the
  environment boxes add the baked light along the stored normal; a later stage multiplies that by a screen-space
  occlusion value. Point lights add theirs. Then one small stage multiplies the whole accumulation by the stored colour
  times what is not metal, and the composition stage adds reflections and emitted light.
- A first scheme stored the sun's own direction as the normal, so that the sun lit the stored colour at full strength,
  and subtracted in the emitted light what the baked light would add. It does not hold: with a false normal the
  screen-space occlusion and the host's diffuse term change with the viewing angle (seen in game: the same flat grey
  body showed from 0.09 to 0.41 of its colour, front against back), so nothing fixed can be subtracted.
- What holds: a surface of whole metal and no gloss takes no diffuse light from the host at all. So that pair of values
  can mark a character, and two of the host's stages can be told what to do with a marked surface: the sun's stage
  accumulates the shadow value alone, scaled up by 1024, and the colour stage multiplies by the stored colour scaled
  down by 1024. The frame then shows the stored colour times the shadow, whatever the normal or the viewing angle; the
  baked light and point lights the host also accumulates there come to under a thousandth of themselves.
- The host turns its sun's two angles (x, y) into the direction to the light (cos x sin y, -sin x, cos x cos y): found
  by trying the four sign choices and seeing which facing went unlit.
- The host's shadow value leaves the side of a body turned away from the light lit, and the host darkens it by the
  light's own facing term, which a marked surface does not pass through. A hollow seen from behind in the source is
  matt and dark, so a character's stage shades by the turn of the surface itself. Whether the source does shade that
  side is not settled: a seated man's chest faces away from the shadow light and is an even dark teal in the source,
  black in the build.
- None of the source's scenery pieces casts shadows; its objects and characters do. With every built placement casting,
  a character standing by a wall was in shadow where the source's stands in the light.

The source's character light, in the terms the program restates (read from the remastered source's program, decompiled
privately):

- The colour map times the definition's plain colour, or its metal colour where the specular map's green says metal,
  raised to 2.2, is the surface's colour. What is not metal scatters it; metal reflects it; a plain surface reflects a
  fifth of the specular map's blue times the metal colour's brightness. The specular map's fourth channel scales all of
  it, and the sky's light once more.
- The sky's light is the diffuse cube along the normal times the scattered colour, plus the sky cube times the
  reflectance, both times a gain. The sky cube is read between the mirror direction and the normal, nearer the normal
  the rougher the surface (red channel), at a level of the cube's count less three plus 1.2 times the base-two logarithm
  of the roughness. It fades as the mirror direction sinks below the surface itself. The reflectance is the head-on
  reflectance times one number plus a grazing reflectance times another, both read from a table by roughness and the
  angle to the eye. The table is a file of the source's: two half-precision numbers a texel, 256 square.
- A lighting row names four sky cubes by number, and a definition chooses which its surfaces reflect: at Firelink
  leather takes the first and metal the third. The area has a cube of its own only for the first number: warm straight
  overhead (about 0.26), blue around that, black below. A set every area shares (`other/envlight.tpf`) has a cube for
  each number; its third is about 3.0 overhead. Tried in game for metal, with the row's gain: white where lit, which the
  source's metal is not. So which cube a number without an area cube takes is not known; the build uses the area's.
- The program reads the specular map one of two ways, by a setting of the definition's: roughness, metal, level and
  openness; or, the older way, a reflection colour with roughness in the fourth channel on a surface that scatters all
  of its colour. Of 580 definitions 92 state the first, 110 the second and 378 neither. Those that state neither (the
  blades, the mail) read the first way: read the second, their maps gave magenta and green (seen in game).
- Shadow multiplies the sky's light and leaves the hemisphere colour alone. The shadow row says where the casting light
  stands, how dark and what colour a shadow is, and how far from the camera shadows are whole and then fade out. Every
  collision of Firelink selects one row: black, full, whole to three metres and gone at ten. So in the source a shaded
  character is only shaded when seen from close by.
- The shadow table's definition spells one field's first letter with a full-width character.
- Not carried: the source also multiplies by a vertex colour, adds point lights (the bonfire's), a screen-space
  darkening, and fog and scattering; and it treats a surface turned away from the shadow light by a gradient setting
  that is zero in this row.

## What the build does with it

- A character family is cloned from the host's plain one. Its deferred vertex stages forward the vector to the camera;
  its deferred pixel stages work out the source's light from the source's own colour, specular and relief maps
  (unchanged), the two cubes and the table, and the definition's two colours.
- The part a full shadow would remove is stored as the surface colour, less the share a surface turned away from the
  shadow light has already lost (wholly from a cosine of a sixteenth past side-on, an estimate), and the surface is marked with whole
  metal and no gloss; the rest is emitted, with a weight of minus two. The host's stages for a directional light and its colour
  stage are rewritten as above, and the composition stage shows accumulated diffuse light plus emitted light where the
  weight is minus two, without the host's reflections or glow.
- The host keeps one sun, from where the source's shadow light stands. A build's characters take only its shadow. Every
  other directional light is out.
- The host's baked light, in every volume of every state and quality, is the source's hemisphere colour alone. What the
  host's own programs draw (the player in host gear) is therefore lit by the hemisphere colour and the shadowed sun, and
  reflects the source's sky from the host's cubes at the source's gain through the measured scale. The sun is white and
  adds half a facing surface's colour: a stand-in for the source's sky light, which is not directional.
- A built scenery placement casts shadows only if the source's does.
- The composition stage shows the scenery's marked light alone, as before.

Not done: one row lights the whole map; the items listed as not carried above. A host surface of exactly whole metal
and no gloss would be taken for a character's.

Not right yet (15 November, evening): against the source game's pictures, mean levels of three views agree within a
few levels and skin and leather are close, but metal reflects warmer and harsher (a blade's edge and flat, mail), and
mail that is an even dark teal in the source is black with bright edges. The row's reflection gain as the build works
it out (white times 3.00, raised to 2.2, times the renderer's strength of 3: 33.6), the cube a number resolves to, the
count of levels the blur is measured from, and the shade of a surface turned from the light are each inferred, not
read from the running game. A capture of one frame of the source game would give them.

## Read from a frame of the source game (15 November, night)

One frame of the source game was captured at the hollow's front view with RenderDoc and replayed privately; the
capture and everything read out of it stay in the capture scratch. What the hollow's draws were given (their
constants, their textures, their program) corrects several things inferred above.

- **The light row.** A character takes its row from the table the scenery selects from, by the number its collision
  names. The build had read a second table of the same shape under that number; the character draws do not use it,
  and the tool no longer reads it. The hemisphere's two colours are the row's at the renderer's ambient strength, as
  for scenery.
- **The cubes' strengths.** The two cubes are scaled by the row's two environment colours times one. Scenery's are
  scaled by the renderer's environment strength of three, which the build had applied to characters as well: with the
  wrong table that made the reflection gain 33.6 where the source's is one.
- **The cubes.** A character does not take the area's scenery cubes. The map lists light captures, each a point; a
  collision names one by its place in that list; the capture's name ends in a number, and a package of the area's
  holds a diffuse cube and a sky cube under that number. Firelink has sixteen captures. A sky cube's name also ends
  in which of a definition's four choices it is, and only the first is stored: in the frame, the hollow's metal
  (which chooses the third) and its leather (the first) are given the same cube. The count of levels the blur is
  measured from is the cube's own, eight.
- **Shadow.** The sky's light is multiplied by the square of one less the shadow's strength times its reach times
  how shadowed the surface is. The strength is the row's density times a share the definition states (four fifths
  for every character definition built here). The power is a named setting of the program's; a variant of the
  program that also reads a light map multiplies by a second named setting, which the character draws here do not.
  How shadowed is the shadow map's answer plus the surface's own: wholly once its relief-bent normal is side-on to
  the shadows' light, not at all from a cosine of a fifth towards it. So a surface turned from the light is shaded
  by the program itself, as the build guessed, but by the bent normal and over a wider edge.
- **Glow.** A surface gives off ten times its colour, times a named setting of one, where its specular map's fourth
  channel is less than whole; shadow leaves it alone.
- **Air.** The whole then passes through the collision's fog and scattering rows, as scenery does. Even at no
  distance the scattering row tints what it lights (the row's reflectance colour, half blended), which is why a
  build's characters looked warmer than the source's.
- **Also.** The whole is multiplied by a screen-space darkening. No point light reached the hollow in that frame.
  The vertex colours of the four enemy models built are white throughout, so nothing is lost by not reading them.
- Three numbers were read from the frame and are written into the tool, because where the source's program keeps
  them was not found: the cubes' strength of one, and the cosine and edge of a surface's own shadow.

What the build does now: each character takes the light of the collision the source places it on. A character
family is made for each set of light, fog and scattering rows its characters stand in, with that light compiled into
its deferred pixel stages; a material names the two cubes of the collision's capture; and a model placed in a second
light is built again as a character of its own. The pixel stage emits the wholly shadowed light, passed through the
air, and stores what a surface out of the host's shadow shows above that, so the frame is the source's at both ends
of the host's shadow and a straight line between (the source's own edge is curved). What the host's programs draw
(the player in host gear) takes the arrival's hemisphere colour from the baked light, a sun coloured as the
arrival's diffuse cube is overhead at the row's strength, and the arrival's sky cube in the host's cubes.

Not carried: a character that walks onto a collision selecting another light keeps the one it was placed in; the
screen-space darkening; point lights.

Seen in the game against the source's pictures (16 November): the seated warrior's mail is the source's even dark
teal where it was black with bright edges, and the hollow's metal no longer blazes; mean levels of the three views
are within eight of the source's in every channel. Still off: the hem of mail hanging under his thighs and the
front of one shin are lit where the source's are dark. Both lie under his own body as the shadows' light sees them.

## No cast shadow reaches a character (16 November)

A frame of the candidate was captured the same way, at the warrior's view and at the hollow's, and replayed
privately. It shows why the hem is lit, and that an earlier claim of this note is wrong.

- The host's stage for the sun reads one texture for its shadow and uses three of its values: the lesser of the
  first two multiplies the light, and the fourth is mixed in by a constant of the scene's. The build's rewrite of
  that stage gives a marked surface the lesser of the first two alone.
- In both frames the first value holds the host's screen-space occlusion (dark in creases, no cast shapes), the
  second is one everywhere, and the fourth is nought everywhere.
- So what a character's stored light is multiplied by, in the candidate, is screen-space occlusion and nothing
  else. No shadow cast by scenery, by an object, by another character or by the character's own body reaches it.
  What reads as shade on a character is the program's own shadow for a surface turned from the light.
- The character's meshes are drawn into the host's shadow pass (their index counts appear among its 396 draws into
  a 4096 by 2048 depth target), and two full-screen stages read that target before the occlusion stage writes the
  texture. Half of the depth target looks empty and the rest holds a few tiles of fixed scenery.
- Earlier, while the build's scenery still cast shadows, a hollow behind a wall was wholly dark, so the second value
  did carry cast shadow then. With the scenery's flags as the source has them (its map pieces cast none) nothing
  that still casts shows up in it.

Read further the same morning:

- The sun stage mixes the fourth value in by the scene's shadow colour (one, less the colour times one less the
  value), so the fourth value is what the stage treats as the sun's cast shadow. As stored it is nought in every
  pixel, before and after each of the three stages that write the texture: the first, which reads the shadow map,
  fills the first three values with what looks like packed depth; the second changes nothing; the third overwrites
  the first three with occlusion, one and one.
- The hollow's five meshes are among the draws into the shadow map's largest tile, so it is drawn as a caster. That
  tile, stretched, shows large flat overlapping sheets and no figure that can be made out. The built map's casting
  placements are only the bonfire, the statue, the urns, the Fire Keeper and the two people; 22 of the 24 enemy
  placements keep their host template's flag and do not cast, where the source's enemies do.
- Tried privately and reverted: with the sun stage also multiplying a marked surface by the fourth value, every
  character shows only its wholly shadowed light, the hollow in the open included. The seated warrior's hem and
  shin then match the source's; the hollow, lit in the source, is far too dark.

Settled the same day, by reading the host's own stages for the shadow texture and by trial in the game:

- The stage that works out the sun's cast shadow from the shadow maps writes packed depth to the texture's first
  three values and the shadow to its fourth; the stage after it overwrites the first three with screen-space
  occlusion and two values of the material's, leaving the fourth. So the fourth value is the sun's cast shadow, and
  the lesser of the first two, which the build had taken for it, is occlusion.
- It was nought everywhere because the host draws the build's scenery into its shadow maps whether or not a
  placement is flagged to cast, so every wall and ruin shaded what stood behind it; the source's own scenery casts
  nothing. A setting of the material's definition does stop a surface casting.

What the build does now: a scenery model none of whose source placements casts is drawn by definitions that forbid
casting, named apart from its family's casting one (the bonfire, the statue, the urns and the Fire Keeper still
cast); and the sun's stages give a marked surface the occlusion times the cast shadow where the stage itself reads
the cast shadow. Seen in the game: the hollow in the open is lit as before, and the seated warrior's own body now
shades the hem of mail under his thighs and the front of his shin, as in the source.

Still open: the host's shadow is coarser than the source's close to the camera, so small shadows are softer; the
measured "a sun of strength one adds 0.40 of a facing colour" was taken while everything stood in the host's
shadow, and what the host's own programs draw (the player) may now take more of the sun than intended.

## Decoding the source's textures

The source's colour maps are often in the high-quality four-channel block format. While a build made a character's
maps texel by texel, the tool decoded every mode of that format and encoded two of them, from the format's published
definition (privately byte-identical to an independent decoder on five textures of both games). A character now
wears the source's maps unchanged, so that code and its checks were removed on 16 November; the commit before holds
them. What remains writes the two-channel format (the reflectance table) and a block of one colour, and reads the
HDR cubes.

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
- RenderDoc 1.46 (MIT licence) captured and replayed the frame, privately; none of its code was read or
  incorporated, and nothing of the capture is in the repository.
- Everything about the host's and source's behaviour comes from read-only inspection of
  Riley's installs and from running a build offline.
