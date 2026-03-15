// Models/VCardContact.cs
// Represents a single contact parsed from a vCard 3.0 / 4.0 payload

using System.Collections.Generic;

namespace GmailCardDAVSync.Models
{
    public class VCardContact
    {
        // --- Name fields ---
        public string DisplayName    { get; set; } = string.Empty;  // FN:
        public string FirstName      { get; set; } = string.Empty;  // N: part[1]
        public string LastName       { get; set; } = string.Empty;  // N: part[0]
        public string MiddleName     { get; set; } = string.Empty;  // N: part[2]
        public string NamePrefix     { get; set; } = string.Empty;  // N: part[3]
        public string NameSuffix     { get; set; } = string.Empty;  // N: part[4]

        // --- Contact info ---
        public List<ContactEmail>  Emails  { get; set; } = new List<ContactEmail>();
        public List<ContactPhone>  Phones  { get; set; } = new List<ContactPhone>();
        public List<ContactAddress> Addresses { get; set; } = new List<ContactAddress>();

        // --- Work info ---
        public string Organization   { get; set; } = string.Empty;  // ORG:
        public string JobTitle       { get; set; } = string.Empty;  // TITLE:

        // --- Other ---
        public string Notes          { get; set; } = string.Empty;  // NOTE:
        public string Nickname       { get; set; } = string.Empty;  // NICKNAME:
        public string Birthday       { get; set; } = string.Empty;  // BDAY:
        public string PhotoUrl       { get; set; } = string.Empty;  // PHOTO;VALUE=URI:
        public string Uid            { get; set; } = string.Empty;  // UID:
        public string Etag           { get; set; } = string.Empty;  // from CardDAV response
    }

    public class ContactEmail
    {
        public string Address { get; set; } = string.Empty;
        public string Type    { get; set; } = "other";   // home, work, other
    }

    public class ContactPhone
    {
        public string Number  { get; set; } = string.Empty;
        public string Type    { get; set; } = "other";   // home, work, cell, other
    }

    public class ContactAddress
    {
        public string Street     { get; set; } = string.Empty;
        public string City       { get; set; } = string.Empty;
        public string Region     { get; set; } = string.Empty;
        public string PostalCode { get; set; } = string.Empty;
        public string Country    { get; set; } = string.Empty;
        public string Type       { get; set; } = "other";
    }
}
