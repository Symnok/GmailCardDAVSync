# GmailCardDAVSync

**GmailCardDAVSync** is an application for synchronizing **Google Contacts** with a **Windows 10 Mobile (W10M) smartphone** using the **CardDAV protocol**.

The need for this application arose because **PH Cloud CardDAV/CalDAV Sync** stopped working with Google.

## Setup

To use the program, you need to generate an **App Password** at:  
https://myaccount.google.com/apppasswords

## Current Functionality

The **first version** of the application synchronizes contacts **in one direction only**:

**Google Contacts → Lumia**

This limitation is intentional to avoid issues where contacts might disappear.  
Two-way synchronization may be added later if users request this functionality.

## Technology

The application uses **CardDAV** instead of the **People API**.

The developer hopes CardDAV will continue working for some time. As of now, **Google has not issued any warnings about discontinuing CardDAV access**.

## Planned Features

Planned additions include synchronization of contacts with:

- **Yandex**
- **Yahoo**
- **Apple iCloud**
- **GMX**
- **mail.com**

## Supported Systems

Supported **Windows 10 Mobile builds**:

- **1703**
- **1709**

There are also plans to add **Windows Phone 8.1** support, but this will be released as a separate application.

## Installation

The archive includes:

- **APPX installer**
- **.CER certificate**

## Dependencies

Required dependencies are currently unknown.

The application worked without installing additional dependencies during testing, but they may already have been present on the test device.

## Supported Contact Fields

The application supports all major Google contact fields, including:

- Contact avatar / photo
- Multiple phone numbers
- Multiple email addresses

Currently, only one field is not synchronized:

- `website`
