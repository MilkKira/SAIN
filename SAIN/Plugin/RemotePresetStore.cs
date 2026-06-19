using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using SPT.Common.Http;

namespace SAIN.Plugin;

internal sealed class RemotePresetPackage
{
    public string PresetName { get; set; }
    public Dictionary<string, string> Files { get; set; }
}

internal static class RemotePresetStore
{
    private const string PresetRoute = "/sain/preset";
    private static readonly Dictionary<string, string> Files = new(StringComparer.OrdinalIgnoreCase);

    public static string PresetName { get; private set; }
    public static bool IsLoaded { get; private set; }

    public static void Initialize()
    {
        Files.Clear();
        IsLoaded = false;

        string response = RequestHandler.GetJson(PresetRoute);
        var package = JsonConvert.DeserializeObject<RemotePresetPackage>(response);
        if (package == null || string.IsNullOrWhiteSpace(package.PresetName) || package.Files == null)
        {
            throw new InvalidOperationException("The SAIN server returned an invalid preset package.");
        }

        foreach (var file in package.Files)
        {
            Files[Normalize(file.Key)] = file.Value;
        }

        PresetName = package.PresetName;
        if (!TryGetFile($"Presets/{PresetName}/Info.json", out _))
        {
            throw new InvalidOperationException($"Server preset '{PresetName}' does not contain Info.json.");
        }

        IsLoaded = true;
        Logger.LogInfo($"Loaded server preset '{PresetName}' into memory ({Files.Count} files).");
    }

    public static bool TryGetFile(string path, out string content)
    {
        return Files.TryGetValue(Normalize(path), out content);
    }

    public static IEnumerable<string> GetFiles(string directory, string extension)
    {
        string prefix = Normalize(directory).TrimEnd('/') + "/";
        foreach (var path in Files.Keys)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                yield return path;
            }
        }
    }

    private static string Normalize(string path)
    {
        return path.Replace('\\', '/').TrimStart('/');
    }
}
