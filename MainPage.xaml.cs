// Views/MainPage.xaml.cs
// Code-behind for the main (and only) page of GmailCardDAVSync.
// Orchestrates: credential load/save → CardDAV fetch → vCard parse → ContactStore write.

using System;
using System.Threading.Tasks;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using GmailCardDAVSync.Helpers;
using GmailCardDAVSync.Services;

namespace GmailCardDAVSync
{
    public sealed partial class MainPage : Page
    {
        public MainPage()
        {
            this.InitializeComponent();
        }

        // ---------------------------------------------------------------
        // Page load — restore saved credentials if available
        // ---------------------------------------------------------------
        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            var saved = CredentialStorage.Load();
            if (saved != null)
            {
                TxtEmail.Text     = saved.UserName;
                TxtPassword.Password = saved.Password;
                BtnForget.Visibility  = Visibility.Visible;
                TxtLastSync.Visibility = Visibility.Visible;
                TxtLastSync.Text = "Credentials loaded from secure storage";
            }
        }

        // ---------------------------------------------------------------
        // SYNC button
        // ---------------------------------------------------------------
        private async void BtnSync_Click(object sender, RoutedEventArgs e)
        {
            string email    = TxtEmail.Text.Trim();
            string password = TxtPassword.Password.Trim()
                                         .Replace(" ", ""); // strip spaces from App Password

            // ---- Validate inputs ----
            if (string.IsNullOrEmpty(email) || !email.Contains("@"))
            {
                ShowError("Please enter a valid Gmail address.");
                return;
            }
            if (string.IsNullOrEmpty(password) || password.Length < 16)
            {
                ShowError(
                    "Please enter your Google App Password.\n" +
                    "It is 16 characters and is NOT your regular Gmail password.");
                return;
            }

            // ---- Save credentials if requested ----
            if (ChkSaveCredentials.IsChecked == true)
            {
                CredentialStorage.Save(email, password);
                BtnForget.Visibility = Visibility.Visible;
            }

            // ---- Begin sync ----
            SetUiBusy(true);
            HideAllBanners();

            try
            {
                var progress = new Progress<string>(msg =>
                {
                    // Progress.Report always arrives on UI thread in UWP
                    TxtProgress.Text = msg;
                });

                // Step 1 — Fetch from Google CardDAV
                var cardDav   = new CardDavService(email, password);
                var contacts  = await cardDav.FetchAllContactsAsync(progress);

                if (contacts.Count == 0)
                {
                    ShowError("No contacts found in your Google account.");
                    return;
                }

                // Step 2 — Write to W10M ContactStore
                var store = new ContactStoreService();
                int saved = await store.SyncAsync(contacts, progress);

                // ---- Success ----
                string when = DateTime.Now.ToString("dd MMM yyyy  HH:mm");
                TxtSuccess.Text = $"✓  {saved} contacts synced successfully.";
                BannerSuccess.Visibility = Visibility.Visible;
                TxtLastSync.Text         = $"Last sync: {when}";
                TxtLastSync.Visibility   = Visibility.Visible;
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }
            finally
            {
                SetUiBusy(false);
            }
        }

        // ---------------------------------------------------------------
        // FORGET button — wipe saved credentials
        // ---------------------------------------------------------------
        private void BtnForget_Click(object sender, RoutedEventArgs e)
        {
            CredentialStorage.Delete();
            TxtEmail.Text            = string.Empty;
            TxtPassword.Password     = string.Empty;
            BtnForget.Visibility     = Visibility.Collapsed;
            TxtLastSync.Visibility   = Visibility.Collapsed;
            HideAllBanners();
        }

        // ---------------------------------------------------------------
        // UI helpers
        // ---------------------------------------------------------------
        private void SetUiBusy(bool busy)
        {
            BtnSync.IsEnabled        = !busy;
            TxtEmail.IsEnabled       = !busy;
            TxtPassword.IsEnabled    = !busy;
            PanelProgress.Visibility = busy
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void ShowError(string message)
        {
            TxtError.Text            = message;
            BannerError.Visibility   = Visibility.Visible;
            BannerSuccess.Visibility = Visibility.Collapsed;
        }

        private void HideAllBanners()
        {
            BannerError.Visibility   = Visibility.Collapsed;
            BannerSuccess.Visibility = Visibility.Collapsed;
        }
    }
}
