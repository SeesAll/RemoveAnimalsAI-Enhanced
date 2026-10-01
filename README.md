# Remove Animals AI Enhanced

An enhanced, configurable version of **Remove Animals AI** for Rust servers running Oxide/uMod.

The original plugin by **Whipers88 @CobaltStudios** disabled AI for every animal while leaving scientists and other human NPCs alone. This enhanced edition preserves that lightweight behavior, adds October 2026 Rust compatibility, and lets server owners decide which animal groups or individual species keep their AI.

## Features

- Selective animal AI control without disabling roadside, monument, event, or other human NPC AI.
- Category controls for predators, prey, livestock, mounts, aquatic predators, and decorative critters.
- Per-animal overrides for every supported species.
- Support for the Jungle and Livestock Update animals.
- Normal animal death and harvestable corpses remain functional when AI is disabled.
- Repairs configured-enabled animals that were left dormant by older all-animal plugin versions.
- Automatic discovery of future animal prefabs, with a safe configurable default.
- Configurable prefab exclusions protect custom and event NPCs from animal-AI management.
- Upgrade-safe configuration merging that preserves existing choices and custom entries.
- Case-insensitive key normalization prevents duplicate entries such as `Wolf` and `wolf`.
- Read-only status commands for live animal counts and effective AI states.

## Supported animals

| Category | Animals | Default |
| --- | --- | --- |
| Predators | Bear, Polar Bear, Wolf, Panther, Tiger, Crocodile, Snake | AI disabled |
| Prey | Boar, Deer | AI disabled |
| Livestock | Chicken, Cow/Bull, Sheep | AI enabled |
| Mounts | Horse | AI enabled |
| Aquatic Predators | Shark | AI disabled |
| Decorative Critters | Crab, Frog, Jellyfish, Rabbit, Sea Turtle, Squirrel | AI enabled |

Unknown future animals default to AI enabled so a Rust update cannot unexpectedly freeze a new entity. Change `Unknown Animal AI Enabled` if you prefer the opposite policy.

### Shark compatibility

Rust controls SimpleShark globally with the boolean server convar `simpleshark.disable`; it does not provide a `shark.population` or `simpleshark.population` convar. The current shark entity does not expose one of the AI controllers this plugin can safely pause. If sharks are enabled at the server level while this plugin is configured to disable Shark AI, the plugin logs a compatibility warning instead of making an unsafe change. Servers that do not want sharks should use `simpleshark.disable true` in `server.cfg`.

`Never Manage Prefab Name Contains` is an additional safety boundary. Matching entities are never classified or modified by the plugin, even if unknown-animal AI is disabled. Custom event or NPC prefab tokens can be added to this list and will be preserved during updates.

## Installation

1. Place `RemoveAnimalsAI.cs` in `oxide/plugins`.
2. Allow the plugin to compile and create `oxide/config/RemoveAnimalsAI.json`.
3. Edit the configuration as desired.
4. Reload with `oxide.reload RemoveAnimalsAI`.

When replacing an older version that destroyed animal brain components, a normal server restart is recommended once so existing saved animals are reconstructed cleanly. This enhanced version does not destroy their brains.

## Configuration

```json
{
  "Configuration Version": 2,
  "Mode (Selective, DisableAll, or Vanilla)": "Selective",
  "Unknown Animal AI Enabled": true,
  "Log Newly Discovered Animal Prefabs": true,
  "Repair Enabled Animal Controllers On Plugin Load": true,
  "Never Manage Prefab Name Contains": [
    "scientist",
    "murderer",
    "scarecrow",
    "caveman",
    "npcplayer",
    "bandit",
    "outbreak",
    "dweller",
    "peacekeeper",
    "vendor"
  ],
  "Category AI Enabled": {
    "Predators": false,
    "Prey": false,
    "Livestock": true,
    "Mounts": true,
    "Aquatic Predators": false,
    "Decorative Critters": true
  },
  "Per-Animal AI Overrides (null uses category setting)": {
    "Polar Bear": null,
    "Panther": null,
    "Tiger": null,
    "Crocodile": null,
    "Snake": null,
    "Wolf": null,
    "Bear": null,
    "Boar": null,
    "Deer": null,
    "Chicken": null,
    "Cow": null,
    "Sheep": null,
    "Horse": null,
    "Shark": null,
    "Sea Turtle": null,
    "Jellyfish": null,
    "Squirrel": null,
    "Rabbit": null,
    "Frog": null,
    "Crab": null
  }
}
```

### Modes

- `Selective` — category settings and per-animal overrides control AI.
- `DisableAll` — disables the animal AI queue globally, matching the original plugin's broad behavior.
- `Vanilla` — leaves animal AI enabled while retaining the plugin's discovery and reporting tools.

### Per-animal overrides

Every entry under `Per-Animal AI Overrides` can use one of three values:

| Value | Effect |
| --- | --- |
| `null` | Inherits the current setting for that animal's category. |
| `true` | Forces AI on for that species, even when its category is disabled. |
| `false` | Forces AI off for that species, even when its category is enabled. |

For example, when `Predators` is `false`, setting `"Panther": true` enables panther AI while the other predators remain disabled. When `Prey` is `true`, setting `"Boar": false` disables boar AI while deer continue using the enabled Prey category.

Changing an override back to `null` returns that species to its category setting. Reload the plugin after editing the configuration; configured-enabled animals are repaired on load, so switching a species from disabled to enabled does not require a server restart.

`Repair Enabled Animal Controllers On Plugin Load` defaults to `true`. It repairs enabled species that an older version left dormant, without activating any species that is still configured as disabled.

### Safe configuration updates

On an update, the plugin adds only missing settings. Existing values and administrator-added entries are retained. Keys are compared without regard to capitalization, preventing a newly introduced default from creating a case-only duplicate. An unchanged config is not rewritten during a normal reload, and malformed JSON is left untouched so it can be recovered manually.

## Commands

- Server/RCON console: `removeanimalsai.status`
- Admin chat: `/animalai` — writes the detailed report to the admin's F1 console.

The report shows the active mode, enabled/disabled/unknown totals, species counts, categories, effective AI states, actual prefab names, and runtime entity types. The runtime details are useful when Facepunch introduces new animals.

## Performance notes

The plugin performs its full entity classification when it loads and only lightweight cached checks during normal play. Spawn handling rejects non-animal entities before scheduling deferred work, and both enabled and disabled animal decisions are cached. It does not run a repeating world scan. The detailed status scan runs only when an administrator requests it.

Selective control can provide part of the CPU benefit of disabling all animal AI. The exact gain depends on which categories remain active and how many animals exist on the map. `DisableAll` remains the lowest-overhead choice, while `Vanilla` provides no AI reduction.

## Credits and license

- Original plugin: **Remove Animals AI** by **Whipers88 @CobaltStudios**
- Enhanced edition and October 2026 compatibility: **SeesAll**

Licensed under the [MIT License](LICENSE), consistent with the original project.
