// Helpers/CredentialStorage.cs
// Securely stores Gmail address + App Password in the Windows Credential Locker
// (PasswordVault) — the W10M equivalent of Keychain / Android Keystore.
// Credentials are encrypted by the OS and tied to the device.

using Windows.Security.Credentials;

namespace GmailCardDAVSync.Helpers
{
    public static class CredentialStorage
    {
        private const string ResourceName = "GmailCardDAVSync";

        // ---------------------------------------------------------------
        // Save credentials  (overwrites any previously saved ones)
        // ---------------------------------------------------------------
        public static void Save(string gmailAddress, string appPassword)
        {
            // Remove old entry first to avoid duplicates
            Delete();

            var vault      = new PasswordVault();
            var credential = new PasswordCredential(
                ResourceName, gmailAddress, appPassword);
            vault.Add(credential);
        }

        // ---------------------------------------------------------------
        // Load credentials — returns null if nothing saved yet
        // ---------------------------------------------------------------
        public static PasswordCredential Load()
        {
            try
            {
                var vault = new PasswordVault();
                var creds = vault.FindAllByResource(ResourceName);
                if (creds == null || creds.Count == 0) return null;

                // Retrieve the first saved credential and populate the Password field
                var cred = creds[0];
                cred.RetrievePassword();   // must call this before accessing .Password
                return cred;
            }
            catch
            {
                return null;   // No credentials saved yet
            }
        }

        // ---------------------------------------------------------------
        // Check if credentials exist without loading them
        // ---------------------------------------------------------------
        public static bool HasCredentials()
        {
            try
            {
                var vault = new PasswordVault();
                var creds = vault.FindAllByResource(ResourceName);
                return creds != null && creds.Count > 0;
            }
            catch { return false; }
        }

        // ---------------------------------------------------------------
        // Delete saved credentials (used when user logs out / changes account)
        // ---------------------------------------------------------------
        public static void Delete()
        {
            try
            {
                var vault = new PasswordVault();
                var creds = vault.FindAllByResource(ResourceName);
                if (creds == null) return;
                foreach (var c in creds)
                    vault.Remove(c);
            }
            catch { /* nothing saved, ignore */ }
        }
    }
}
