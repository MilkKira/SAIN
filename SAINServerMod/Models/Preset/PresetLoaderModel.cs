namespace SAINServerMod.Models.Preset;

public sealed class PresetLoaderModel
{
    public string Preset { get; init; } = string.Empty;
}

public sealed class PresetPackageModel
{
    public string PresetName { get; init; } = string.Empty;
    public Dictionary<string, string> Files { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
