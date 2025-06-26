using System;
using System.IO;
using System.Text.Json; // Requires .NET Core 3.0+ or .NET 5+ / .NET Framework with System.Text.Json NuGet

namespace VpnApp
{
    public static class ConfigurationService
    {
        // Using LocalApplicationData is often preferred over ApplicationData for user-specific config files
        private static readonly string AppDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VpnApp");
        private static readonly string ConfigFilePath = Path.Combine(AppDataFolder, "settings.json");
        private static Action<string> _logger; // Optional logger action

        public static void Initialize(Action<string> logger = null)
        {
            _logger = logger ?? Console.WriteLine;
        }

        public static void SaveSettings(AppSettings settings)
        {
            try
            {
                if (!Directory.Exists(AppDataFolder))
                {
                    Directory.CreateDirectory(AppDataFolder);
                    _logger?.Invoke($"Created settings directory: {AppDataFolder}");
                }

                string jsonString = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigFilePath, jsonString);
                _logger?.Invoke($"Settings saved to {ConfigFilePath}");
            }
            catch (Exception ex)
            {
                _logger?.Invoke($"Error saving settings: {ex.Message}");
            }
        }

        public static AppSettings LoadSettings()
        {
            try
            {
                if (File.Exists(ConfigFilePath))
                {
                    string jsonString = File.ReadAllText(ConfigFilePath);
                    AppSettings settings = JsonSerializer.Deserialize<AppSettings>(jsonString);
                    _logger?.Invoke($"Settings loaded from {ConfigFilePath}");
                    return settings ?? new AppSettings();
                }
                else
                {
                    _logger?.Invoke($"Settings file not found at {ConfigFilePath}. Returning default settings.");
                }
            }
            catch (JsonException ex)
            {
                 _logger?.Invoke($"Error deserializing settings from {ConfigFilePath}: {ex.Message}. Returning default settings.");
            }
            catch (Exception ex)
            {
                _logger?.Invoke($"Error loading settings from {ConfigFilePath}: {ex.Message}. Returning default settings.");
            }
            return new AppSettings();
        }
    }
}
