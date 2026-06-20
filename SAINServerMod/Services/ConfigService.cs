using System.Reflection;
using Microsoft.Extensions.Logging;
using SAINServerMod.Models.Preset;
using SAINServerMod.Models.Preset.Personalities;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Logging;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils;

namespace SAINServerMod.Services;

[Injectable(InjectionType.Singleton)]
public sealed class ConfigService(ModHelper modHelper, JsonUtil jsonUtil, ILogger<ConfigService> logger, ISptLogger<ConfigService> sptLogger)
{
    public string ModPath { get; init; } = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
    public NicknamesModel NicknamesModel { get; private set; } = default!;
    public PresetPackageModel PresetPackage { get; private set; } = default!;

    public async Task LoadAsync()
    {
        logger.LogInformation("[SAIN] Loading server configuration from {ModPath}", ModPath);

        try
        {
            string nicknamesPath = Path.Combine(ModPath, "data", "NicknamePersonalities.json");
            NicknamesModel =
                await jsonUtil.DeserializeFromFileAsync<NicknamesModel>(nicknamesPath)
                ?? throw new InvalidOperationException($"Could not load nickname configuration: {nicknamesPath}");

            logger.LogInformation(
                "[SAIN] Loaded nickname configuration: {Path} ({Count} entries)",
                nicknamesPath,
                NicknamesModel.NicknamePersonalities.Count
            );

            PresetPackage = await LoadPresetPackageAsync();

            logger.LogInformation(
                "[SAIN] Server configuration loaded successfully. Active preset: {PresetName}; preset files: {FileCount}",
                PresetPackage.PresetName,
                PresetPackage.Files.Count
            );
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "[SAIN] Failed to load server configuration from {ModPath}", ModPath);
            throw;
        }
    }

    private async Task<PresetPackageModel> LoadPresetPackageAsync()
    {
        string dataPath = Path.Combine(ModPath, "data");
        string presetsPath = Path.Combine(dataPath, "presets");
        string loaderPath = Path.Combine(dataPath, "loader.json");

        logger.LogInformation("[SAIN] Loading preset selector: {LoaderPath}", loaderPath);

        Dictionary<string, string> loader =
            await jsonUtil.DeserializeFromFileAsync<Dictionary<string, string>>(loaderPath)
            ?? throw new InvalidOperationException($"Could not load preset selector: {loaderPath}");

        if (!loader.TryGetValue("preset", out string? presetName)
            && !loader.TryGetValue("Preset", out presetName))
        {
            throw new InvalidOperationException($"data/loader.json must contain a 'preset' property: {loaderPath}");
        }

        presetName = presetName?.Trim();
        if (string.IsNullOrWhiteSpace(presetName)
            || presetName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || presetName.Contains(Path.DirectorySeparatorChar)
            || presetName.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new InvalidOperationException(
                $"data/loader.json contains an invalid preset name '{presetName ?? "<null>"}': {loaderPath}"
            );
        }

        logger.LogInformation("[SAIN] loader.json selected preset: {PresetName}", presetName);

        string presetPath = Path.GetFullPath(Path.Combine(presetsPath, presetName));
        string presetRoot = Path.GetFullPath(presetsPath) + Path.DirectorySeparatorChar;
        if (!presetPath.StartsWith(presetRoot, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(presetPath))
        {
            throw new InvalidOperationException($"Configured SAIN preset does not exist: {presetPath}");
        }

        logger.LogInformation("[SAIN] Loading preset directory: {PresetPath}", presetPath);

        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string filePath in Directory.EnumerateFiles(presetPath, "*.json", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(presetPath, filePath).Replace('\\', '/');
            files[$"Presets/{presetName}/{relativePath}"] = await File.ReadAllTextAsync(filePath);
            logger.LogInformation("[SAIN] Loaded preset file: {PresetFile}", relativePath);

            sptLogger.LogWithColor("[SAIN] SAIN SERVER Loaded Successfully", LogTextColor.Green, LogBackgroundColor.Black);
        }

        if (!files.ContainsKey($"Presets/{presetName}/Info.json"))
        {
            throw new InvalidOperationException($"Preset '{presetName}' is missing Info.json.");
        }

        logger.LogInformation(
            "[SAIN] Preset '{PresetName}' loaded successfully from {PresetPath} ({FileCount} JSON files)",
            presetName,
            presetPath,
            files.Count
        );

        return new PresetPackageModel { PresetName = presetName, Files = files };
    }
}
