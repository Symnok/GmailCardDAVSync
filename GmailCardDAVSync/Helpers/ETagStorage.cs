// Helpers/ETagStorage.cs
// Stores href→etag pairs in ApplicationData.LocalSettings so we can
// do incremental sync — only fetch contacts whose etag changed.

using System.Collections.Generic;
using Windows.Storage;

namespace GmailCardDAVSync.Helpers
{
    public static class ETagStorage
    {
        // Container name in LocalSettings
        private const string ContainerName = "CardDAVETags";

        // ---------------------------------------------------------------
        // Save the full map of href → etag after a successful sync
        // ---------------------------------------------------------------
        public static void SaveAll(Dictionary<string, string> hrefToEtag)
        {
            var settings  = ApplicationData.Current.LocalSettings;
            var container = settings.CreateContainer(ContainerName,
                ApplicationDataCreateDisposition.Always);

            // Clear old entries first
            container.Values.Clear();

            foreach (var kv in hrefToEtag)
            {
                // LocalSettings keys can't be longer than 255 chars
                // Use a short hash of the href as key, store "href|etag" as value
                string key   = SafeKey(kv.Key);
                string value = kv.Key + "|" + kv.Value;
                container.Values[key] = value;
            }
        }

        // ---------------------------------------------------------------
        // Load saved href → etag map
        // ---------------------------------------------------------------
        public static Dictionary<string, string> LoadAll()
        {
            var result   = new Dictionary<string, string>();
            var settings = ApplicationData.Current.LocalSettings;

            if (!settings.Containers.ContainsKey(ContainerName))
                return result;

            var container = settings.Containers[ContainerName];
            foreach (var kv in container.Values)
            {
                string stored = kv.Value as string;
                if (string.IsNullOrEmpty(stored)) continue;

                int sep = stored.IndexOf('|');
                if (sep < 0) continue;

                string href = stored.Substring(0, sep);
                string etag = stored.Substring(sep + 1);
                result[href] = etag;
            }
            return result;
        }

        // ---------------------------------------------------------------
        // Check if any etags are saved (i.e. we've synced before)
        // ---------------------------------------------------------------
        public static bool HasData()
        {
            var settings = ApplicationData.Current.LocalSettings;
            if (!settings.Containers.ContainsKey(ContainerName))
                return false;
            return settings.Containers[ContainerName].Values.Count > 0;
        }

        // ---------------------------------------------------------------
        // Clear all saved etags (used when user changes account)
        // ---------------------------------------------------------------
        public static void Clear()
        {
            var settings = ApplicationData.Current.LocalSettings;
            if (settings.Containers.ContainsKey(ContainerName))
                settings.Containers[ContainerName].Values.Clear();
        }

        // Make a safe LocalSettings key from an href
        private static string SafeKey(string href)
        {
            // Simple: use last 200 chars (enough to be unique per contact)
            if (href.Length <= 200) return href;
            return href.Substring(href.Length - 200);
        }
    }
}
