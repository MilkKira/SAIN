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
