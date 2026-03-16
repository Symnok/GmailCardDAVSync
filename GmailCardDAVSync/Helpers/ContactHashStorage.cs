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

            // Name fields — normalize to avoid case/whitespace false positives
            sb.Append(Norm(c.FirstName)); sb.Append("|");
            sb.Append(Norm(c.LastName));  sb.Append("|");
            sb.Append(Norm(c.Nickname));  sb.Append("|");
            sb.Append(Norm(c.Notes));

            // Emails — address ONLY, no Kind or Description
            // Kind/Description may be normalized differently by W10M on readback
            foreach (var e in c.Emails)
            {
                sb.Append("|E:");
                sb.Append(Norm(e.Address));
            }

            // Phones — number ONLY, no Description (label)
            // Normalize number format so "+1 800-555" == "+1800555"
            foreach (var p in c.Phones)
            {
                sb.Append("|P:");
                sb.Append(NormPhone(p.Number));
            }

            foreach (var a in c.Addresses)
            {
                sb.Append("|A:");
                sb.Append(Norm(a.StreetAddress));
                sb.Append(Norm(a.Locality));
                sb.Append(Norm(a.PostalCode));
                sb.Append(Norm(a.Country));
            }

            if (c.JobInfo.Count > 0)
            {
                sb.Append("|J:");
                sb.Append(Norm(c.JobInfo[0].CompanyName));
                sb.Append(Norm(c.JobInfo[0].Title));
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

            string raw = sb.ToString();
            int hash   = 17;
            foreach (char ch in raw)
                hash = hash * 31 + ch;

            return hash.ToString();
        }

        // Normalize: trim + lowercase so minor differences don't cause false positives
        private static string Norm(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Trim().ToLowerInvariant();
        }

        // Normalize phone: digits and + only
        private static string NormPhone(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder();
            foreach (char c in s)
                if (char.IsDigit(c) || c == '+') sb.Append(c);
            return sb.ToString();
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
