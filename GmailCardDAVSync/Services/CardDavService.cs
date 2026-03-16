// Services/CardDavService.cs
// Fetches Google contacts via CardDAV (Basic Auth / App Password).
// Supports full sync and incremental sync via ETags.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using GmailCardDAVSync.Models;

namespace GmailCardDAVSync.Services
{
    // Result of FetchAllContactsAsync — replaces tuple (not supported in UWP)
    public class FetchAllResult
    {
        public List<VCardContact>          Contacts { get; set; } = new List<VCardContact>();
        public Dictionary<string, string>  Etags    { get; set; } = new Dictionary<string, string>();
    }

    // Result of GetChangesAsync
    public class SyncDiff
    {
        public List<string>                ChangedHrefs { get; set; } = new List<string>();
        public List<string>                DeletedHrefs { get; set; } = new List<string>();
        public Dictionary<string, string>  ServerEtags  { get; set; } = new Dictionary<string, string>();
    }

    public class CardDavService
    {
        private const string BaseUrl     = "https://www.google.com";
        private const string CardDavPath = "/carddav/v1/principals/{0}/lists/default/";

        private readonly HttpClient  _http;
        private readonly VCardParser _parser;
        private readonly string      _addressBookUrl;

        public CardDavService(string gmailAddress, string appPassword)
        {
            _parser         = new VCardParser();
            _addressBookUrl = BaseUrl + string.Format(CardDavPath,
                              Uri.EscapeDataString(gmailAddress));

            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes(gmailAddress + ":" + appPassword));

            _http = new HttpClient();
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", credentials);
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("GmailCardDAVSync/1.0");
            _http.Timeout = TimeSpan.FromSeconds(60);
        }

        // ================================================================
        // FULL SYNC — fetch every contact from Google
        // ================================================================
        public async Task<FetchAllResult> FetchAllContactsAsync(
            IProgress<string> progress = null)
        {
            progress?.Report("Fetching contact list from Google...");

            var serverEtags = await FetchServerEtagsAsync();

            progress?.Report("Found " + serverEtags.Count +
                             " contacts. Downloading...");

            var contacts = new List<VCardContact>();
            int fetched  = 0;

            foreach (var kv in serverEtags)
            {
                string href = kv.Key;
                string etag = kv.Value;

                var contact = await FetchSingleContactAsync(href);
                if (contact != null)
                {
                    contact.Etag = etag;
                    contact.Href = href;
                    contacts.Add(contact);
                    fetched++;

                    if (fetched % 10 == 0)
                        progress?.Report("Downloaded " + fetched +
                                         " of " + serverEtags.Count + "...");
                }
            }

            progress?.Report("Done. " + contacts.Count + " contacts downloaded.");

            var result    = new FetchAllResult();
            result.Contacts = contacts;
            result.Etags    = serverEtags;
            return result;
        }

        // ================================================================
        // INCREMENTAL SYNC — compare server etags with saved local etags
        // ================================================================
        public async Task<SyncDiff> GetChangesAsync(
            Dictionary<string, string> localEtags,
            IProgress<string> progress = null)
        {
            progress?.Report("Checking for changes on Google...");

            var serverEtags = await FetchServerEtagsAsync();
            var diff        = new SyncDiff();
            diff.ServerEtags = serverEtags;

            // Find changed or new contacts
            foreach (var kv in serverEtags)
            {
                string href       = kv.Key;
                string serverEtag = kv.Value;

                if (!localEtags.ContainsKey(href))
                    diff.ChangedHrefs.Add(href);        // new contact
                else if (localEtags[href] != serverEtag)
                    diff.ChangedHrefs.Add(href);        // modified contact
                // else: unchanged — skip
            }

            // Find deleted contacts
            foreach (var href in localEtags.Keys)
            {
                if (!serverEtags.ContainsKey(href))
                    diff.DeletedHrefs.Add(href);
            }

            progress?.Report(
                diff.ChangedHrefs.Count + " changed, " +
                diff.DeletedHrefs.Count + " deleted.");

            return diff;
        }

        // ================================================================
        // FETCH CHANGED — download only specific hrefs
        // ================================================================
        public async Task<List<VCardContact>> FetchContactsByHrefsAsync(
            List<string> hrefs,
            Dictionary<string, string> serverEtags,
            IProgress<string> progress = null)
        {
            var contacts = new List<VCardContact>();
            int fetched  = 0;

            foreach (string href in hrefs)
            {
                var contact = await FetchSingleContactAsync(href);
                if (contact != null)
                {
                    contact.Href = href;
                    if (serverEtags.ContainsKey(href))
                        contact.Etag = serverEtags[href];
                    contacts.Add(contact);
                    fetched++;

                    if (fetched % 5 == 0)
                        progress?.Report("Fetched " + fetched +
                                         " of " + hrefs.Count + "...");
                }
            }

            return contacts;
        }

