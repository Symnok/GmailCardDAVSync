// SyncComponent/GoogleToPhoneSyncTask.cs
// Background task — runs every 15 minutes.
// Performs incremental Google→Phone sync using saved ETags.
// Self-contained — no linked files from main project.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.Background;
using Windows.ApplicationModel.Contacts;
using Windows.Security.Credentials;
using Windows.Storage;

namespace SyncComponent
{
    public sealed class GoogleToPhoneSyncTask : IBackgroundTask
    {
        private BackgroundTaskDeferral _deferral;

        // LocalSettings container names — must match main project
        private const string ETagContainer  = "CardDAVETags";
        private const string HrefContainer  = "CardDAVHrefs";
        private const string ListName       = "Gmail (CardDAV)";
        private const string CredResource   = "GmailCardDAVSync";
        private const string BaseUrl        = "https://www.google.com";
        private const string CardDavPath    = "/carddav/v1/principals/{0}/lists/default/";
        private const string LastBgSyncKey  = "LastBgSync";

        public async void Run(IBackgroundTaskInstance taskInstance)
        {
            _deferral = taskInstance.GetDeferral();
            try   { await DoSyncAsync(); }
            catch { }
            finally { _deferral.Complete(); }
        }

        // ================================================================
        // MAIN SYNC LOGIC
        // ================================================================
        private async Task DoSyncAsync()
        {
            // 1. Load credentials
            string email, password;
            if (!LoadCredentials(out email, out password)) return;

            // 2. Need prior sync for ETags to exist
            var localEtags = LoadEtags();
            if (localEtags.Count == 0) return;

            // 3. Build HTTP client
            var http = BuildHttpClient(email, password);
            string addressBookUrl = BaseUrl + string.Format(CardDavPath,
                Uri.EscapeDataString(email));

            // 4. Get server ETags — find what changed
            var serverEtags = await FetchServerEtagsAsync(http, addressBookUrl);
            if (serverEtags == null) return;

            var changedHrefs = new List<string>();
            var deletedHrefs = new List<string>();

            foreach (var kv in serverEtags)
            {
                if (!localEtags.ContainsKey(kv.Key) ||
                    localEtags[kv.Key] != kv.Value)
                    changedHrefs.Add(kv.Key);
            }
            foreach (var href in localEtags.Keys)
                if (!serverEtags.ContainsKey(href))
                    deletedHrefs.Add(href);

            if (changedHrefs.Count == 0 && deletedHrefs.Count == 0)
            {
                // No changes — just update timestamp
                ApplicationData.Current.LocalSettings.Values[LastBgSyncKey] =
                    DateTime.Now.ToString("dd MMM yyyy HH:mm") + " (no changes)";
                return;
            }

            // 5. Open contact store
            var store = await ContactManager.RequestStoreAsync(
                ContactStoreAccessType.AppContactsReadWrite);
            if (store == null) return;

            var list = await GetOrCreateListAsync(store);

            // 6. Upsert changed contacts
            foreach (var href in changedHrefs)
            {
                string vcf = await FetchVCardAsync(http, href);
                if (string.IsNullOrEmpty(vcf)) continue;

                var vc = ParseVCard(vcf, href,
                    serverEtags.ContainsKey(href) ? serverEtags[href] : "");
                if (vc == null) continue;

                await UpsertContactAsync(list, vc);
            }

            // 7. Delete removed contacts
            foreach (var href in deletedHrefs)
            {
                string uid = UidFromHref(href);
                if (!string.IsNullOrEmpty(uid))
                    await DeleteContactByUidAsync(list, uid);
            }

            // 8. Save updated ETags
            SaveEtags(serverEtags);

            // 9. Save uid→href for any new contacts
            var uidHrefs = LoadUidHrefs();
            foreach (var vc in changedHrefs)
            {
                // href format: /carddav/.../uid.vcf
                string uid = UidFromHref(vc);
                if (!string.IsNullOrEmpty(uid))
                    uidHrefs[uid] = vc;
            }
            SaveUidHrefs(uidHrefs);

            // 10. Log timestamp
            ApplicationData.Current.LocalSettings.Values[LastBgSyncKey] =
                DateTime.Now.ToString("dd MMM yyyy HH:mm");
        }

