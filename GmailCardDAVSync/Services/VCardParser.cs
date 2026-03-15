// Services/VCardParser.cs
// Parses raw vCard 3.0 / 4.0 text into VCardContact objects.
// Based on the approach used in davege1107/CardDAVUtilities (Basic Auth, no OAuth2).

using System;
using System.Collections.Generic;
using GmailCardDAVSync.Models;

namespace GmailCardDAVSync.Services
{
    public class VCardParser
    {
        // ---------------------------------------------------------------
        // PUBLIC: Parse a block of text that may contain 1..N vCards
        // ---------------------------------------------------------------
        public List<VCardContact> ParseMultiple(string rawData)
        {
            var results = new List<VCardContact>();
            if (string.IsNullOrWhiteSpace(rawData)) return results;

            // Un-fold RFC 6350 line continuations (CRLF + whitespace = one logical line)
            rawData = rawData.Replace("\r\n ", "").Replace("\r\n\t", "")
                             .Replace("\n ",   "").Replace("\n\t",   "");

            int searchFrom = 0;
            while (true)
            {
                int begin = rawData.IndexOf("BEGIN:VCARD", searchFrom,
                                            StringComparison.OrdinalIgnoreCase);
                if (begin < 0) break;

                int end = rawData.IndexOf("END:VCARD", begin,
                                          StringComparison.OrdinalIgnoreCase);
                if (end < 0) break;

                end += "END:VCARD".Length;
                string block = rawData.Substring(begin, end - begin);
                results.Add(ParseSingle(block));
                searchFrom = end;
            }

            return results;
        }

        // ---------------------------------------------------------------
        // PUBLIC: Parse one vCard block
        // ---------------------------------------------------------------
        public VCardContact ParseSingle(string vCardBlock)
        {
            var contact = new VCardContact();
            var lines   = vCardBlock.Split(
                new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line)) continue;

                // Split into  PROPERTY[;params] : VALUE
                int colonPos = line.IndexOf(':');
                if (colonPos < 0) continue;

                string propFull = line.Substring(0, colonPos).ToUpperInvariant();
                string value    = line.Substring(colonPos + 1).Trim();

                // Base property name (before any semicolons / parameters)
                string propName = propFull.Split(';')[0];

                switch (propName)
                {
                    case "FN":
                        contact.DisplayName = Unescape(value);
                        break;

                    case "N":
                        ParseN(value, contact);
                        break;

                    case "EMAIL":
                        contact.Emails.Add(new ContactEmail
                        {
                            Address = Unescape(value),
                            Type    = ExtractParam(propFull, "TYPE", "other")
                        });
                        break;

                    case "TEL":
                        contact.Phones.Add(new ContactPhone
                        {
                            Number = Unescape(value),
                            Type   = ExtractParam(propFull, "TYPE", "other")
                        });
                        break;

                    case "ADR":
                        contact.Addresses.Add(ParseAdr(propFull, value));
                        break;

                    case "ORG":
                        // ORG may contain dept after semicolon: CompanyName;Dept
                        contact.Organization = Unescape(value.Split(';')[0]);
                        break;

                    case "TITLE":
                        contact.JobTitle = Unescape(value);
                        break;

                    case "NOTE":
                        contact.Notes = Unescape(value);
                        break;

                    case "NICKNAME":
                        contact.Nickname = Unescape(value);
                        break;

                    case "BDAY":
                        contact.Birthday = value;
                        break;

                    case "PHOTO":
                        // Only store URI-type photos (skip base64 inline for W10M)
                        if (propFull.Contains("VALUE=URI") || value.StartsWith("http",
                            StringComparison.OrdinalIgnoreCase))
                        {
                            contact.PhotoUrl = value;
                        }
                        break;

                    case "UID":
                        contact.Uid = value;
                        break;
                }
            }

            // Fallback: if FN was missing, build from N parts
            if (string.IsNullOrEmpty(contact.DisplayName))
            {
                contact.DisplayName =
                    (contact.FirstName + " " + contact.LastName).Trim();
            }

            return contact;
        }

        // ---------------------------------------------------------------
        // PRIVATE helpers
        // ---------------------------------------------------------------

        // N:LastName;FirstName;Middle;Prefix;Suffix
        private void ParseN(string value, VCardContact c)
        {
            var parts = value.Split(';');
            if (parts.Length > 0) c.LastName   = Unescape(parts[0]);
            if (parts.Length > 1) c.FirstName  = Unescape(parts[1]);
            if (parts.Length > 2) c.MiddleName = Unescape(parts[2]);
            if (parts.Length > 3) c.NamePrefix = Unescape(parts[3]);
            if (parts.Length > 4) c.NameSuffix = Unescape(parts[4]);
        }

        // ADR;TYPE=home:POBox;Ext;Street;City;Region;PostalCode;Country
        private ContactAddress ParseAdr(string propFull, string value)
        {
            var parts = value.Split(';');
            return new ContactAddress
            {
                // parts[0]=POBox, parts[1]=Extended, parts[2]=Street
                Street     = parts.Length > 2 ? Unescape(parts[2]) : string.Empty,
                City       = parts.Length > 3 ? Unescape(parts[3]) : string.Empty,
                Region     = parts.Length > 4 ? Unescape(parts[4]) : string.Empty,
                PostalCode = parts.Length > 5 ? Unescape(parts[5]) : string.Empty,
                Country    = parts.Length > 6 ? Unescape(parts[6]) : string.Empty,
                Type       = ExtractParam(propFull, "TYPE", "other")
            };
        }

        // Extract a named parameter value from the property descriptor
        // e.g. "EMAIL;TYPE=WORK" → ExtractParam(..., "TYPE", "other") → "work"
        private string ExtractParam(string propFull, string paramName, string defaultVal)
        {
            string search = paramName + "=";
            int idx = propFull.IndexOf(search, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return defaultVal;

            idx += search.Length;
            int end = propFull.IndexOf(';', idx);
            string val = end < 0
                ? propFull.Substring(idx)
                : propFull.Substring(idx, end - idx);

            return val.ToLowerInvariant();
        }

        // Decode vCard escape sequences  \n  \,  \;  \\
        private string Unescape(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Replace("\\n", "\n")
                    .Replace("\\N", "\n")
                    .Replace("\\,", ",")
                    .Replace("\\;", ";")
                    .Replace("\\\\", "\\");
        }
    }
}
