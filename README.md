# GmailCardDAVSync

**GmailCardDAVSync** is an application for synchronizing **Google Contacts with Windows 10 Mobile (W10M) smartphones** using the **CardDAV protocol**.

The need for this application arose because **PH Cloud CardDAV/CalDAV Sync [WM10]** stopped working with Google.

---

## Authentication

To use the program, you must generate a **Google App Password**:

https://myaccount.google.com/apppasswords

This application **does not require creating a Secret Key or Client ID in the Google Console**.

---

## Protocol Used

The program uses **CardDAV** instead of the **Google People API**.

Hopefully CardDAV support will remain available for some time.
At the moment, **Google has not announced any plans to discontinue CardDAV access**.

---

## Planned Features

Future versions may include synchronization support for:

* **Yandex Contacts**
* **Yahoo Contacts**
* **Apple iCloud Contacts**

---

## Supported Windows 10 Mobile Builds

The application supports the following **Windows 10 Mobile builds**:

* **1607**
* **1703**
* **1709**

Due to the **lack of background execution support**, synchronization must currently be **performed manually**.

---

## Future WP8.1 Support

Support for **Windows Phone 8.1 (WP8.1)** is planned, but it will be implemented as a **separate application**.

---

## Dependencies

The application requires the following packages:

* `Microsoft.NET.Native.Framework.1.7`
* `Microsoft.NET.Native.Runtime.1.7`
* `Microsoft.VCLibs.140.00`

---

## Supported Contact Fields

All major **Google contact fields** are supported, including:

* Contact **avatar / photo**
* **Multiple phone numbers**
* **Multiple email addresses**

### Known Limitation

Currently, only one field is known **not to synchronize**:

* `website`

---

## Notes

* Synchronization works **directly via CardDAV**.
* No Google Cloud project configuration is required.
* Only a **Google App Password** is needed.

## Screenshot
<p align="center">
  <img src="(docs/images/screen.png" width="320">
</p>

---
