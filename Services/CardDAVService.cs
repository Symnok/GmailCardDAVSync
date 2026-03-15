// Services/CardDavService.cs
// Mirrors the working Python approach from getGoogleContacts.py:
//   Step 1 — PROPFIND to get list of contact hrefs
//   Step 2 — GET each contact individually
// Uses www.google.com/carddav (not googleapis.com)

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using GmailCardDAVSync.Models;

namespace GmailCardDAVSync.Services
{
    public class CardDavService
    {
        // Base URL — matches the Python script exactly
        private const string BaseUrl = "https://www.google.com";
        private const string CardDavPath = "/carddav/v1/principals/{0}/lists/default/";

        private readonly HttpClient _http;
        private readonly VCardParser _parser;
        private readonly string _userEmail;

        public CardDavService(string gmailAddress, string appPassword)
        {
            _userEmail = gmailAddress;
            _parser = new VCardParser();

            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes(gmailAddress + ":" + appPassword));

            _http = new HttpClient();
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", credentials);
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("GmailCardDAVSync/1.0");
            _http.Timeout = TimeSpan.FromSeconds(60);
        }

        public async Task<List<VCardContact>> FetchAllContactsAsync(
            IProgress<string> progress = null)
        {
            progress?.Report("Step 1: Fetching contact list...");

            // -------------------------------------------------------
            // STEP 1 — PROPFIND to get hrefs (same as Python script)
            // -------------------------------------------------------
            string addressBookUrl = BaseUrl +
                string.Format(CardDavPath, Uri.EscapeDataString(_userEmail));

            string propfindBody =
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<d:propfind xmlns:d=\"DAV:\" xmlns:card=\"urn:ietf:params:xml:ns:carddav\">" +
                "  <d:prop>" +
                "    <d:getetag/>" +
                "    <d:displayname/>" +
                "    <d:resourcetype/>" +
                "  </d:prop>" +
                "</d:propfind>";

            HttpResponseMessage propfindResponse;
            try
            {
                var request = new HttpRequestMessage
                {
                    Method = new HttpMethod("PROPFIND"),
                    RequestUri = new Uri(addressBookUrl),
                    Content = new StringContent(propfindBody, Encoding.UTF8,
                                                   "application/xml")
                };
                request.Headers.Add("Depth", "1");
                propfindResponse = await _http.SendAsync(request);
            }
            catch (Exception ex)
            {
                throw new Exception("Network error during PROPFIND: " + ex.Message, ex);
            }

            if (!propfindResponse.IsSuccessStatusCode)
            {
                int code = (int)propfindResponse.StatusCode;
                if (code == 401)
                    throw new Exception(
                        "Authentication failed (401).\n\n" +
                        "Use a Google App Password (16 chars), not your Gmail password.\n" +
                        "Create one at: myaccount.google.com/apppasswords");
                if (code == 403)
                    throw new Exception(
                        "Access denied (403).\n\n" +
                        "Check: Gmail Settings > Forwarding and POP/IMAP > enable IMAP.");

                throw new Exception("PROPFIND failed with " + code + " " +
                    propfindResponse.ReasonPhrase);
            }

            string propfindXml = await propfindResponse.Content.ReadAsStringAsync();
            List<string> hrefs = ExtractHrefs(propfindXml);

            progress?.Report("Found " + hrefs.Count + " contacts. Fetching...");

            // -------------------------------------------------------
            // STEP 2 — GET each contact individually (same as Python)
            // -------------------------------------------------------
            var contacts = new List<VCardContact>();
            int fetched = 0;

            foreach (string href in hrefs)
            {
                // Skip the address book root entry itself
                if (!href.EndsWith(".vcf", StringComparison.OrdinalIgnoreCase) &&
                    !href.Contains("/carddav/v1/principals/"))
                {
                    // might still be a contact without .vcf extension — try it
                }

                // Skip the collection itself (ends with /)
                if (href.EndsWith("/")) continue;

                try
                {
                    string contactUrl = BaseUrl + href;
                    var getResponse = await _http.GetAsync(contactUrl);

                    if (getResponse.IsSuccessStatusCode)
                    {
                        string vcardText = await getResponse.Content.ReadAsStringAsync();
                        if (vcardText.IndexOf("BEGIN:VCARD",
                            StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            var parsed = _parser.ParseMultiple(vcardText);
                            contacts.AddRange(parsed);
                            fetched++;
                        }
                    }
                }
                catch { /* skip individual failed contacts */ }

                // Update progress every 10 contacts
                if (fetched % 10 == 0 && fetched > 0)
                    progress?.Report("Fetched " + fetched + " of " +
                                     hrefs.Count + "...");
            }

            progress?.Report("Done. " + contacts.Count + " contacts fetched.");
            return contacts;
        }

        // ---------------------------------------------------------------
        // Extract href values from PROPFIND XML response.
        // Scans for <d:href> or <href> tags — no XPath needed.
        // ---------------------------------------------------------------
        private List<string> ExtractHrefs(string xml)
        {
            var hrefs = new List<string>();
            if (string.IsNullOrEmpty(xml)) return hrefs;

            int pos = 0;
            while (true)
            {
                // Find any href tag (with or without namespace prefix)
                int hrefStart = IndexOfTag(xml, "href", pos);
                if (hrefStart < 0) break;

                // Skip to end of opening tag
                int tagEnd = xml.IndexOf('>', hrefStart);
                if (tagEnd < 0) break;
                tagEnd++;

                // Find closing tag
                int closeTag = IndexOfCloseTag(xml, "href", tagEnd);
                if (closeTag < 0) break;

                string href = xml.Substring(tagEnd, closeTag - tagEnd).Trim();
                if (!string.IsNullOrEmpty(href))
                    hrefs.Add(href);

                pos = closeTag + 1;
            }

            return hrefs;
        }

        private int IndexOfTag(string xml, string tagName, int from)
        {
            int pos = from;
            while (pos < xml.Length)
            {
                int lt = xml.IndexOf('<', pos);
                if (lt < 0) return -1;

                // Skip whitespace and optional namespace prefix after <
                int nameStart = lt + 1;
                while (nameStart < xml.Length && xml[nameStart] == '/') nameStart++;

                // Skip namespace prefix (e.g. "d:" in "<d:href>")
                int colon = xml.IndexOf(':', nameStart);
                int gtPos = xml.IndexOf('>', lt);
                int nameCheck = (colon >= 0 && colon < gtPos) ? colon + 1 : nameStart;

                int remaining = xml.Length - nameCheck;
                if (remaining >= tagName.Length &&
                    string.Compare(xml, nameCheck, tagName, 0,
                                   tagName.Length, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    // Make sure it's not a closing tag
                    if (xml[lt + 1] != '/')
                        return lt;
                }
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
                    string tag = xml.Substring(lt, gt - lt + 1);
                    if (tag.IndexOf(tagName, StringComparison.OrdinalIgnoreCase) >= 0)
                        return lt;
                }
                pos = lt + 1;
            }
            return -1;
        }
    }
}
