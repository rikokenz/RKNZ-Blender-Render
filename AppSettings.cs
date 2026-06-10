using System;
using System.IO;
using System.Text.Json;

namespace BlenderTool
{
    public class AppConfig
    {
        public string BlenderPath { get; set; } = string.Empty;
        public string Theme       { get; set; } = "Light";   // "Light" | "Dark"
    }

    public static class AppSettings
    {
        // Saves to %AppData%\BlenderTool\config.json
        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BlenderTool",
            "config.json"
        );

        public static AppConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
                }
            }
            catch { /* return defaults on any error */ }

            return new AppConfig();
        }

        public static void Save(AppConfig config)
        {
            var dir = Path.GetDirectoryName(ConfigPath)!;
            Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }

        // Convenience: get blender path directly
        public static string GetBlenderPath() => Load().BlenderPath;
    }
}
