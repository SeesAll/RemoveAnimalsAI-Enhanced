// MIT License
// Original plugin by Whipers88 @CobaltStudios.
// Enhanced selective-animal controls and October 2026 compatibility by SeesAll.

using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core.Plugins;
using Rust.Ai.Gen2;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Oxide.Plugins
{
    [Info("Remove Animals AI", "Whipers88 @CobaltStudios, modified by SeesAll", "2.1.0")]
    [Description("Selectively disables animal AI while leaving human NPC AI untouched.")]
    public class RemoveAnimalsAI : RustPlugin
    {
        private const int CurrentConfigurationVersion = 2;

        private const string ModeSelective = "Selective";
        private const string ModeDisableAll = "DisableAll";
        private const string ModeVanilla = "Vanilla";

        private const string CategoryPredators = "Predators";
        private const string CategoryPrey = "Prey";
        private const string CategoryLivestock = "Livestock";
        private const string CategoryMounts = "Mounts";
        private const string CategoryAquaticPredators = "Aquatic Predators";
        private const string CategoryDecorativeCritters = "Decorative Critters";
        private const string CategoryUnknown = "Unknown";

        private static RemoveAnimalsAI Instance;

        private Configuration _config;
        private readonly Dictionary<BaseEntity, bool> _animalPolicyCache = new Dictionary<BaseEntity, bool>();
        private readonly Dictionary<BaseEntity, DisabledAnimalState> _disabledAnimalStates =
            new Dictionary<BaseEntity, DisabledAnimalState>();
        private readonly HashSet<BaseEntity> _deathProcessingEntities = new HashSet<BaseEntity>();
        private readonly HashSet<string> _unsupportedControllerWarnings =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string[] _excludedPrefabTokens = new string[0];
        private string _activeMode = ModeSelective;
        private bool _configSaveQueued;
        private bool _shuttingDown;
        private bool _unloading;

        private sealed class DisabledAnimalState
        {
            public bool HadBrain;
            public bool BrainEnabled;
            public bool HadFsm;
            public bool FsmRunning;
            public bool WasQueued;
            public bool HadLegacyNpc;
            public bool LegacyTickWasInvoking;
        }

        private sealed class SpeciesDefinition
        {
            public readonly string Name;
            public readonly string Category;
            public readonly string[] MatchTokens;

            public SpeciesDefinition(string name, string category, params string[] matchTokens)
            {
                Name = name;
                Category = category;
                MatchTokens = matchTokens.Select(NormalizeName).ToArray();
            }
        }

        // More specific names must precede broader matches such as Bear and Turtle.
        private static readonly SpeciesDefinition[] KnownSpecies =
        {
            new SpeciesDefinition("Polar Bear", CategoryPredators, "polarbear", "polar_bear"),
            new SpeciesDefinition("Panther", CategoryPredators, "panther"),
            new SpeciesDefinition("Tiger", CategoryPredators, "tiger"),
            new SpeciesDefinition("Crocodile", CategoryPredators, "crocodile", "croc"),
            new SpeciesDefinition("Snake", CategoryPredators, "snake"),
            new SpeciesDefinition("Wolf", CategoryPredators, "wolf"),
            new SpeciesDefinition("Bear", CategoryPredators, "bear"),

            new SpeciesDefinition("Boar", CategoryPrey, "boar", "wildpig", "wild_pig"),
            new SpeciesDefinition("Deer", CategoryPrey, "stag", "deer"),

            new SpeciesDefinition("Chicken", CategoryLivestock, "chicken"),
            new SpeciesDefinition("Cow", CategoryLivestock, "cow", "cattle", "bull"),
            new SpeciesDefinition("Sheep", CategoryLivestock, "sheep"),

            new SpeciesDefinition("Horse", CategoryMounts, "horse"),
            new SpeciesDefinition("Shark", CategoryAquaticPredators, "shark"),

            new SpeciesDefinition("Sea Turtle", CategoryDecorativeCritters, "seaturtle", "sea_turtle"),
            new SpeciesDefinition("Jellyfish", CategoryDecorativeCritters, "jellyfish"),
            new SpeciesDefinition("Squirrel", CategoryDecorativeCritters, "squirrel"),
            new SpeciesDefinition("Rabbit", CategoryDecorativeCritters, "rabbit"),
            new SpeciesDefinition("Frog", CategoryDecorativeCritters, "frog"),
            new SpeciesDefinition("Crab", CategoryDecorativeCritters, "crab")
        };

        private sealed class Configuration
        {
            [JsonProperty("Configuration Version")]
            public int ConfigurationVersion = CurrentConfigurationVersion;

            [JsonProperty("Mode (Selective, DisableAll, or Vanilla)")]
            public string Mode = ModeSelective;

            [JsonProperty("Unknown Animal AI Enabled")]
            public bool UnknownAnimalAiEnabled = true;

            [JsonProperty("Log Newly Discovered Animal Prefabs")]
            public bool LogNewlyDiscoveredAnimalPrefabs = true;

            [JsonProperty("Repair Enabled Animal Controllers On Plugin Load")]
            public bool RepairEnabledAnimalControllersOnPluginLoad = true;

            [JsonProperty("Never Manage Prefab Name Contains")]
            public List<string> NeverManagePrefabNameContains = CreateDefaultExcludedPrefabTokens();

            [JsonProperty("Category AI Enabled")]
            public Dictionary<string, bool> CategoryAiEnabled = CreateDefaultCategories();

            [JsonProperty("Per-Animal AI Overrides (null uses category setting)")]
            public Dictionary<string, bool?> AnimalAiOverrides = CreateDefaultOverrides();

            // Preserve future or administrator-added top-level fields when the
            // typed configuration is read and written by an older plugin build.
            [JsonExtensionData]
            public IDictionary<string, JToken> AdditionalData;

            public static Configuration CreateDefault()
            {
                return new Configuration();
            }

            public bool MergeDefaults()
            {
                bool changed = false;

                if (ConfigurationVersion < CurrentConfigurationVersion)
                {
                    ConfigurationVersion = CurrentConfigurationVersion;
                    changed = true;
                }

                if (string.IsNullOrWhiteSpace(Mode))
                {
                    Mode = ModeSelective;
                    changed = true;
                }
                else
                {
                    string canonicalMode = CanonicalizeMode(Mode);
                    if (!string.Equals(Mode, canonicalMode, StringComparison.Ordinal))
                    {
                        Mode = canonicalMode;
                        changed = true;
                    }
                }

                CategoryAiEnabled = NormalizeDictionary(CategoryAiEnabled, ref changed);
                AnimalAiOverrides = NormalizeDictionary(AnimalAiOverrides, ref changed);
                NeverManagePrefabNameContains = NormalizeList(NeverManagePrefabNameContains, ref changed);

                foreach (string token in CreateDefaultExcludedPrefabTokens())
                {
                    if (!NeverManagePrefabNameContains.Contains(token, StringComparer.OrdinalIgnoreCase))
                    {
                        NeverManagePrefabNameContains.Add(token);
                        changed = true;
                    }
                }

                foreach (KeyValuePair<string, bool> entry in CreateDefaultCategories())
                {
                    if (!CategoryAiEnabled.ContainsKey(entry.Key))
                    {
                        CategoryAiEnabled.Add(entry.Key, entry.Value);
                        changed = true;
                    }
                }

                foreach (KeyValuePair<string, bool?> entry in CreateDefaultOverrides())
                {
                    if (!AnimalAiOverrides.ContainsKey(entry.Key))
                    {
                        AnimalAiOverrides.Add(entry.Key, entry.Value);
                        changed = true;
                    }
                }

                return changed;
            }

            private static string CanonicalizeMode(string value)
            {
                string configured = (value ?? string.Empty).Trim();
                if (configured.Equals(ModeDisableAll, StringComparison.OrdinalIgnoreCase))
                {
                    return ModeDisableAll;
                }
                if (configured.Equals(ModeVanilla, StringComparison.OrdinalIgnoreCase))
                {
                    return ModeVanilla;
                }
                return ModeSelective;
            }

            private static Dictionary<string, bool> CreateDefaultCategories()
            {
                return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                {
                    [CategoryPredators] = false,
                    [CategoryPrey] = false,
                    [CategoryLivestock] = true,
                    [CategoryMounts] = true,
                    [CategoryAquaticPredators] = false,
                    [CategoryDecorativeCritters] = true
                };
            }

            private static Dictionary<string, bool?> CreateDefaultOverrides()
            {
                Dictionary<string, bool?> result = new Dictionary<string, bool?>(StringComparer.OrdinalIgnoreCase);
                foreach (SpeciesDefinition species in KnownSpecies)
                {
                    result[species.Name] = null;
                }
                return result;
            }

            private static List<string> CreateDefaultExcludedPrefabTokens()
            {
                return new List<string>
                {
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
                };
            }

            private static List<string> NormalizeList(IEnumerable<string> source, ref bool changed)
            {
                List<string> normalized = new List<string>();
                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (source == null)
                {
                    changed = true;
                    return normalized;
                }

                foreach (string value in source)
                {
                    string token = value == null ? string.Empty : value.Trim();
                    if (token.Length == 0 || !seen.Add(token))
                    {
                        changed = true;
                        continue;
                    }

                    if (!string.Equals(token, value, StringComparison.Ordinal))
                    {
                        changed = true;
                    }
                    normalized.Add(token);
                }

                return normalized;
            }

            private static Dictionary<string, TValue> NormalizeDictionary<TValue>(IDictionary<string, TValue> source, ref bool changed)
            {
                Dictionary<string, TValue> normalized = new Dictionary<string, TValue>(StringComparer.OrdinalIgnoreCase);
                if (source == null)
                {
                    changed = true;
                    return normalized;
                }

                foreach (KeyValuePair<string, TValue> entry in source)
                {
                    string key = entry.Key == null ? string.Empty : entry.Key.Trim();
                    if (key.Length == 0)
                    {
                        changed = true;
                        continue;
                    }

                    if (normalized.ContainsKey(key))
                    {
                        // JSON dictionaries are normally case-sensitive. Collapse
                        // entries such as "Wolf" and "wolf" deterministically so
                        // subsequent reloads cannot create a second setting.
                        changed = true;
                        continue;
                    }

                    if (!string.Equals(key, entry.Key, StringComparison.Ordinal))
                    {
                        changed = true;
                    }
                    normalized.Add(key, entry.Value);
                }
                return normalized;
            }
        }

        protected override void LoadDefaultConfig()
        {
            _config = Configuration.CreateDefault();
            RefreshRuntimeConfigCache();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            bool configIsValid = true;

            try
            {
                _config = Config.ReadObject<Configuration>();
                if (_config == null)
                {
                    throw new JsonException("Configuration deserialized to null.");
                }
            }
            catch (Exception exception)
            {
                PrintError($"Configuration could not be read: {exception.Message}");
                PrintWarning("Loading safe defaults in memory. The invalid configuration file was left untouched for recovery.");
                _config = Configuration.CreateDefault();
                configIsValid = false;
            }

            bool changed = _config.MergeDefaults();
            if (changed && configIsValid)
            {
                PrintWarning("Configuration was merged with newly available settings; existing values were preserved.");
                SaveConfig();
            }

            RefreshRuntimeConfigCache();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(_config, true);
        }

        private void RefreshRuntimeConfigCache()
        {
            string configuredMode = (_config.Mode ?? string.Empty).Trim();
            _activeMode = configuredMode.Equals(ModeDisableAll, StringComparison.OrdinalIgnoreCase)
                ? ModeDisableAll
                : configuredMode.Equals(ModeVanilla, StringComparison.OrdinalIgnoreCase)
                    ? ModeVanilla
                    : ModeSelective;

            _excludedPrefabTokens = _config.NeverManagePrefabNameContains
                .Select(NormalizeName)
                .Where(token => token.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private void Init()
        {
            Instance = this;
        }

        private void OnServerInitialized()
        {
            int animals = 0;
            int disabled = 0;

            foreach (BaseNetworkable networkable in BaseNetworkable.serverEntities)
            {
                BaseEntity entity = networkable as BaseEntity;
                if (entity == null || !TryGetAnimalIdentity(entity, out string species, out string category))
                {
                    continue;
                }

                animals++;
                EnsureAnimalConfigEntry(species, entity.ShortPrefabName, category);
                bool aiDisabled = ApplyAnimalPolicy(
                    entity,
                    species,
                    category,
                    _config.RepairEnabledAnimalControllersOnPluginLoad);
                if (aiDisabled)
                {
                    disabled++;
                }
                WarnIfUnsupportedController(entity, species, aiDisabled);
            }

            Puts($"Mode={GetMode()}. Detected {animals} animal(s); AI disabled for {disabled}.");
        }

        private static bool HasSupportedAiController(BaseEntity entity)
        {
            return entity is BaseNpc ||
                   entity.GetComponent<FSMComponent>() != null ||
                   entity.GetComponent<BaseAIBrain>() != null;
        }

        private void WarnIfUnsupportedController(BaseEntity entity, string species, bool aiDisabled)
        {
            if (!aiDisabled || GetMode() != ModeSelective || HasSupportedAiController(entity) ||
                !_unsupportedControllerWarnings.Add(species))
            {
                return;
            }

            PrintWarning($"Configured-disabled animal '{species}' uses no recognized AI controller and may " +
                         "require a compatibility update.");
        }

        private void OnServerShutdown()
        {
            _shuttingDown = true;
        }

        private void Unload()
        {
            _unloading = true;

            // Let restoration calls pass through our own Harmony prefixes.
            Instance = null;

            if (!_shuttingDown)
            {
                RestoreDisabledAnimals();
            }

            _disabledAnimalStates.Clear();
            _animalPolicyCache.Clear();
            _deathProcessingEntities.Clear();
            _unsupportedControllerWarnings.Clear();
        }

        private void OnEntitySpawned(BaseNetworkable networkable)
        {
            BaseEntity entity = networkable as BaseEntity;
            if (entity == null || !IsPotentialAnimalCandidate(entity))
            {
                return;
            }

            NextTick(() =>
            {
                if (_unloading || entity == null || entity.IsDestroyed ||
                    !TryGetAnimalIdentity(entity, out string species, out string category))
                {
                    return;
                }

                EnsureAnimalConfigEntry(species, entity.ShortPrefabName, category);
                bool aiDisabled = ApplyAnimalPolicy(entity, species, category);
                WarnIfUnsupportedController(entity, species, aiDisabled);
            });
        }

        private bool IsPotentialAnimalCandidate(BaseEntity entity)
        {
            if (entity == null || entity is BasePlayer || entity is ScientistNPC2)
            {
                return false;
            }

            string normalized = NormalizeName(entity.ShortPrefabName);
            if (normalized.Length == 0 || IsExcludedEntityName(normalized) || IsHumanNpcName(normalized))
            {
                return false;
            }

            if (FindKnownSpecies(entity, normalized) != null)
            {
                return true;
            }

            return entity is BaseAnimalNPC ||
                   entity is BaseNpc ||
                   entity.GetComponent<AnimalBrain>() != null;
        }

        private void OnEntityKill(BaseNetworkable networkable)
        {
            BaseEntity entity = networkable as BaseEntity;
            if (!ReferenceEquals(entity, null))
            {
                _disabledAnimalStates.Remove(entity);
                _animalPolicyCache.Remove(entity);
                _deathProcessingEntities.Remove(entity);
            }
        }

        private void BeginDeathProcessing(BaseEntity entity)
        {
            if (entity == null || !_deathProcessingEntities.Add(entity))
            {
                return;
            }

            // OnEntityKill normally removes this immediately after the corpse
            // is created. The timeout prevents an unexpected modded death flow
            // from retaining a dead entity reference indefinitely.
            timer.Once(60f, () => _deathProcessingEntities.Remove(entity));
        }

        private bool ApplyAnimalPolicy(
            BaseEntity entity,
            string species = null,
            string category = null,
            bool repairEnabledController = false)
        {
            if (entity == null || entity.IsDestroyed)
            {
                return false;
            }

            // Once a disabled animal dies, Rust must be allowed to run its
            // normal death FSM so the harvestable corpse and yields are made.
            if (_deathProcessingEntities.Contains(entity))
            {
                return false;
            }

            if (species == null && !TryGetAnimalIdentity(entity, out species, out category))
            {
                return false;
            }

            if (IsAiEnabled(species, category))
            {
                _animalPolicyCache[entity] = false;
                if (repairEnabledController)
                {
                    RepairEnabledAnimalController(entity);
                }
                return false;
            }

            _animalPolicyCache[entity] = true;

            if (!_disabledAnimalStates.TryGetValue(entity, out DisabledAnimalState state))
            {
                state = new DisabledAnimalState();
                _disabledAnimalStates.Add(entity, state);
            }

            // Components can be added at different points in Rust's spawn
            // pipeline. Capture each original state the first time it exists.
            if (!state.HadBrain && entity.TryGetComponent<BaseAIBrain>(out BaseAIBrain existingBrain))
            {
                state.HadBrain = true;
                state.BrainEnabled = existingBrain.enabled;
            }

            if (!state.HadFsm && entity.TryGetComponent<FSMComponent>(out FSMComponent existingFsm))
            {
                state.HadFsm = true;
                state.FsmRunning = existingFsm.isRunning;
                state.WasQueued = existingFsm.workQueue != null && existingFsm.workQueue.Contains(existingFsm);
            }

            BaseNpc legacyNpc = entity as BaseNpc;
            if (legacyNpc != null)
            {
                if (!state.HadLegacyNpc)
                {
                    state.HadLegacyNpc = true;
                    state.LegacyTickWasInvoking = legacyNpc.IsInvoking(legacyNpc.TickAi);
                }
                legacyNpc.CancelInvoke(legacyNpc.TickAi);
            }

            if (entity.TryGetComponent<FSMComponent>(out FSMComponent fsm))
            {
                fsm.SetFsmActive(false);
                fsm.isRunning = false;
                if (fsm.workQueue != null && fsm.workQueue.Contains(fsm))
                {
                    fsm.workQueue.Remove(fsm);
                }
            }

            if (entity.TryGetComponent<BaseAIBrain>(out BaseAIBrain brain))
            {
                brain.enabled = false;
            }

            return true;
        }

        private static void RepairEnabledAnimalController(BaseEntity entity)
        {
            // Older releases disabled every animal without restoring the
            // legacy TickAi schedule when they were replaced. Repairing only
            // configured-enabled animals on plugin load makes that migration
            // reversible without affecting species that remain disabled.
            if (entity.TryGetComponent<BaseAIBrain>(out BaseAIBrain brain) && !brain.enabled)
            {
                brain.enabled = true;
            }

            if (entity.TryGetComponent<FSMComponent>(out FSMComponent fsm))
            {
                if (!fsm.isRunning)
                {
                    fsm.SetFsmActive(true);
                    fsm.isRunning = true;
                }

                if (fsm.workQueue != null && !fsm.workQueue.Contains(fsm))
                {
                    fsm.workQueue.Add(fsm);
                }
            }

            BaseNpc legacyNpc = entity as BaseNpc;
            if (legacyNpc != null && !legacyNpc.IsInvoking(legacyNpc.TickAi))
            {
                legacyNpc.InvokeRandomized(legacyNpc.TickAi, 0.1f, 0.1f, 0.005f);
            }
        }

        private bool ShouldDisable(BaseEntity entity)
        {
            if (entity == null || entity.IsDestroyed)
            {
                return false;
            }

            if (_deathProcessingEntities.Contains(entity))
            {
                return false;
            }

            if (_animalPolicyCache.TryGetValue(entity, out bool disabledByPolicy))
            {
                return disabledByPolicy;
            }

            if (!TryGetAnimalIdentity(entity, out string species, out string category))
            {
                return false;
            }

            if (IsAiEnabled(species, category))
            {
                _animalPolicyCache[entity] = false;
                return false;
            }

            return ApplyAnimalPolicy(entity, species, category);
        }

        private bool IsAiEnabled(string species, string category)
        {
            string mode = GetMode();
            if (mode == ModeVanilla)
            {
                return true;
            }
            if (mode == ModeDisableAll)
            {
                return false;
            }

            if (_config.AnimalAiOverrides.TryGetValue(species, out bool? animalOverride) && animalOverride.HasValue)
            {
                return animalOverride.Value;
            }

            if (category.Equals(CategoryUnknown, StringComparison.OrdinalIgnoreCase))
            {
                return _config.UnknownAnimalAiEnabled;
            }

            if (_config.CategoryAiEnabled.TryGetValue(category, out bool categoryEnabled))
            {
                return categoryEnabled;
            }

            return _config.UnknownAnimalAiEnabled;
        }

        private string GetMode()
        {
            return _activeMode;
        }

        private bool TryGetAnimalIdentity(BaseEntity entity, out string species, out string category)
        {
            species = null;
            category = null;

            if (entity == null || entity is BasePlayer || entity is ScientistNPC2)
            {
                return false;
            }

            string prefab = entity.ShortPrefabName ?? string.Empty;
            string normalized = NormalizeName(prefab);
            if (normalized.Length == 0 || IsExcludedEntityName(normalized) || IsHumanNpcName(normalized))
            {
                return false;
            }

            // Some current Rust animals (notably horses and snakes) no longer
            // share the legacy animal component hierarchy. Explicit prefab
            // recognition must therefore happen before the component fallback.
            SpeciesDefinition definition = FindKnownSpecies(entity, normalized);
            if (definition != null)
            {
                species = definition.Name;
                category = definition.Category;
                return true;
            }

            bool animalComponent = entity is BaseAnimalNPC ||
                                   entity is BaseNpc ||
                                   entity.GetComponent<AnimalBrain>() != null;

            if (!animalComponent)
            {
                return false;
            }

            species = prefab;
            category = CategoryUnknown;
            return true;
        }

        private static SpeciesDefinition FindKnownSpecies(BaseEntity entity, string normalizedPrefab)
        {
            string normalizedRuntimeType = NormalizeName(entity?.GetType().Name);
            bool hasAnimalController = entity is BaseAnimalNPC ||
                                       entity is BaseNpc ||
                                       entity?.GetComponent<AnimalBrain>() != null;

            foreach (SpeciesDefinition definition in KnownSpecies)
            {
                foreach (string token in definition.MatchTokens)
                {
                    // Require both a matching prefab name and animal-like
                    // runtime evidence. This prevents names such as
                    // "dashboard" (contains "boar") and airwolf spawners from
                    // being managed as animals.
                    if (normalizedPrefab.Contains(token) &&
                        (normalizedRuntimeType.Contains(token) || hasAnimalController))
                    {
                        return definition;
                    }
                }
            }
            return null;
        }

        private static string NormalizeName(string value)
        {
            return (value ?? string.Empty)
                .Trim()
                .ToLowerInvariant()
                .Replace("_", string.Empty)
                .Replace("-", string.Empty)
                .Replace(".", string.Empty)
                .Replace(" ", string.Empty);
        }

        private static bool IsExcludedEntityName(string normalizedName)
        {
            return normalizedName.Contains("corpse") ||
                   normalizedName.Contains("ragdoll") ||
                   normalizedName.Contains("gib") ||
                   normalizedName.Contains("carcass") ||
                   normalizedName.Contains("head") ||
                   normalizedName.Contains("trophy");
        }

        private bool IsHumanNpcName(string normalizedName)
        {
            foreach (string token in _excludedPrefabTokens)
            {
                if (normalizedName.Contains(token))
                {
                    return true;
                }
            }
            return false;
        }

        private void EnsureAnimalConfigEntry(string species, string prefab, string category)
        {
            if (_config.AnimalAiOverrides.ContainsKey(species))
            {
                return;
            }

            _config.AnimalAiOverrides.Add(species, null);
            QueueConfigSave();

            if (_config.LogNewlyDiscoveredAnimalPrefabs)
            {
                Puts($"Discovered animal '{species}' (prefab '{prefab}', category '{category}'). " +
                     "Added a null override without changing its effective setting.");
            }
        }

        private void QueueConfigSave()
        {
            if (_configSaveQueued)
            {
                return;
            }

            _configSaveQueued = true;
            timer.Once(1f, () =>
            {
                _configSaveQueued = false;
                if (_unloading)
                {
                    return;
                }
                SaveConfig();
            });
        }

        private void RestoreDisabledAnimals()
        {
            foreach (KeyValuePair<BaseEntity, DisabledAnimalState> entry in _disabledAnimalStates.ToArray())
            {
                BaseEntity entity = entry.Key;
                DisabledAnimalState state = entry.Value;
                if (entity == null || entity.IsDestroyed || _deathProcessingEntities.Contains(entity))
                {
                    continue;
                }

                if (state.HadBrain && entity.TryGetComponent<BaseAIBrain>(out BaseAIBrain brain))
                {
                    brain.enabled = state.BrainEnabled;
                }

                BaseNpc legacyNpc = entity as BaseNpc;
                if (state.HadLegacyNpc && state.LegacyTickWasInvoking && legacyNpc != null && !legacyNpc.IsInvoking(legacyNpc.TickAi))
                {
                    // Mirrors BaseNpc.ServerInit. Restoring this timer makes
                    // switching to Vanilla mode reversible without a restart.
                    legacyNpc.InvokeRandomized(legacyNpc.TickAi, 0.1f, 0.1f, 0.005f);
                }

                if (state.HadFsm && entity.TryGetComponent<FSMComponent>(out FSMComponent fsm))
                {
                    fsm.SetFsmActive(state.FsmRunning);
                    fsm.isRunning = state.FsmRunning;

                    if (fsm.workQueue == null)
                    {
                        continue;
                    }

                    bool isQueued = fsm.workQueue.Contains(fsm);
                    if (state.WasQueued && !isQueued)
                    {
                        fsm.workQueue.Add(fsm);
                    }
                    else if (!state.WasQueued && isQueued)
                    {
                        fsm.workQueue.Remove(fsm);
                    }
                }
            }
        }

        private List<string> BuildStatusLines()
        {
            Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> categories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, HashSet<string>> runtimeIdentities =
                new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            int enabledCount = 0;
            int disabledCount = 0;
            int unknownCount = 0;

            foreach (BaseNetworkable networkable in BaseNetworkable.serverEntities)
            {
                BaseEntity entity = networkable as BaseEntity;
                if (entity == null || !TryGetAnimalIdentity(entity, out string species, out string category))
                {
                    continue;
                }

                counts[species] = counts.TryGetValue(species, out int count) ? count + 1 : 1;
                categories[species] = category;

                if (!runtimeIdentities.TryGetValue(species, out HashSet<string> identities))
                {
                    identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    runtimeIdentities.Add(species, identities);
                }
                identities.Add($"{entity.ShortPrefabName} [{entity.GetType().Name}]");

                if (category.Equals(CategoryUnknown, StringComparison.OrdinalIgnoreCase))
                {
                    unknownCount++;
                }

                if (IsAiEnabled(species, category))
                {
                    enabledCount++;
                }
                else
                {
                    disabledCount++;
                }
            }

            List<string> lines = new List<string>
            {
                $"Remove Animals AI v{Version} | Mode={GetMode()} | Detected={counts.Values.Sum()} | " +
                $"AI enabled={enabledCount} | AI disabled={disabledCount} | Unknown={unknownCount}"
            };

            foreach (string species in counts.Keys.OrderBy(value => value))
            {
                string category = categories[species];
                string identities = string.Join(", ", runtimeIdentities[species].OrderBy(value => value));
                string aiState = IsAiEnabled(species, category) ? "enabled" : "disabled";
                lines.Add($"{species}: count={counts[species]}, category={category}, " +
                          $"AI={aiState}, runtime={identities}");
            }

            if (counts.Count == 0)
            {
                lines.Add("No animal entities are currently detected.");
            }

            return lines;
        }

        [ConsoleCommand("removeanimalsai.status")]
        private void ConsoleStatus(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Connection?.player as BasePlayer;
            if (player != null && !player.IsAdmin)
            {
                return;
            }

            arg.ReplyWith(string.Join("\n", BuildStatusLines()));
        }

        [ChatCommand("animalai")]
        private void ChatStatus(BasePlayer player, string command, string[] args)
        {
            if (player == null || !player.IsAdmin)
            {
                return;
            }

            foreach (string line in BuildStatusLines())
            {
                player.ConsoleMessage(line);
            }
            player.ChatMessage("Animal AI status was written to your F1 console.");
        }

        [HarmonyPatch(typeof(BaseNpc), nameof(BaseNpc.ServerInit)), AutoPatch]
        public static class ServerInitPatch
        {
            [HarmonyPostfix]
            private static void Postfix(BaseNpc __instance)
            {
                if (Instance != null)
                {
                    Instance.ShouldDisable(__instance);
                }
            }
        }

        [HarmonyPatch(typeof(FSMComponent), nameof(FSMComponent.SetFsmActive)), AutoPatch]
        public static class SetFsmActivePatch
        {
            [HarmonyPrefix]
            private static bool Prefix(FSMComponent __instance, bool newActive)
            {
                if (!newActive || Instance == null)
                {
                    return true;
                }

                return !Instance.ShouldDisable(__instance._baseEntity);
            }
        }

        [HarmonyPatch(typeof(AIThinkManager), nameof(AIThinkManager.ProcessQueue)), AutoPatch]
        public static class ProcessQueuePatch
        {
            [HarmonyPrefix]
            private static bool Prefix(AIThinkManager.QueueType queueType)
            {
                if (Instance == null || queueType != AIThinkManager.QueueType.Animal)
                {
                    return true;
                }

                return Instance.GetMode() != ModeDisableAll;
            }
        }

        [HarmonyPatch(typeof(AnimalBrain), nameof(AnimalBrain.InitializeAI)), AutoPatch]
        public static class InitializeAIPatch
        {
            [HarmonyPostfix]
            private static void Postfix(AnimalBrain __instance)
            {
                if (Instance == null || __instance == null)
                {
                    return;
                }

                BaseEntity entity = __instance.GetComponent<BaseEntity>();
                if (entity != null)
                {
                    Instance.ApplyAnimalPolicy(entity);
                }
            }
        }

        [HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.OnDied)), AutoPatch]
        public static class OnDiedRequeueFSMPatch
        {
            [HarmonyPrefix]
            private static void Prefix(BaseCombatEntity __instance)
            {
                if (Instance == null || !Instance.ShouldDisable(__instance))
                {
                    return;
                }

                // From this point onward all of our activation guards allow
                // Rust's vanilla death transition to finish. Enabled animals
                // never enter this set and remain completely vanilla.
                Instance.BeginDeathProcessing(__instance);

                if (__instance.TryGetComponent<FSMComponent>(out FSMComponent fsm) && fsm.workQueue != null)
                {
                    // Movement stays disabled, but Rust must process the death FSM
                    // long enough to create a normal, harvestable animal corpse.
                    fsm.isRunning = true;
                    if (!fsm.workQueue.Contains(fsm))
                    {
                        fsm.workQueue.Add(fsm);
                    }
                }
            }
        }
    }
}
