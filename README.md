# Age of Gold

A Dark Souls mod for Elden Ring. It rebuilds Dark Souls's world and game in Elden Ring's engine. It
is a mod, not a remake: this repository holds none of either game's files. A tool you run on your
own machine reads your installed copies of both games and writes the mod from them, so you need to
own and install both.

A private Firelink playtest is working. The maintained reproducible builder is in development. [`docs/core.md`](docs/core.md) holds what Age of Gold is, the rules it
keeps, the decisions still open and the roadmap. This file will show how to build and play each
part as it lands.

## Playing safely

The mod runs only with Elden Ring offline and its anti-cheat off. Never take a modded game
online. Age of Gold never changes either game's installed files; removing it is deleting the
directory it wrote.

## Development

The maintained builder uses C#/.NET, SoulsFormatsNEXT and HKLib. The existing `spikes/`
checks and private tools are being consolidated; they are not yet a clean install-to-build
workflow. See [the consolidation checkpoint](docs/firelink-consolidation.md).

## Licence

GNU GPL version 3 ([GPL-3.0-only](LICENSE)). See [`NOTICE`](NOTICE) for trademarks and the
clean-room posture. The licence covers the tool, not either game or locally generated builds.
