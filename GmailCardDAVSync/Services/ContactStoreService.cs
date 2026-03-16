// Services/ContactStoreService.cs
// Writes contacts to W10M ContactStore.
// Supports full sync and incremental update/delete.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.ApplicationModel.Contacts;

using VCardEmail   = GmailCardDAVSync.Models.ContactEmail;
using VCardPhone   = GmailCardDAVSync.Models.ContactPhone;
using VCardAddr    = GmailCardDAVSync.Models.ContactAddress;
using VCardWeb     = GmailCardDAVSync.Models.ContactWebsite;
using VCardContact = GmailCardDAVSync.Models.VCardContact;
using GmailCardDAVSync.Helpers;
using Windows.ApplicationModel.Contacts;

namespace GmailCardDAVSync.Services
{
    public class ContactStoreService
    {
        private const string ListDisplayName = "Gmail (CardDAV)";

        // ================================================================
        // FULL SYNC — clear list and rewrite all contacts
        // ================================================================
        public async Task<int> SyncAsync(
            List<VCardContact> contacts,
            IProgress<string> progress = null)
        {
            var store = await GetStoreAsync();
            var list  = await GetOrCreateListAsync(store);

            progress?.Report("Clearing old contacts...");
            await ClearListAsync(list);

            progress?.Report("Writing " + contacts.Count + " contacts...");
            int saved = 0;
            var hashMap = new System.Collections.Generic.Dictionary<string, string>();

            foreach (var vc in contacts)
            {
                try
                {
                    var uwpContact = ToUwpContact(vc);
                    await list.SaveContactAsync(uwpContact);
                    SaveLabels(vc);

                    // Save hash of this contact so we can detect changes later
                    if (!string.IsNullOrEmpty(vc.Uid))
                        hashMap[vc.Uid] = ContactHashStorage.ComputeHash(uwpContact);

                    saved++;
                }
                catch { }
            }

            // Persist hashes for Phone→Google change detection
            ContactHashStorage.SaveAll(hashMap);

            return saved;
        }

        // ================================================================
        // INCREMENTAL UPDATE — save or update a single contact by UID.
        // Finds existing contact by RemoteId (= UID), updates it,
        // or creates it if not found.
        // ================================================================
        public async Task UpsertContactAsync(VCardContact vc)
        {
            var store = await GetStoreAsync();
            var list  = await GetOrCreateListAsync(store);

            // Find and delete existing contact by RemoteId (UID)
            // then re-create it — safer than trying to update in place
            // which throws Arg_ArgumentException on W10M
            if (!string.IsNullOrEmpty(vc.Uid))
            {
                var reader = list.GetContactReader();
                var batch  = await reader.ReadBatchAsync();
                while (batch.Contacts.Count > 0)
                {
                    foreach (var c in batch.Contacts)
                    {
                        if (c.RemoteId == vc.Uid)
                        {
                            await list.DeleteContactAsync(c);
                            break;
                        }
                    }
                    batch = await reader.ReadBatchAsync();
                }
            }

            // Save as fresh contact
            var upserted = ToUwpContact(vc);
            await list.SaveContactAsync(upserted);
            SaveLabels(vc);

            // Update stored hash
            if (!string.IsNullOrEmpty(vc.Uid))
            {
                var hashes = ContactHashStorage.LoadAll();
                hashes[vc.Uid] = ContactHashStorage.ComputeHash(upserted);
                ContactHashStorage.SaveAll(hashes);
            }
        }

