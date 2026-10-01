# Changelog

All notable changes to this project are documented here.

## [2.1.0] - 2026-10-01

### Added

- Merge-safe `Never Manage Prefab Name Contains` exclusions for custom, event, and human NPC compatibility.
- Startup repair for configured-enabled animals left dormant by older all-animal plugin versions.
- Enabled, disabled, and unknown totals plus prefab/runtime-type details in the on-demand status report.
- Explicit death-transition bypass tracking so both AI-enabled and AI-disabled animals complete Rust's vanilla corpse and harvesting pipeline.
- Startup warnings when a configured-disabled animal uses an unrecognized AI controller that the plugin cannot safely pause.

### Changed

- Cached policy results for enabled animals as well as disabled animals, reducing repeated classification in Harmony hot paths.
- Documented Rust's `simpleshark.disable` server convar and the current safe-warning behavior for unsupported shark AI control.
- Cached the operating mode and normalized exclusion tokens when configuration loads.
- Advanced the merge-safe configuration schema to version 2.
- Added a fast spawn-candidate filter so buildings, items, deployables, and scenery do not create needless next-tick callbacks.
- Restricted fallback animal discovery to animal-specific components instead of generic `BaseNPC2` entities.
- Restores each animal's original brain, FSM, queue, and legacy TickAi timer state when the plugin unloads.

### Fixed

- Prevented livestock and other configured-enabled species from remaining frozen after upgrading from an older all-animal version.
- Prevented partial prefab-name matches such as `dashboard`/`boar` and airwolf spawners/`wolf` from being classified as animals without matching runtime evidence.
- Prevented plugin unload or Vanilla-mode switching from blindly activating animals that were already dormant for another reason.
- Prevented legacy `BaseNpc` animals from remaining frozen after the plugin is unloaded.
- Prevented queued config-save callbacks from writing during plugin unload.
- Removed stale entity references from all runtime caches reliably during entity destruction.
- Canonicalized invalid or inconsistently-cased mode values without duplicating configuration entries.

## [2.0.0] - 2026-10-01

### Added

- Selective, DisableAll, and Vanilla operating modes.
- Configurable predator, prey, livestock, mount, aquatic-predator, and decorative-critter categories.
- Nullable per-animal overrides that inherit their category when unset.
- Coverage for Jungle and Livestock Update animals, including panthers, tigers, crocodiles, snakes, cows/bulls, sheep, frogs, rabbits, squirrels, sea turtles, crabs, and jellyfish.
- Safe discovery and logging of future animal prefabs.
- Admin `/animalai` and server/RCON `removeanimalsai.status` reports.
- Upgrade-safe configuration merging with custom-field preservation and case-insensitive duplicate prevention.

### Changed

- Updated FSM handling for Rust's instance-owned AI work queues.
- Animal brains are initialized normally and then selectively paused instead of being destroyed.
- Human NPC identification is explicitly guarded so scientists, cavemen, vendors, event NPCs, and similar entities retain normal AI.
- Startup classification caches disabled animals for lightweight runtime checks.
- Plugin attribution now credits the original developer and identifies the SeesAll modifications.

### Fixed

- Restored normal death-state processing so AI-disabled animals create harvestable corpses.
- Avoided the `boneReference is null` condition caused by skipping animal-brain initialization.
- Preserved and restored animal FSM state when the plugin is unloaded.
- Prevented invalid configuration JSON from being overwritten by defaults.