        // ================================================================
        // CONTACT STORE
        // ================================================================
        private async Task<ContactList> GetOrCreateListAsync(ContactStore store)
        {
            var lists = await store.FindContactListsAsync();
            foreach (var l in lists)
                if (l.DisplayName == ListName) return l;

            var newList = await store.CreateContactListAsync(ListName);
            newList.OtherAppReadAccess  = ContactListOtherAppReadAccess.Full;
            newList.OtherAppWriteAccess = ContactListOtherAppWriteAccess.SystemOnly;
            await newList.SaveAsync();
            return newList;
        }

        private async Task UpsertContactAsync(ContactList list, ParsedContact vc)
        {
            // Find by RemoteId or name — delete then recreate
            string searchName = (vc.FirstName + " " + vc.LastName)
                .Trim().ToLowerInvariant();

            var reader = list.GetContactReader();
            var batch  = await reader.ReadBatchAsync();
            while (batch.Contacts.Count > 0)
            {
                foreach (var c in batch.Contacts)
                {
                    bool byId   = !string.IsNullOrEmpty(vc.Uid) &&
                                   c.RemoteId == vc.Uid;
                    bool byName = !string.IsNullOrEmpty(searchName) &&
                                   (c.FirstName + " " + c.LastName)
                                   .Trim().ToLowerInvariant() == searchName;
                    if (byId || byName)
                    {
                        await list.DeleteContactAsync(c);
                        break;
                    }
                }
                batch = await reader.ReadBatchAsync();
            }

            await list.SaveContactAsync(ToUwpContact(vc));
        }

        private async Task DeleteContactByUidAsync(ContactList list, string uid)
        {
            var reader = list.GetContactReader();
            var batch  = await reader.ReadBatchAsync();
            while (batch.Contacts.Count > 0)
            {
                foreach (var c in batch.Contacts)
                {
                    if (c.RemoteId == uid)
                    {
                        await list.DeleteContactAsync(c);
                        return;
                    }
                }
                batch = await reader.ReadBatchAsync();
            }
        }

        private Contact ToUwpContact(ParsedContact vc)
        {
            var c = new Contact
            {
                FirstName  = vc.FirstName  ?? "",
                LastName   = vc.LastName   ?? "",
                MiddleName = "",
                Nickname   = vc.Nickname   ?? "",
                Notes      = vc.Notes      ?? "",
                RemoteId   = vc.Uid        ?? ""
            };

            foreach (var e in vc.Emails)
                c.Emails.Add(new ContactEmail
                {
                    Address = e.Address ?? "",
                    Kind    = e.IsWork
                        ? ContactEmailKind.Work
                        : ContactEmailKind.Personal
                });

            foreach (var p in vc.Phones)
                c.Phones.Add(new ContactPhone
                {
                    Number      = p.Number ?? "",
                    Description = p.Description ?? ""
                });

            if (!string.IsNullOrEmpty(vc.Org) || !string.IsNullOrEmpty(vc.Title))
                c.JobInfo.Add(new ContactJobInfo
                {
                    CompanyName = vc.Org   ?? "",
                    Title       = vc.Title ?? ""
                });

            if (!string.IsNullOrEmpty(vc.Birthday))
            {
                DateTimeOffset bday;
                if (TryParseBirthday(vc.Birthday, out bday))
                    c.ImportantDates.Add(new ContactDate
                    {
                        Kind  = ContactDateKind.Birthday,
                        Day   = (uint)bday.Day,
                        Month = (uint)bday.Month,
                        Year  = bday.Year
                    });
            }

            if (string.IsNullOrEmpty(c.FirstName) && string.IsNullOrEmpty(c.LastName)
                && !string.IsNullOrEmpty(vc.DisplayName))
                c.Nickname = vc.DisplayName;

            return c;
        }

