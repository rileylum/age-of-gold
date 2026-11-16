# Faces and hair for people — 16 November 2026

How the source gives a person its hair, face and skin, read from Riley's install and from one frame
of the source game captured privately, and what a build does with it. An input, not a spec.

## Where a person's look is kept

- A person's start-up row names a face row and says whether the person is a man. The positions
  of those fields, and of the fields below, were read from the table definitions the source
  install itself ships.
- A face row is 128 bytes: fifty bytes that shape the face, fifty that colour its skin, the
  hair's style, a byte only ever seen empty, three bytes of hair colour and three of eye colour.
- Hair is a row of the armour table, of a kind of its own. A man's styles are rows a hundred
  apart from row one thousand (a woman's from three thousand). The row names the hair's part
  model and, like armour, the marks it hides on other parts. A shaved head's hair model has no
  mesh.
- Every person shares one head model (the skull and neck) and one face-front model. The face
  front has four meshes: the face, the jaw, the eyes, and a four-vertex sheet between the eyes
  that reads a single point of the eye picture where the picture has no coverage.

## The face front's vertices

The format library the tool depends on read no vertices for the face front. The model marks
each of its vertex buffers with a high bit of the buffer's number, which the library takes for
a packed layout used on another platform. The vertices are stored plainly, like any other
buffer's; the mark seems to say that the game rewrites the buffer as it runs. The tool's own
model reader now reads such a buffer, and takes the next version of the model format as well
(some hair models are one newer than the map's; the reader agrees with the library vertex for
vertex on every part of both people).

## Shaping

The source keeps its face generation in one binder: a base skin picture, a "shaping mesh" for
the face and one for the eyes, a file of modes for each, a file of modes for the skin, and a
map from the face-front model's vertices to shaping vertices. In the tool's own words:

- The map has a 16-byte header and a 12-byte record for each mesh of the model: where the name
  of the shaping mesh it follows is, where its numbers are, and how many. The numbers are one
  16-bit shaping-vertex number for each model vertex.
- A shaping file has a 64-byte header (a tag, the vertex count, the count of modes that bend
  both sides alike and of lopsided ones), then for each mode a scale and three 16-bit whole
  numbers a vertex. A mode moves a vertex by its weight times the scale times the numbers.
  The model's depth runs the other way from the shaping data's.
- A face row's byte weighs one both-sides mode: byte × 20 / 255 − 10. The lopsided modes stay
  at nothing.

Checked against the frame: the face of the seated warrior, bent this way, agrees with the
vertices the source game drew to within 0.13 mm (the fitted rule was 0.07845 × byte − 10.0003,
correlation 0.99997). A second person in the frame, whose face row was found by its fitted
weights, departs from the rule by the same small constant in each mode, so the source's
starting shape differs from the shaping mesh's by under a tenth of a millimetre; that is not
carried.

The source also rewrites the face's normals, and leaves its tangents alone. Its normals are not
the bent surface's own: they differ from area-weighted normals of the bent mesh by some
thirteen degrees on average, and no welding, weighting, smoothing or scaling tried explains
them. The tool turns each of the model's normals by the change in its surface's own normal,
which leaves it as far from the frame's.

## Skin

- The skin picture a person's face wears is the base picture plus, for each of fifty modes, the
  weight (the same rule) times a scale times a signed byte for each texel's red, green and
  blue. A mode keeps a whole picture of red, then of green, then of blue, rows from the top.
  The base picture is 128 texels square. A second base picture in the binder looks to be a
  hollow's.
- With weights fitted freely the frame's skin picture is reproduced to under one level of 255
  everywhere, so the form is right. With the rule's weights it is within one level on average
  and six at most for the warrior, and three and ten for the other person: the fitted weights
  are a few hundredths smaller than the rule's, more so for later modes. Truncating or rounding
  each term, clamping as it goes, a common shrink and fixed-point weights were each tried and
  none explains it. The rule's weights are used.
- The face is drawn from two pictures. The skin is found by a vertex's first coordinates; a
  detail picture every face shares (pores, stubble, an ear) is found by its second. The colour
  is the skin moved towards the detail by the vertex's fourth colour value, where the detail
  has first had a shift added. The relief and specular maps are mixed the same way.
- The same shift is added to the colour of the rest of a person's skin — the head model and the
  bare body's — so that it meets the face. In the frame it is, for both people, exactly how far
  the top right texel of the person's skin picture has moved from the base picture's, as a
  fraction of 255.

## Hair and eyes

- Hair's colour map is multiplied by the person's hair colour (as a fraction of 255) and by a
  darkening kept at each vertex; the vertex stage does it. Most of a head of hair is sheets the
  source blends over what is behind them by the map's coverage.
- An eye's picture marks its iris in its fourth value; there the colour is multiplied by the
  person's eye colour.

## What the build does

- A person wears the hair its face row's style names and the face front, bent by its face row.
- A build makes textures for each person: the face's colour as one picture in the detail
  picture's layout (the skin laid in by rasterising the face's triangles), the head's and the
  bare body's colour with the shift added, and the eyes tinted. They are decoded from and
  encoded to block formats by code restored from the history, where it was removed as unused
  the day before.
- Hair is tinted through its material definition's colour.

Seen in the game beside the source's picture, the seated warrior's face and hair are his.

Ceilings, each marked in the code:

- Blended hair is cut out at half coverage, so its soft ends are hard-edged.
- The darkening at each vertex of hair is not drawn, and the definition's specular power (hair
  has five where most have one) is not read.
- The face takes the detail layer's relief and specular maps alone.
- The two sides of a face share the detail picture's texels, so one side of the skin is laid
  on both.
- Only a man's part models are chosen; a woman is refused.
- The body's proportions, which a start-up row also sets, are not read.

## Sources and licences

- Everything about the source's files and behaviour comes from read-only inspection of Riley's
  install with the tool's own readers, and from one frame of the source game.
- RenderDoc 1.46 (MIT licence) captured and replayed the frame, privately; none of its code was
  read or incorporated, and nothing of the capture is in the repository.
- The shaping, colouring and mesh files carry the tags of a commercial face-generation kit
  (FaceGen, Singular Inversions), whose maker documents those file formats publicly. The
  layouts here were recalled from that documentation rather than re-read, then confirmed
  against the files' own lengths and counts and against the frame. The documentation's terms
  were not checked; only the formats' facts are restated, in the tool's own words, and no code
  of the kit was read.
- NumPy 2.5 (BSD-3-Clause) was used privately to fit weights; none of its code is incorporated.
