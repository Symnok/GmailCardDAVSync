// Helpers/ContactHashStorage.cs
// Stores a hash of each contact's data after Google→Phone sync.
// On Phone→Google sync, we compare current contact data against
// the stored hash to detect which contacts actually changed.

using System.Collections.Generic;
using Windows.Storage;
using Windows.ApplicationModel.Contacts;

namespace GmailCardDAVSync.Helpers
{
    public static class ContactHashStorage
    {
        private const string ContainerName = "ContactHashes";

        // ---------------------------------------------------------------
        // Compute a simple hash string from a contact's key fields.
        // Changing any field changes the hash → triggers upload.
        // ---------------------------------------------------------------
        public static string ComputeHash(Contact c)
        {
            var sb = new System.Text.StringBuilder();

            sb.Append(c.FirstName  ?? "");
            sb.Append("|");
            sb.Append(c.LastName   ?? "");
            sb.Append("|");
            sb.Append(c.Nickname   ?? "");
            sb.Append("|");
            sb.Append(c.Notes      ?? "");

            foreach (var e in c.Emails)
            {
                sb.Append("|E:");
                sb.Append(e.Address ?? "");
                sb.Append(":");
                sb.Append(e.Kind.ToString());
                sb.Append(":");
                sb.Append(e.Description ?? "");
            }

            foreach (var p in c.Phones)
            {
                sb.Append("|P:");
                sb.Append(p.Number ?? "");
                sb.Append(":");
                sb.Append(p.Description ?? "");
            }

            foreach (var a in c.Addresses)
            {
                sb.Append("|A:");
                sb.Append(a.StreetAddress ?? "");
                sb.Append(a.Locality      ?? "");
                sb.Append(a.PostalCode    ?? "");
                sb.Append(a.Country       ?? "");
            }

            if (c.JobInfo.Count > 0)
            {
                sb.Append("|J:");
                sb.Append(c.JobInfo[0].CompanyName ?? "");
                sb.Append(":");
                sb.Append(c.JobInfo[0].Title       ?? "");
            }

            foreach (var date in c.ImportantDates)
            {
                if (date.Kind == ContactDateKind.Birthday)
                {
                    sb.Append("|B:");
                    sb.Append(date.Year.HasValue ? date.Year.Value.ToString() : "");
                    sb.Append("-");
                    sb.Append((int)date.Month);
                    sb.Append("-");
                    sb.Append((int)date.Day);
                    break;
                }
            }

            // Simple hash — combine all chars into an int
            string raw = sb.ToString();
            int hash   = 17;
            foreach (char ch in raw)
                hash = hash * 31 + ch;

            return hash.ToString();
        }

        // ---------------------------------------------------------------
        // Save hashes for all contacts (call after Google→Phone sync)
        // uid → hash
        // ---------------------------------------------------------------
        public static void SaveAll(Dictionary<string, string> uidToHash)
        {
            var settings  = ApplicationData.Current.LocalSettings;
            var container = settings.CreateContainer(ContainerName,
                ApplicationDataCreateDisposition.Always);

            container.Values.Clear();

            foreach (var kv in uidToHash)
                if (!string.IsNullOrEmpty(kv.Key))
                    container.Values[SafeKey(kv.Key)] = kv.Key + "|" + kv.Value;
        }

        // ---------------------------------------------------------------
        // Load saved hashes
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
                result[stored.Substring(0, sep)] = stored.Substring(sep + 1);
            }
            return result;
        }

        // ---------------------------------------------------------------
        // Check if a contact's hash matches the saved one
        // Returns true if contact is unchanged
        // ---------------------------------------------------------------
        public static bool IsUnchanged(Contact c, Dictionary<string, string> savedHashes)
        {
            string uid = c.RemoteId;
            if (string.IsNullOrEmpty(uid)) return false; // new contact — always upload

            if (!savedHashes.ContainsKey(uid)) return false; // not seen before — upload

            string currentHash = ComputeHash(c);
            return savedHashes[uid] == currentHash;
        }

        // ---------------------------------------------------------------
        // Clear
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