        // ================================================================
        // CARDDAV HTTP
        // ================================================================
        private HttpClient BuildHttpClient(string email, string password)
        {
            string cred = Convert.ToBase64String(
                Encoding.UTF8.GetBytes(email + ":" + password));
            var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", cred);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("GmailCardDAVSync/1.0");
            http.Timeout = TimeSpan.FromSeconds(30);
            return http;
        }

        private async Task<Dictionary<string, string>> FetchServerEtagsAsync(
            HttpClient http, string addressBookUrl)
        {
            try
            {
                string body =
                    "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                    "<d:propfind xmlns:d=\"DAV:\">" +
                    "<d:prop><d:getetag/><d:resourcetype/></d:prop>" +
                    "</d:propfind>";

                var req = new HttpRequestMessage
                {
                    Method     = new HttpMethod("PROPFIND"),
                    RequestUri = new Uri(addressBookUrl),
                    Content    = new StringContent(body, Encoding.UTF8, "application/xml")
                };
                req.Headers.Add("Depth", "1");

                var resp = await http.SendAsync(req);
                if (!resp.IsSuccessStatusCode) return null;

                string xml = await resp.Content.ReadAsStringAsync();
                return ParseHrefEtagPairs(xml);
            }
            catch { return null; }
        }

        private async Task<string> FetchVCardAsync(HttpClient http, string href)
        {
            try
            {
                var resp = await http.GetAsync(BaseUrl + href);
                if (!resp.IsSuccessStatusCode) return null;
                return await resp.Content.ReadAsStringAsync();
            }
            catch { return null; }
        }

        // ================================================================
        // VCARD PARSER — minimal, handles what Google sends
        // ================================================================
        private ParsedContact ParseVCard(string vcf, string href, string etag)
        {
            var vc = new ParsedContact { Href = href, Etag = etag };

            string[] lines = vcf.Split(
                new string[] { "\r\n", "\n" },
                StringSplitOptions.RemoveEmptyEntries);

            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();
                int colon   = line.IndexOf(':');
                if (colon < 0) continue;

                string prop  = line.Substring(0, colon).ToUpperInvariant();
                string value = line.Substring(colon + 1).Trim();

                // Skip item1./item2. prefixes (Google format)
                if (prop.StartsWith("ITEM")) continue;
                if (prop.Contains("X-ABLABEL")) continue;

                // Strip param part for property name lookup
                string propName = prop.Contains(";")
                    ? prop.Substring(0, prop.IndexOf(';'))
                    : prop;

                string typeParam = "";
                if (prop.Contains(";TYPE="))
                    typeParam = prop.Substring(prop.IndexOf(";TYPE=") + 6)
                        .ToLowerInvariant();

                switch (propName)
                {
                    case "UID":      vc.Uid         = value; break;
                    case "FN":       vc.DisplayName = UnescapeVCard(value); break;
                    case "NICKNAME": vc.Nickname    = UnescapeVCard(value); break;
                    case "NOTE":     vc.Notes       = UnescapeVCard(value); break;
                    case "ORG":      vc.Org         = UnescapeVCard(value.Split(';')[0]); break;
                    case "TITLE":    vc.Title       = UnescapeVCard(value); break;
                    case "BDAY":     vc.Birthday    = value; break;

                    case "N":
                        var parts = value.Split(';');
                        if (parts.Length > 0) vc.LastName  = UnescapeVCard(parts[0]);
                        if (parts.Length > 1) vc.FirstName = UnescapeVCard(parts[1]);
                        break;

                    case "EMAIL":
                        vc.Emails.Add(new ParsedEmail
                        {
                            Address = value,
                            IsWork  = typeParam.Contains("work")
                        });
                        break;

                    case "TEL":
                        vc.Phones.Add(new ParsedPhone
                        {
                            Number      = value,
                            Description = PhoneTypeToDescription(typeParam)
                        });
                        break;
                }
            }

            if (string.IsNullOrEmpty(vc.Uid))
                vc.Uid = UidFromHref(href);

