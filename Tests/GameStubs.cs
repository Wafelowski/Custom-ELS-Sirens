// Test doubles only. This file is excluded from the plugin project.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Rage
{
    public enum ControllerButtons { B, DPadDown, DPadRight }
    public struct Vector3
    {
        public float X, Y, Z;
        public Vector3(float x, float y, float z) { X = x; Y = y; Z = z; }
        public static float Distance(Vector3 a, Vector3 b)
        {
            float x = a.X - b.X, y = a.Y - b.Y, z = a.Z - b.Z;
            return (float)Math.Sqrt(x * x + y * y + z * z);
        }
    }
    public sealed class Vehicle
    {
        public bool Valid = true;
        public bool IsAlive = true;
        public Vector3 Position;
        public bool IsValid() => Valid;
    }
    public static class Game
    {
        public static uint GameTime;
        public static class Console { public static void Print(string message) => System.Console.WriteLine(message); }
    }

    // A small on-disk INI double lets tests exercise the real configuration and
    // profile code without loading RAGE Plugin Hook inside the test process.
    public sealed class InitializationFile
    {
        private readonly string path;
        public InitializationFile(string path) { this.path = path; }
        public bool Exists() => File.Exists(path);
        public void Create() { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))); File.WriteAllText(path, ""); }
        private Dictionary<string, Dictionary<string, string>> ReadAll()
        {
            var data = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            string section = "";
            if (!Exists()) return data;
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
                if (line.StartsWith("[") && line.EndsWith("]")) section = line.Substring(1, line.Length - 2);
                else
                {
                    int separator = line.IndexOf('=');
                    if (separator < 0) continue;
                    if (!data.ContainsKey(section)) data[section] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    data[section][line.Substring(0, separator).Trim()] = line.Substring(separator + 1).Trim();
                }
            }
            return data;
        }
        public string ReadString(string section, string key, string fallback)
        {
            var data = ReadAll();
            return data.TryGetValue(section, out var entries) && entries.TryGetValue(key, out string value) ? value : fallback;
        }
        public bool ReadBoolean(string section, string key, bool fallback) => bool.TryParse(ReadString(section, key, ""), out bool value) ? value : fallback;
        public int ReadInt32(string section, string key, int fallback) => int.TryParse(ReadString(section, key, ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : fallback;
        public T ReadEnum<T>(string section, string key, T fallback) where T : struct => Enum.TryParse(ReadString(section, key, ""), true, out T value) ? value : fallback;
        public void Write(string section, string key, string value)
        {
            var data = ReadAll();
            if (!data.ContainsKey(section)) data[section] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            data[section][key] = value;
            using (var writer = new StreamWriter(path))
                foreach (var group in data)
                {
                    writer.WriteLine("[" + group.Key + "]");
                    foreach (var entry in group.Value) writer.WriteLine(entry.Key + "=" + entry.Value);
                }
        }
    }
}
