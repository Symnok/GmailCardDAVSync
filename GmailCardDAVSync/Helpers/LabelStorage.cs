// Helpers/LabelStorage.cs
// Saves phone and email labels (types) per contact UID so they can be
// restored when serializing contacts back to Google.
// The People app on W10M doesn't reliably preserve the Description field,
// so we save labels separately after each Google→Phone sync.

using System.Collections.Generic;
using Windows.Storage;

namespace GmailCardDAVSync.Helpers
{
    public static class LabelStorage
    {
        private const string ContainerName = "ContactLabels";

        // ---------------------------------------------------------------
        // Save labels for one contact
        // labels = { "phone_0": "CELL,VOICE", "email_0": "INTERNET,HOME", ... }
        // ---------------------------------------------------------------
        public static void SaveLabels(string uid,
            Dictionary<string, string> labels)
        {
            if (string.IsNullOrEmpty(uid) || labels == null) return;

            var settings  = ApplicationData.Current.LocalSettings;
            var container = settings.CreateContainer(ContainerName,
                ApplicationDataCreateDisposition.Always);

            // Store all labels as one string: "phone_0=CELL,VOICE;email_0=INTERNET,HOME"
            var parts = new List<string>();
            foreach (var kv in labels)
                parts.Add(kv.Key + "=" + kv.Value);

            container.Values[SafeKey(uid)] = string.Join("|", parts);
        }

        // ---------------------------------------------------------------
        // Load labels for one contact
        // ---------------------------------------------------------------
        public static Dictionary<string, string> LoadLabels(string uid)
        {
            var result = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(uid)) return result;

            var settings = ApplicationData.Current.LocalSettings;
            if (!settings.Containers.ContainsKey(ContainerName)) return result;

            var container = settings.Containers[ContainerName];
            string key    = SafeKey(uid);
            if (!container.Values.ContainsKey(key)) return result;

            string stored = container.Values[key] as string;
            if (string.IsNullOrEmpty(stored)) return result;

            foreach (var part in stored.Split('|'))
            {
                int eq = part.IndexOf('=');
                if (eq < 0) continue;
                result[part.Substring(0, eq)] = part.Substring(eq + 1);
            }
            return result;
        }

        // ---------------------------------------------------------------
        // Clear all saved labels
        // ---------------------------------------------------------------
        public static void Clear()
        {
            var settings = ApplicationData.Current.LocalSettings;
            if (settings.Containers.ContainsKey(ContainerName))
                settings.Containers[ContainerName].Values.Clear();
        }

        private static string SafeKey(string s)
        {
            if (s.Length <= 200) return s;
            return s.Substring(s.Length - 200);
        }
    }
}