            return vc;
        }

        private string PhoneTypeToDescription(string type)
        {
            if (string.IsNullOrEmpty(type)) return "Mobile";
            if (type.Contains("home"))   return "Home";
            if (type.Contains("work"))   return "Work";
            if (type.Contains("cell") ||
                type.Contains("mobile")) return "Mobile";
            if (type.Contains("pager"))  return "Pager";
            if (type.Contains("fax"))    return "Fax";
            return "Mobile";
        }

        private string UnescapeVCard(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Replace("\\n", "\n").Replace("\\N", "\n")
                    .Replace("\\,", ",").Replace("\\;", ";")
                    .Replace("\\\\", "\\");
        }

        private string UidFromHref(string href)
        {
            int slash = href.LastIndexOf('/');
            if (slash < 0) return href;
            string name = href.Substring(slash + 1);
            if (name.EndsWith(".vcf", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 4);
            return name;
        }

        private bool TryParseBirthday(string s, out DateTimeOffset result)
        {
            result = DateTimeOffset.MinValue;
            s = s.Replace("-", "");
            if (s.Length < 8) return false;
            int y, m, d;
            if (!int.TryParse(s.Substring(0, 4), out y)) return false;
            if (!int.TryParse(s.Substring(4, 2), out m)) return false;
            if (!int.TryParse(s.Substring(6, 2), out d)) return false;
            try
            {
                result = new DateTimeOffset(y, m, d, 0, 0, 0, TimeSpan.Zero);
                return true;
            }
            catch { return false; }
        }

        // ================================================================
        // PROPFIND XML PARSER
        // ================================================================
        private Dictionary<string, string> ParseHrefEtagPairs(string xml)
        {
            var result = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(xml)) return result;

            int pos = 0;
            while (true)
            {
                int start = FindOpenTag(xml, "response", pos);
                if (start < 0) break;
                int end = FindCloseTag(xml, "response", start + 1);
                if (end < 0) break;
                end = xml.IndexOf('>', end) + 1;

                string block = xml.Substring(start, end - start);
                string href  = GetTagValue(block, "href");
                string etag  = GetTagValue(block, "getetag");

                if (!string.IsNullOrEmpty(href) && !href.EndsWith("/") &&
                    !string.IsNullOrEmpty(etag))
                    result[href] = etag.Trim('"');

                pos = end;
            }
            return result;
        }

        private string GetTagValue(string xml, string tag)
        {
            int open = FindOpenTag(xml, tag, 0);
            if (open < 0) return null;
            int end = xml.IndexOf('>', open);
            if (end < 0) return null;
            int close = FindCloseTag(xml, tag, end + 1);
            if (close < 0) return null;
            return xml.Substring(end + 1, close - end - 1).Trim();
        }

        private int FindOpenTag(string xml, string tag, int from)
        {
            int pos = from;
            while (pos < xml.Length)
            {
                int lt = xml.IndexOf('<', pos);
                if (lt < 0) return -1;
                if (lt + 1 < xml.Length && xml[lt + 1] == '/') { pos = lt + 1; continue; }
                int gt    = xml.IndexOf('>', lt);
                if (gt < 0) { pos = lt + 1; continue; }
                int colon = xml.IndexOf(':', lt);
                int check = (colon >= 0 && colon < gt) ? colon + 1 : lt + 1;
                if (xml.Length - check >= tag.Length &&
                    string.Compare(xml, check, tag, 0, tag.Length,
                        StringComparison.OrdinalIgnoreCase) == 0)
                    return lt;
                pos = lt + 1;
            }
            return -1;
        }

        private int FindCloseTag(string xml, string tag, int from)
        {
            int pos = from;
            while (pos < xml.Length)
            {
                int lt = xml.IndexOf('<', pos);
                if (lt < 0) return -1;
                if (lt + 1 < xml.Length && xml[lt + 1] == '/')
                {
                    int gt  = xml.IndexOf('>', lt);
                    if (gt < 0) { pos = lt + 1; continue; }
                    string t = xml.Substring(lt, gt - lt + 1);
                    if (t.IndexOf(tag, StringComparison.OrdinalIgnoreCase) >= 0)
                        return lt;
                }
                pos = lt + 1;
            }
            return -1;
        }

