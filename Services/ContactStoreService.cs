// Services/ContactStoreService.cs
// Writes VCardContact objects into the Windows 10 Mobile ContactStore (People app).
// Does NOT require Windows Mobile Extensions SDK.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.ApplicationModel.Contacts;

using VCardEmail   = GmailCardDAVSync.Models.ContactEmail;
using VCardPhone   = GmailCardDAVSync.Models.ContactPhone;
using VCardAddr    = GmailCardDAVSync.Models.ContactAddress;
using VCardContact = GmailCardDAVSync.Models.VCardContact;

namespace GmailCardDAVSync.Services
{
    public class ContactStoreService
    {
        private const string ListDisplayName = "Gmail (CardDAV)";

        public async Task<int> SyncAsync(
            List<VCardContact> contacts,
            IProgress<string> progress = null)
        {
            var store = await ContactManager.RequestStoreAsync(
                ContactStoreAccessType.AppContactsReadWrite);

            if (store == null)
                throw new Exception(
                    "Could not open ContactStore.\n" +
                    "Please grant Contacts permission in Settings.");

            progress?.Report("Preparing contact list...");
            var list = await GetOrCreateListAsync(store);

            progress?.Report("Clearing old synced contacts...");
            await ClearListAsync(list);

            progress?.Report("Writing " + contacts.Count + " contacts...");
            int saved = 0;
            foreach (var vc in contacts)
            {
                try
                {
                    await list.SaveContactAsync(ToUwpContact(vc));
                    saved++;
                }
                catch { }
            }
            return saved;
        }

        private async Task<ContactList> GetOrCreateListAsync(ContactStore store)
        {
            var lists = await store.FindContactListsAsync();
            foreach (var l in lists)
                if (l.DisplayName == ListDisplayName)
                    return l;

            var newList = await store.CreateContactListAsync(ListDisplayName);
            newList.OtherAppReadAccess  = ContactListOtherAppReadAccess.Full;
            newList.OtherAppWriteAccess = ContactListOtherAppWriteAccess.None;
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
                MiddleName          = vc.MiddleName ?? string.Empty,
                HonorificNamePrefix = vc.NamePrefix ?? string.Empty,
                HonorificNameSuffix = vc.NameSuffix ?? string.Empty,
                Nickname            = vc.Nickname   ?? string.Empty,
                Notes               = vc.Notes      ?? string.Empty,
                RemoteId            = vc.Uid        ?? string.Empty
            };

            // Emails
            foreach (VCardEmail e in vc.Emails)
            {
                c.Emails.Add(new Windows.ApplicationModel.Contacts.ContactEmail
                {
                    Address = e.Address ?? string.Empty,
                    Kind    = ParseEmailKind(e.Type)
                });
            }

            // Phones — Kind deliberately omitted to avoid
            // ContactPhoneNumberKind which needs Mobile Extensions.
            // Contacts still sync correctly; type defaults to Mobile.
            foreach (VCardPhone p in vc.Phones)
            {
                c.Phones.Add(new Windows.ApplicationModel.Contacts.ContactPhone
                {
                    Number = p.Number ?? string.Empty
                });
            }

            // Addresses
            foreach (VCardAddr a in vc.Addresses)
            {
                c.Addresses.Add(new Windows.ApplicationModel.Contacts.ContactAddress
                {
                    StreetAddress = a.Street     ?? string.Empty,
                    Locality      = a.City       ?? string.Empty,
                    Region        = a.Region     ?? string.Empty,
                    PostalCode    = a.PostalCode ?? string.Empty,
                    Country       = a.Country   ?? string.Empty,
                    Kind          = ParseAddressKind(a.Type)
                });
            }

            // Job info
            if (!string.IsNullOrEmpty(vc.Organization) ||
                !string.IsNullOrEmpty(vc.JobTitle))
            {
                c.JobInfo.Add(new ContactJobInfo
                {
                    CompanyName = vc.Organization ?? string.Empty,
                    Title       = vc.JobTitle     ?? string.Empty
                });
            }

            // Birthday
            if (!string.IsNullOrEmpty(vc.Birthday))
            {
                DateTimeOffset bday;
                if (TryParseBirthday(vc.Birthday, out bday))
                {
                    c.ImportantDates.Add(new ContactDate
                    {
                        Kind  = ContactDateKind.Birthday,
                        Day   = (uint)bday.Day,
                        Month = (uint)bday.Month,
                        Year  = bday.Year
                    });
                }
            }

            // Fallback display name
            if (string.IsNullOrEmpty(c.FirstName) &&
                string.IsNullOrEmpty(c.LastName) &&
                !string.IsNullOrEmpty(vc.DisplayName))
            {
                c.Nickname = vc.DisplayName;
            }

            return c;
        }

        private ContactEmailKind ParseEmailKind(string type)
        {
            if (type == null)          return ContactEmailKind.Other;
            if (type.Contains("home")) return ContactEmailKind.Personal;
            if (type.Contains("work")) return ContactEmailKind.Work;
            return ContactEmailKind.Other;
        }

        private ContactAddressKind ParseAddressKind(string type)
        {
            if (type == null)          return ContactAddressKind.Other;
            if (type.Contains("home")) return ContactAddressKind.Home;
            if (type.Contains("work")) return ContactAddressKind.Work;
            return ContactAddressKind.Other;
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
