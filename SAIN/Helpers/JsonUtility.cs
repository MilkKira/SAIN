using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using SAIN.Plugin;
using SAIN.Preset;
using SAIN.Preset.GearStealthValues;

namespace SAIN.Helpers;

public enum JsonEnum
{
    Presets,
    GlobalSettings,
}

public static class JsonUtility
{
    public static readonly Dictionary<JsonEnum, string> FileAndFolderNames = new()
    {
        { JsonEnum.Presets, "Presets" },
        { JsonEnum.GlobalSettings, "GlobalSettings" },
    };

    public const string PresetsFolder = "Presets";
    public const string JsonExtension = ".json";
    public const string Info = "Info";

    public static void SaveObjectToJson(object objectToSave, string fileName, params string[] folders)
    {
        // Intentionally disabled. Client configuration is read-only and memory-only.
    }

    public static bool DoesFileExist(string fileName, params string[] folders)
    {
        return TryGetRemotePath(EnsureJsonExtension(fileName), out string path, folders)
            && RemotePresetStore.TryGetFile(path, out _);
    }

    public static class Load
    {
        public static void LoadCustomPresetOptions(List<SAINPresetDefinition> list)
        {
            list.Clear();
        }

        public static void LoadStealthValues(List<ItemStealthValue> list, params string[] folders)
        {
            if (!IsPresetPath(folders))
            {
                return;
            }

            string directory = string.Join("/", folders);
            foreach (string path in RemotePresetStore.GetFiles(directory, JsonExtension))
            {
                if (RemotePresetStore.TryGetFile(path, out string jsonContent))
                {
                    list.Add(JsonConvert.DeserializeObject<ItemStealthValue>(jsonContent));
                }
            }
        }

        public static T DeserializeObject<T>(string json)
        {
            return JsonConvert.DeserializeObject<T>(json);
        }

        public static string LoadTextFile(string fileExtension, string fileName, params string[] folders)
        {
            if (!TryGetRemotePath(fileName + fileExtension, out string path, folders))
            {
                return null;
            }

            RemotePresetStore.TryGetFile(path, out string content);
            return content;
        }

        public static bool LoadJsonFile(out string json, string fileName, params string[] folders)
        {
            json = LoadTextFile(JsonExtension, fileName, folders);
            return json != null;
        }

        public static bool LoadObject<T>(out T obj, string fileName, params string[] folders)
        {
            try
            {
                string json = LoadTextFile(JsonExtension, fileName, folders);
                if (json != null)
                {
                    obj = DeserializeObject<T>(json);
                    return true;
                }
            }
            catch (JsonSerializationException) { }

            obj = default;
            return false;
        }
    }

    public static void DeletePreset(SAINPresetDefinition preset)
    {
        // Intentionally disabled.
    }

    public static void CreateFolder(params string[] subFolders)
    {
        // Intentionally disabled.
    }

    public static bool DoesFolderExist(params string[] subFolders)
    {
        if (!RemotePresetStore.IsLoaded || !IsPresetPath(subFolders))
        {
            return false;
        }

        using var enumerator = RemotePresetStore.GetFiles(string.Join("/", subFolders), JsonExtension).GetEnumerator();
        return enumerator.MoveNext();
    }

    public static bool GetFoldersPath(out string path, params string[] folders)
    {
        path = null;
        return false;
    }

    public static string GetSAINPluginPath()
    {
        return null;
    }

    private static bool IsPresetPath(string[] folders)
    {
        return folders != null
            && folders.Length > 0
            && string.Equals(folders[0], PresetsFolder, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetRemotePath(string fileName, out string path, params string[] folders)
    {
        path = null;
        if (!RemotePresetStore.IsLoaded || !IsPresetPath(folders))
        {
            return false;
        }

        path = string.Join("/", folders) + "/" + fileName;
        return true;
    }

    private static string EnsureJsonExtension(string fileName)
    {
        return fileName.EndsWith(JsonExtension, StringComparison.OrdinalIgnoreCase)
            ? fileName
            : fileName + JsonExtension;
    }
}
