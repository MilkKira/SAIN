using System;
using SAIN.Preset;
using static SAIN.Helpers.JsonUtility;

namespace SAIN.Plugin;

internal static class PresetHandler
{
    public static event Action<SAINPresetClass> OnPresetUpdated;

    public static SAINPresetClass LoadedPreset { get; private set; }

    public static void Init()
    {
        RemotePresetStore.Initialize();

        // Custom bot types (e.g. server-defined types not present in the client's vanilla
        // BotTypes set) must be registered before the preset's bot settings are built.
        BotTypeDefinitions.RegisterServerCustomTypes();

        if (!Load.LoadObject(out SAINPresetDefinition definition, Info, PresetsFolder, RemotePresetStore.PresetName))
        {
            throw new InvalidOperationException($"Unable to load server preset '{RemotePresetStore.PresetName}'.");
        }

        LoadedPreset = new SAINPresetClass(definition);
        LoadedPreset.Init();

        var defaultPreset = SAINDifficultyClass.GetDefaultPreset(definition.BaseSAINDifficulty);
        if (defaultPreset != null)
        {
            LoadedPreset.UpdateDefaults(defaultPreset);
        }

        UpdateExistingBots();
    }

    public static void UpdateExistingBots()
    {
        OnPresetUpdated?.Invoke(LoadedPreset);
        LoadedPreset?.GlobalSettings.Update();
        LoadedPreset?.PersonalityManager.Update();
        LoadedPreset?.BotSettings.Update();
    }
}