        // ================================================================
        // CREDENTIALS
        // ================================================================
        private bool LoadCredentials(out string email, out string password)
        {
            email = password = null;
            try
            {
                var vault = new PasswordVault();
                var creds = vault.FindAllByResource(CredResource);
                if (creds == null || creds.Count == 0) return false;
                var cred = creds[0];
                cred.RetrievePassword();
                email    = cred.UserName;
                password = cred.Password;
                return true;
            }
            catch { return false; }
        }

        // ================================================================
        // ETAG STORAGE — mirrors ETagStorage.cs in main project
        // ================================================================
        private Dictionary<string, string> LoadEtags()
        {
            var result   = new Dictionary<string, string>();
            var settings = ApplicationData.Current.LocalSettings;
            if (!settings.Containers.ContainsKey(ETagContainer)) return result;
            var container = settings.Containers[ETagContainer];
            foreach (var kv in container.Values)
            {
                string s = kv.Value as string;
                if (string.IsNullOrEmpty(s)) continue;
                int sep = s.IndexOf('|');
                if (sep < 0) continue;
                result[s.Substring(0, sep)] = s.Substring(sep + 1);
            }
            return result;
        }

        private void SaveEtags(Dictionary<string, string> etags)
        {
            var settings  = ApplicationData.Current.LocalSettings;
            var container = settings.CreateContainer(ETagContainer,
                ApplicationDataCreateDisposition.Always);
            container.Values.Clear();
            foreach (var kv in etags)
            {
                string key = SafeKey(kv.Key);
                container.Values[key] = kv.Key + "|" + kv.Value;
            }
        }

        private Dictionary<string, string> LoadUidHrefs()
        {
            var result   = new Dictionary<string, string>();
            var settings = ApplicationData.Current.LocalSettings;
            if (!settings.Containers.ContainsKey(HrefContainer)) return result;
            var container = settings.Containers[HrefContainer];
            foreach (var kv in container.Values)
            {
                string s = kv.Value as string;
                if (string.IsNullOrEmpty(s)) continue;
                int sep = s.IndexOf('|');
                if (sep < 0) continue;
                result[s.Substring(0, sep)] = s.Substring(sep + 1);
            }
            return result;
        }

        private void SaveUidHrefs(Dictionary<string, string> hrefs)
        {
            var settings  = ApplicationData.Current.LocalSettings;
            var container = settings.CreateContainer(HrefContainer,
                ApplicationDataCreateDisposition.Always);
            container.Values.Clear();
            foreach (var kv in hrefs)
                if (!string.IsNullOrEmpty(kv.Key))
                    container.Values[SafeKey(kv.Key)] = kv.Key + "|" + kv.Value;
        }

        private string SafeKey(string s)
        {
            return s.Length <= 200 ? s : s.Substring(s.Length - 200);
        }

        // ================================================================
        // SIMPLE DATA CLASSES
        // ================================================================
        private class ParsedContact
        {
            public string Uid         { get; set; }
            public string Href        { get; set; }
            public string Etag        { get; set; }
            public string DisplayName { get; set; }
            public string FirstName   { get; set; }
            public string LastName    { get; set; }
            public string Nickname    { get; set; }
            public string Notes       { get; set; }
            public string Org         { get; set; }
            public string Title       { get; set; }
            public string Birthday    { get; set; }
            public List<ParsedEmail> Emails { get; set; } = new List<ParsedEmail>();
            public List<ParsedPhone> Phones { get; set; } = new List<ParsedPhone>();
        }

        private class ParsedEmail
        {
            public string Address { get; set; }
            public bool   IsWork  { get; set; }
        }

        private class ParsedPhone
        {
            public string Number      { get; set; }
            public string Description { get; set; }
        }
    }
}
