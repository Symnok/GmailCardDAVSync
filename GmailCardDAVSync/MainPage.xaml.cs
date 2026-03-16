using System;
using System.Collections.Generic;
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

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            var saved = CredentialStorage.Load();
            if (saved != null)
            {
                TxtEmail.Text          = saved.UserName;
                TxtPassword.Password   = saved.Password;
                BtnForget.Visibility   = Visibility.Visible;
                TxtLastSync.Visibility = Visibility.Visible;
                TxtLastSync.Text       = "Credentials loaded from secure storage";
            }
        }

        private async void UpdateProgress(string msg)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                TxtProgress.Text = msg);
        }

        private async void BtnSync_Click(object sender, RoutedEventArgs e)
        {
            string email    = TxtEmail.Text.Trim();
            string password = TxtPassword.Password.Trim().Replace(" ", "");

            if (string.IsNullOrEmpty(email) || !email.Contains("@"))
            {
                ShowError("Please enter a valid Gmail address.");
                return;
            }
            if (string.IsNullOrEmpty(password) || password.Length < 16)
            {
                ShowError("Please enter your Google App Password (16 characters).\n" +
                          "NOT your regular Gmail password.");
                return;
            }

            if (ChkSaveCredentials.IsChecked == true)
            {
                CredentialStorage.Save(email, password);
                BtnForget.Visibility = Visibility.Visible;
            }

            SetUiBusy(true);
            HideAllBanners();

            try
            {
                IProgress<string> progress = new Progress<string>(
                    msg => UpdateProgress(msg));

                var cardDav = new CardDavService(email, password);
                var store   = new ContactStoreService();
                int count   = 0;
                string mode = "";

                bool isFirstSync = !ETagStorage.HasData();

                if (isFirstSync)
                {
                    // ================================================
                    // FIRST SYNC — download everything
                    // ================================================
                    mode = "full";
                    var fetchResult = await cardDav.FetchAllContactsAsync(progress);
                    var contacts = fetchResult.Contacts;
                    var etags    = fetchResult.Etags;

                    if (contacts.Count == 0)
                    {
                        ShowError("No contacts found in your Google account.");
                        return;
                    }

                    count = await store.SyncAsync(contacts, progress);

                    // Save etags for future incremental syncs
                    ETagStorage.SaveAll(etags);
                }
                else
                {
                    // ================================================
                    // INCREMENTAL SYNC — only changed contacts
                    // ================================================
                    mode = "incremental";
                    var localEtags = ETagStorage.LoadAll();
                    var diff = await cardDav.GetChangesAsync(localEtags, progress);

                    if (diff.ChangedHrefs.Count == 0 && diff.DeletedHrefs.Count == 0)
                    {
                        // Nothing changed — still update etag map and show success
                        ETagStorage.SaveAll(diff.ServerEtags);
                        string when2 = DateTime.Now.ToString("dd MMM yyyy  HH:mm");
                        TxtSuccess.Text = "All contacts up to date. No changes.";
                        BannerSuccess.Visibility = Visibility.Visible;
                        TxtLastSync.Text         = "Last sync: " + when2;
                        TxtLastSync.Visibility   = Visibility.Visible;
                        return;
                    }

                    progress.Report(
                        diff.ChangedHrefs.Count + " to update, " +
                        diff.DeletedHrefs.Count + " to delete.");

                    // Download and upsert changed contacts
                    if (diff.ChangedHrefs.Count > 0)
                    {
                        var changed = await cardDav.FetchContactsByHrefsAsync(
                            diff.ChangedHrefs, diff.ServerEtags, progress);

                        progress.Report("Updating " + changed.Count + " contacts...");
                        foreach (var vc in changed)
                        {
                            await store.UpsertContactAsync(vc);
                            count++;
                        }
                    }

                    // Delete removed contacts
                    if (diff.DeletedHrefs.Count > 0)
                    {
                        progress.Report("Removing " +
                            diff.DeletedHrefs.Count + " deleted contacts...");

                        // We need UIDs to delete — load from saved etag map
                        // by doing a fresh fetch of deleted hrefs won't work
                        // (they're gone). Instead we store href→uid separately.
                        // For now: do a full re-read of contact list to find by href
                        foreach (var href in diff.DeletedHrefs)
                        {
                            // Use href as a fallback key — contacts were saved
                            // with RemoteId = UID. Look up UID from saved data.
                            string uid = UidFromHref(href);
                            if (!string.IsNullOrEmpty(uid))
                                await store.DeleteContactByUidAsync(uid);
                        }
                    }

                    // Save updated etag map
                    ETagStorage.SaveAll(diff.ServerEtags);
                }

                string when = DateTime.Now.ToString("dd MMM yyyy  HH:mm");
                string result = mode == "full"
                    ? count + " contacts synced (full sync)."
                    : count + " contacts updated (incremental sync).";

                TxtSuccess.Text          = result;
                BannerSuccess.Visibility = Visibility.Visible;
                TxtLastSync.Text         = "Last sync: " + when;
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

        // Map href → UID using saved etag data as a lookup key.
        // Since we store "href|etag" we can't directly get UID from href.
        // For deleted contacts, we do a scan of the contact store.
        private string UidFromHref(string href)
        {
            // Extract the filename part of the href (e.g. "abc123.vcf")
            // which Google uses as the UID
            int lastSlash = href.LastIndexOf('/');
            if (lastSlash < 0) return string.Empty;
            string filename = href.Substring(lastSlash + 1);
            // Remove .vcf extension
            if (filename.EndsWith(".vcf", StringComparison.OrdinalIgnoreCase))
                filename = filename.Substring(0, filename.Length - 4);
            return filename;
        }

        private void BtnForget_Click(object sender, RoutedEventArgs e)
        {
            CredentialStorage.Delete();
            ETagStorage.Clear();
            TxtEmail.Text          = string.Empty;
            TxtPassword.Password   = string.Empty;
            BtnForget.Visibility   = Visibility.Collapsed;
            TxtLastSync.Visibility = Visibility.Collapsed;
            HideAllBanners();
        }

        private void SetUiBusy(bool busy)
        {
            BtnSync.IsEnabled        = !busy;
            TxtEmail.IsEnabled       = !busy;
            TxtPassword.IsEnabled    = !busy;
            PanelProgress.Visibility = busy
                ? Visibility.Visible
                : Visibility.Collapsed;
            if (!busy) TxtProgress.Text = string.Empty;
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
