# Age of Gold — agent guide

Age of Gold is a Dark Souls mod for Elden Ring: it rebuilds Dark Souls's world and game in Elden
Ring's engine, reading both games from the player's own installs. Riley is the owner. The name
never includes either game's name; they appear only to describe what the mod works with.

Read `docs/core.md` first: goal, scope, proof, principles, constraints, open decisions, language
and roadmap. Use its language exactly, including the words it rules out. Then read `HANDOFF.md`,
if it exists. It says where the last session stopped and lists Riley's standing decisions.

- **Design first.** Riley decides and the agent writes. Propose one decision at a time, with a
  recommendation. Once Riley agrees, write the code test first, and see each test fail for the
  right reason. Report the choices Riley did not see.
- **Can't classify something?** Stop and ask Riley.
- **Specs:** a sentence in `docs/core.md` without a test is a proposal. Its Open decisions are
  settled in order unless Riley says otherwise.
- **Nothing from either game enters the repository** (constraint 1): no game files, extracted or
  converted output, decompiled code, quoted text, hex dumps of real files, or screenshots, in
  commits, tests, docs, commit messages or `HANDOFF.md`. Describe a format in our own words; a
  fixture is written from scratch or produced from an install at test time. If something from a
  game was committed, stop and tell Riley before anything is pushed.
- **Installs are read-only.** Never write into, patch or "fix" a game install, and never run a
  game online with a build loaded.
- **Research:** `docs/research/` holds dated snapshots of what was learnt about formats, tools and
  prior art; they are inputs, not specs. Record each source's licence. Never copy code from a
  community tool without a logged decision that its licence allows it.
- **Checks:** none yet; the first code decision sets them.
- **The plan** named by the global principles is `docs/core.md`. The format families' readers and
  writers are the named extension points. Constraints 1–4 (nothing from either game, both
  installs required, installs read-only, offline only) and deterministic builds are the
  cross-cutting constraints allowed early.
- **Tests** that need the games read the installs from environment variables, which the first
  code decision names, and write only under the system temp directory. They never run in CI.
- **Handoff:** a session that ends with work in flight writes `HANDOFF.md`: where it stopped,
  what's next, and Riley's standing decisions from the session.
- **Logged decisions** go in the commit that makes them, as a `Decision:` trailer, so
  `git log --grep '^Decision:'` lists them.
- **Git, overriding the global workflow:** branches stay local; push only when Riley asks.
  Riley merges.
