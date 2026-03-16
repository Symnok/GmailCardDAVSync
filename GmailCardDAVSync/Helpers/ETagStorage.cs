// Helpers/ETagStorage.cs
// Stores href→etag pairs AND uid→href mapping in ApplicationData.LocalSettings.
// The uid→href map is used when uploading contacts back to Google,
// so we PUT to the correct existing URL instead of creating a duplicate.

using System.Collections.Generic;
using Windows.Storage;

namespace GmailCardDAVSync.Helpers
{
    public static class ETagStorage
    {
        private const string ETagContainer = "CardDAVETags";
        private const string HrefContainer = "CardDAVHrefs"; // uid → href

        // ---------------------------------------------------------------
        // Save href→etag map after sync
        // ---------------------------------------------------------------
        public static void SaveAll(Dictionary<string, string> hrefToEtag)
        {
            var settings  = ApplicationData.Current.LocalSettings;
            var container = settings.CreateContainer(ETagContainer,
                ApplicationDataCreateDisposition.Always);

            container.Values.Clear();

            foreach (var kv in hrefToEtag)
            {
                string key   = SafeKey(kv.Key);
                string value = kv.Key + "|" + kv.Value;
                container.Values[key] = value;
            }
        }

        // ---------------------------------------------------------------
        // Save uid→href map after sync (for upload lookup)
        // ---------------------------------------------------------------
        public static void SaveUidHrefs(Dictionary<string, string> uidToHref)
        {
            var settings  = ApplicationData.Current.LocalSettings;
            var container = settings.CreateContainer(HrefContainer,
                ApplicationDataCreateDisposition.Always);

            container.Values.Clear();

            foreach (var kv in uidToHref)
            {
                if (!string.IsNullOrEmpty(kv.Key))
                    container.Values[SafeKey(kv.Key)] = kv.Key + "|" + kv.Value;
            }
        }

        // ---------------------------------------------------------------
        // Look up Google href for a given UID
        // Returns null if not found (contact is new, not from Google)
        // ---------------------------------------------------------------
        public static string GetHrefForUid(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return null;

            var settings = ApplicationData.Current.LocalSettings;
            if (!settings.Containers.ContainsKey(HrefContainer)) return null;

            var container = settings.Containers[HrefContainer];
            string key    = SafeKey(uid);

            if (!container.Values.ContainsKey(key)) return null;

            string stored = container.Values[key] as string;
            if (string.IsNullOrEmpty(stored)) return null;

            int sep = stored.IndexOf('|');
            return sep >= 0 ? stored.Substring(sep + 1) : null;
        }

        // ---------------------------------------------------------------
        // Load href→etag map
        // ---------------------------------------------------------------
        public static Dictionary<string, string> LoadAll()
        {
            var result   = new Dictionary<string, string>();
            var settings = ApplicationData.Current.LocalSettings;

            if (!settings.Containers.ContainsKey(ETagContainer))
                return result;

            var container = settings.Containers[ETagContainer];
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
        // Load uid→href map
        // ---------------------------------------------------------------
        public static Dictionary<string, string> LoadUidHrefs()
        {
            var result   = new Dictionary<string, string>();
            var settings = ApplicationData.Current.LocalSettings;

            if (!settings.Containers.ContainsKey(HrefContainer))
                return result;

            var container = settings.Containers[HrefContainer];
            foreach (var kv in container.Values)
            {
                string stored = kv.Value as string;
                if (string.IsNullOrEmpty(stored)) continue;

                int sep = stored.IndexOf('|');
                if (sep < 0) continue;

                string uid  = stored.Substring(0, sep);
                string href = stored.Substring(sep + 1);
                result[uid] = href;
            }
            return result;
        }

        // ---------------------------------------------------------------
        // HasData / Clear
        // ---------------------------------------------------------------
        public static bool HasData()
        {
            var settings = ApplicationData.Current.LocalSettings;
            if (!settings.Containers.ContainsKey(ETagContainer))
                return false;
            return settings.Containers[ETagContainer].Values.Count > 0;
        }

        public static void Clear()
        {
            var settings = ApplicationData.Current.LocalSettings;
            if (settings.Containers.ContainsKey(ETagContainer))
                settings.Containers[ETagContainer].Values.Clear();
            if (settings.Containers.ContainsKey(HrefContainer))
                settings.Containers[HrefContainer].Values.Clear();
            LabelStorage.Clear();
            ContactHashStorage.Clear();
        }

        private static string SafeKey(string s)
        {
            if (s.Length <= 200) return s;
            return s.Substring(s.Length - 200);
        }
    }
}