        // ================================================================
        // INCREMENTAL DELETE — remove a contact by UID
        // ================================================================
        public async Task DeleteContactByUidAsync(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return;

            var store = await GetStoreAsync();
            var list  = await GetOrCreateListAsync(store);

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


        // ================================================================
        // READ ALL — read all contacts from our Gmail list on the phone
        // Used for Phone → Google sync
        // ================================================================
        public async Task<System.Collections.Generic.List<Windows.ApplicationModel.Contacts.Contact>>
            ReadAllContactsAsync(IProgress<string> progress = null)
        {
            var store  = await GetStoreAsync();
            var list   = await GetOrCreateListAsync(store);
            var result = new System.Collections.Generic.List<Windows.ApplicationModel.Contacts.Contact>();

            progress?.Report("Reading contacts from phone...");

            var reader = list.GetContactReader();
            var batch  = await reader.ReadBatchAsync();
            while (batch.Contacts.Count > 0)
            {
                foreach (var c in batch.Contacts)
                    result.Add(c);
                batch = await reader.ReadBatchAsync();
            }

            progress?.Report("Read " + result.Count + " contacts from phone.");
            return result;
        }

        // ================================================================
        // PRIVATE helpers
        // ================================================================
        private async Task<ContactStore> GetStoreAsync()
        {
            var store = await ContactManager.RequestStoreAsync(
                ContactStoreAccessType.AppContactsReadWrite);
            if (store == null)
                throw new Exception(
                    "Could not open ContactStore.\n" +
                    "Please grant Contacts permission in Settings.");
            return store;
        }

        private async Task<ContactList> GetOrCreateListAsync(ContactStore store)
        {
            var lists = await store.FindContactListsAsync();
            foreach (var l in lists)
                if (l.DisplayName == ListDisplayName)
                    return l;

            var newList = await store.CreateContactListAsync(ListDisplayName);
            newList.OtherAppReadAccess  = ContactListOtherAppReadAccess.Full;
            newList.OtherAppWriteAccess = ContactListOtherAppWriteAccess.SystemOnly;
            await newList.SaveAsync();
            return newList;
        }

        private async Task ClearListAsync(ContactList list)
        {
            var reader = list.GetContactReader();
            var batch  = await reader.ReadBatchAsync();
            while (batch.Contacts.Count > 0)
            {
                foreach (var c in batch.Contacts)
                    await list.DeleteContactAsync(c);
                batch = await reader.ReadBatchAsync();
            }
        }

        private Contact ToUwpContact(VCardContact vc)
        {
            var c = new Contact
            {
                FirstName           = vc.FirstName  ?? string.Empty,
                LastName            = vc.LastName   ?? string.Empty,
                MiddleName          = string.Empty,  // always ignored
                HonorificNamePrefix = vc.NamePrefix ?? string.Empty,
                HonorificNameSuffix = vc.NameSuffix ?? string.Empty,
                Nickname            = vc.Nickname   ?? string.Empty,
                Notes               = vc.Notes      ?? string.Empty,
                RemoteId            = vc.Uid        ?? string.Empty
            };

            foreach (VCardEmail e in vc.Emails)
                c.Emails.Add(new Windows.ApplicationModel.Contacts.ContactEmail
                {
                    Address = e.Address ?? string.Empty,
                    Kind    = ParseEmailKind(e.Type)
                });

            foreach (VCardPhone p in vc.Phones)
                c.Phones.Add(new Windows.ApplicationModel.Contacts.ContactPhone
                {
                    Number      = p.Number ?? string.Empty,
                    Description = PhoneTypeToDescription(p.Type)
                });

            foreach (VCardAddr a in vc.Addresses)
                c.Addresses.Add(new Windows.ApplicationModel.Contacts.ContactAddress
                {
                    StreetAddress = a.Street     ?? string.Empty,
                    Locality      = a.City       ?? string.Empty,
                    Region        = a.Region     ?? string.Empty,
                    PostalCode    = a.PostalCode ?? string.Empty,
                    Country       = a.Country   ?? string.Empty,
                    Kind          = ParseAddressKind(a.Type)
                });

            foreach (VCardWeb w in vc.Websites)
                if (!string.IsNullOrEmpty(w.Url))
                {
                    try
                    {
                        c.Websites.Add(new Windows.ApplicationModel.Contacts.ContactWebsite
                        {
                            Uri         = new Uri(w.Url),
                            Description = ParseWebsiteDescription(w.Type)
                        });
                    }
                    catch { }
                }

            if (!string.IsNullOrEmpty(vc.Organization) ||
                !string.IsNullOrEmpty(vc.JobTitle))
                c.JobInfo.Add(new ContactJobInfo
                {
                    CompanyName = vc.Organization ?? string.Empty,
                    Title       = vc.JobTitle     ?? string.Empty
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

        private ContactEmailKind ParseEmailKind(string type)
        {
            if (string.IsNullOrEmpty(type)) return ContactEmailKind.Personal; // default: Home
            if (type.Contains("home"))      return ContactEmailKind.Personal;
            if (type.Contains("work"))      return ContactEmailKind.Work;
            return ContactEmailKind.Personal; // default: Home
        }

        // Use Description field for phone type — avoids ContactPhoneNumberKind
        // which requires Windows Mobile Extensions SDK
        private string PhoneTypeToDescription(string type)
        {
            if (string.IsNullOrEmpty(type))  return "Mobile"; // default
            string t = type.ToLowerInvariant();
            if (t.Contains("home"))          return "Home";
            if (t.Contains("work") ||
                t.Contains("office"))        return "Work";
            if (t.Contains("cell") ||
                t.Contains("mobile"))        return "Mobile";
            if (t.Contains("pager"))         return "Pager";
            if (t.Contains("fax"))           return "Fax";
            return "Mobile";                 // default
        }

        private ContactAddressKind ParseAddressKind(string type)
        {
            if (type == null)          return ContactAddressKind.Other;
            if (type.Contains("home")) return ContactAddressKind.Home;
            if (type.Contains("work")) return ContactAddressKind.Work;
            return ContactAddressKind.Other;
        }

        private string ParseWebsiteDescription(string type)
        {
            if (type == null)          return "Other";
            if (type.Contains("home")) return "Home";
            if (type.Contains("work")) return "Work";
            if (type.Contains("blog")) return "Blog";
            if (type.Contains("ftp"))  return "FTP";
            return "Other";
        }

        // Save phone/email labels for a contact so they survive
        // People app edits and can be restored on Phone→Google upload
        private void SaveLabels(VCardContact vc)
        {
            if (string.IsNullOrEmpty(vc.Uid)) return;

            var labels = new System.Collections.Generic.Dictionary<string, string>();

            for (int i = 0; i < vc.Phones.Count; i++)
            {
                string type = PhoneTypeToDescription(vc.Phones[i].Type);
                // Store the vCard TYPE value directly for round-trip fidelity
                labels["phone_" + i] = PhoneTypeToVCard(vc.Phones[i].Type);
            }

            for (int i = 0; i < vc.Emails.Count; i++)
                labels["email_" + i] = EmailTypeToVCard(vc.Emails[i].Type);

            LabelStorage.SaveLabels(vc.Uid, labels);
        }

        private string PhoneTypeToVCard(string type)
        {
            if (string.IsNullOrEmpty(type)) return "CELL,VOICE";
            string t = type.ToLowerInvariant();
            if (t == "home")   return "HOME,VOICE";
            if (t == "work")   return "WORK,VOICE";
            if (t == "mobile") return "CELL,VOICE";
            if (t == "pager")  return "PAGER";
            if (t == "fax")    return "FAX";
            return "CELL,VOICE";
        }

        private string EmailTypeToVCard(string type)
        {
            if (string.IsNullOrEmpty(type)) return "INTERNET,HOME";
            string t = type.ToLowerInvariant();
            if (t == "home")   return "INTERNET,HOME";
            if (t == "work")   return "INTERNET,WORK";
            return "INTERNET";
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
    }
}
