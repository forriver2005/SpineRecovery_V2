using System;
using System.IO;
using UnityEngine;

namespace SpineRecovery.App
{
    public static class JsonFileStore
    {
        public static string PathFor(string fileName)
        {
            return System.IO.Path.Combine(Application.persistentDataPath, fileName);
        }

        public static void Save<T>(string fileName, T value)
        {
            string path = PathFor(fileName);
            Directory.CreateDirectory(Application.persistentDataPath);
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(value, true));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }

        public static bool TryLoad<T>(string fileName, out T value) where T : class
        {
            value = null;
            string path = PathFor(fileName);
            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                value = JsonUtility.FromJson<T>(File.ReadAllText(path));
                return value != null;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Could not load local JSON data: " + exception.Message);
                return false;
            }
        }
    }
}
