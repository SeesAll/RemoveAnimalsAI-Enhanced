# Changelog

All notable changes to this project are documented here.

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
