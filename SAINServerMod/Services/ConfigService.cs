using System.Reflection;
using SAINServerMod.Models.Preset;
using SAINServerMod.Models.Preset.Personalities;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Utils;

namespace SAINServerMod.Services;

[Injectable(InjectionType.Singleton)]
public sealed class ConfigService(ModHelper modHelper, JsonUtil jsonUtil)
{
    public string ModPath { get; init; } = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
    public NicknamesModel NicknamesModel { get; private set; } = default!;
    public PresetPackageModel PresetPackage { get; private set; } = default!;

    public async Task LoadAsync()
    {
        NicknamesModel =
            await jsonUtil.DeserializeFromFileAsync<NicknamesModel>(Path.Combine(ModPath, "data", "NicknamePersonalities.json"))
            ?? throw new InvalidOperationException("Could not load nicknames, is the mod installed correctly?");

        PresetPackage = await LoadPresetPackageAsync();
    }

    private async Task<PresetPackageModel> LoadPresetPackageAsync()
    {
        string dataPath = Path.Combine(ModPath, "data");
        string presetsPath = Path.Combine(dataPath, "presets");
        string loaderPath = Path.Combine(dataPath, "loader.json");

        PresetLoaderModel loader =
            await jsonUtil.DeserializeFromFileAsync<PresetLoaderModel>(loaderPath)
            ?? throw new InvalidOperationException($"Could not load preset selector: {loaderPath}");

        if (string.IsNullOrWhiteSpace(loader.Preset)
            || loader.Preset.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || loader.Preset.Contains(Path.DirectorySeparatorChar)
            || loader.Preset.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new InvalidOperationException("data/loader.json contains an invalid preset name.");
        }

        string presetPath = Path.GetFullPath(Path.Combine(presetsPath, loader.Preset));
        string presetRoot = Path.GetFullPath(presetsPath) + Path.DirectorySeparatorChar;
        if (!presetPath.StartsWith(presetRoot, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(presetPath))
        {
            throw new InvalidOperationException($"Configured SAIN preset does not exist: {presetPath}");
        }

        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string filePath in Directory.EnumerateFiles(presetPath, "*.json", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(presetPath, filePath).Replace('\\', '/');
            files[$"Presets/{loader.Preset}/{relativePath}"] = await File.ReadAllTextAsync(filePath);
        }

        if (!files.ContainsKey($"Presets/{loader.Preset}/Info.json"))
        {
            throw new InvalidOperationException($"Preset '{loader.Preset}' is missing Info.json.");
        }

        return new PresetPackageModel { PresetName = loader.Preset, Files = files };
    }
}
