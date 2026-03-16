// Models/VCardContact.cs
using System.Collections.Generic;

namespace GmailCardDAVSync.Models
{
    public class VCardContact
    {
        public string DisplayName    { get; set; } = string.Empty;
        public string FirstName      { get; set; } = string.Empty;
        public string LastName       { get; set; } = string.Empty;
        public string MiddleName     { get; set; } = string.Empty;
        public string NamePrefix     { get; set; } = string.Empty;
        public string NameSuffix     { get; set; } = string.Empty;

        public List<ContactEmail>   Emails    { get; set; } = new List<ContactEmail>();
        public List<ContactPhone>   Phones    { get; set; } = new List<ContactPhone>();
        public List<ContactAddress> Addresses { get; set; } = new List<ContactAddress>();
        public List<ContactWebsite> Websites  { get; set; } = new List<ContactWebsite>();

        public string Organization   { get; set; } = string.Empty;
        public string JobTitle       { get; set; } = string.Empty;
        public string Notes          { get; set; } = string.Empty;
        public string Nickname       { get; set; } = string.Empty;
        public string Birthday       { get; set; } = string.Empty;
        public string PhotoUrl       { get; set; } = string.Empty;
        public string Uid            { get; set; } = string.Empty;
        public string Etag           { get; set; } = string.Empty;
        public string Href           { get; set; } = string.Empty; // CardDAV path
    }

    public class ContactEmail
    {
        public string Address { get; set; } = string.Empty;
        public string Type    { get; set; } = "other";
    }

    public class ContactPhone
    {
        public string Number  { get; set; } = string.Empty;
        public string Type    { get; set; } = "other";
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

    public class ContactWebsite
    {
        public string Url  { get; set; } = string.Empty;
        public string Type { get; set; } = "other";
    }
}