        // ================================================================
        // PRIVATE — PROPFIND: get all href→etag pairs from server
        // ================================================================
        private async Task<Dictionary<string, string>> FetchServerEtagsAsync()
        {
            string propfindBody =
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<d:propfind xmlns:d=\"DAV:\">" +
                "  <d:prop>" +
                "    <d:getetag/>" +
                "    <d:resourcetype/>" +
                "  </d:prop>" +
                "</d:propfind>";

            HttpResponseMessage response;
            try
            {
                var req = new HttpRequestMessage
                {
                    Method     = new HttpMethod("PROPFIND"),
                    RequestUri = new Uri(_addressBookUrl),
                    Content    = new StringContent(propfindBody,
                                     Encoding.UTF8, "application/xml")
                };
                req.Headers.Add("Depth", "1");
                response = await _http.SendAsync(req);
            }
            catch (Exception ex)
            {
                throw new Exception("Network error: " + ex.Message, ex);
            }

            if (!response.IsSuccessStatusCode)
            {
                int code = (int)response.StatusCode;
                if (code == 401)
                    throw new Exception(
                        "Authentication failed (401).\n\n" +
                        "Use a Google App Password (16 chars), " +
                        "not your Gmail password.\n" +
                        "Create one at: myaccount.google.com/apppasswords");
                if (code == 403)
                    throw new Exception(
                        "Access denied (403).\n\n" +
                        "Check: Gmail Settings > Forwarding and POP/IMAP.");
                throw new Exception("Server error " + code + " " +
                    response.ReasonPhrase);
            }

            string xml = await response.Content.ReadAsStringAsync();
            return ParseHrefEtagPairs(xml);
        }

        // ================================================================
        // PRIVATE — GET a single contact by href
        // ================================================================
        private async Task<VCardContact> FetchSingleContactAsync(string href)
        {
            try
            {
                var getResponse = await _http.GetAsync(BaseUrl + href);
                if (!getResponse.IsSuccessStatusCode) return null;

                string vcardText = await getResponse.Content.ReadAsStringAsync();
                if (vcardText.IndexOf("BEGIN:VCARD",
                    StringComparison.OrdinalIgnoreCase) < 0) return null;

                var parsed = _parser.ParseMultiple(vcardText);
                return parsed.Count > 0 ? parsed[0] : null;
            }
            catch { return null; }
        }

        // ================================================================
        // PRIVATE — Parse href+etag pairs from PROPFIND XML
        // ================================================================
        private Dictionary<string, string> ParseHrefEtagPairs(string xml)
        {
            var result = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(xml)) return result;

            int pos = 0;
            while (true)
            {
                int respStart = IndexOfOpenTag(xml, "response", pos);
                if (respStart < 0) break;

                int respEnd = IndexOfCloseTag(xml, "response", respStart + 1);
                if (respEnd < 0) break;
                respEnd = xml.IndexOf('>', respEnd) + 1;

                string block = xml.Substring(respStart, respEnd - respStart);
                string href  = ExtractTagValue(block, "href");
                string etag  = ExtractTagValue(block, "getetag");

                if (!string.IsNullOrEmpty(href) &&
                    !href.EndsWith("/") &&
                    !string.IsNullOrEmpty(etag))
                {
                    etag = etag.Trim('"');
                    result[href] = etag;
                }

                pos = respEnd;
            }

            return result;
        }

        private string ExtractTagValue(string xml, string tagName)
        {
            int open = IndexOfOpenTag(xml, tagName, 0);
            if (open < 0) return null;

            int tagEnd = xml.IndexOf('>', open);
            if (tagEnd < 0) return null;
            tagEnd++;

            int close = IndexOfCloseTag(xml, tagName, tagEnd);
            if (close < 0) return null;

            return xml.Substring(tagEnd, close - tagEnd).Trim()
                      .Replace("&amp;", "&").Replace("&lt;", "<")
                      .Replace("&gt;", ">").Replace("&quot;", "\"");
        }

        private int IndexOfOpenTag(string xml, string tagName, int from)
        {
            int pos = from;
            while (pos < xml.Length)
            {
                int lt = xml.IndexOf('<', pos);
                if (lt < 0) return -1;
                if (lt + 1 < xml.Length && xml[lt + 1] == '/')
                    { pos = lt + 1; continue; }

                int nameStart = lt + 1;
                int colon     = xml.IndexOf(':', nameStart);
                int gt        = xml.IndexOf('>', lt);
                if (gt < 0) { pos = lt + 1; continue; }
                int check = (colon >= 0 && colon < gt) ? colon + 1 : nameStart;

                if (xml.Length - check >= tagName.Length &&
                    string.Compare(xml, check, tagName, 0,
                        tagName.Length, StringComparison.OrdinalIgnoreCase) == 0)
                    return lt;

                pos = lt + 1;
            }
            return -1;
        }

        private int IndexOfCloseTag(string xml, string tagName, int from)
        {
            int pos = from;
            while (pos < xml.Length)
            {
                int lt = xml.IndexOf('<', pos);
                if (lt < 0) return -1;
                if (lt + 1 < xml.Length && xml[lt + 1] == '/')
                {
                    int gt = xml.IndexOf('>', lt);
                    if (gt < 0) { pos = lt + 1; continue; }
                    string tag = xml.Substring(lt, gt - lt + 1);
                    if (tag.IndexOf(tagName,
                        StringComparison.OrdinalIgnoreCase) >= 0)
                        return lt;
                }
                pos = lt + 1;
            }
            return -1;
        }
    }
}
