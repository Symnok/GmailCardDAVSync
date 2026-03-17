// Helpers/BackgroundTaskHelper.cs
// Registers and manages the GoogleToPhoneSyncTask background task.

using System.Threading.Tasks;
using System;
using System.Threading.Tasks;
using Windows.ApplicationModel.Background;
using Windows.Foundation;

namespace GmailCardDAVSync.Helpers
{
    public static class BackgroundTaskHelper
    {
        private const string TaskName       = "GoogleToPhoneSyncTask";
        private const string TaskEntryPoint = "SyncComponent.GoogleToPhoneSyncTask";

        public static async Task<bool> RegisterAsync()
        {
            // Already registered — skip
            foreach (var t in BackgroundTaskRegistration.AllTasks)
                if (t.Value.Name == TaskName) return true;

            // Request background execution permission
            var status = await BackgroundExecutionManager.RequestAccessAsync().AsTask();
            if (status == BackgroundAccessStatus.DeniedByUser ||
                status == BackgroundAccessStatus.DeniedBySystemPolicy ||
                status == BackgroundAccessStatus.Unspecified)
                return false;

            var builder = new BackgroundTaskBuilder
            {
                Name           = TaskName,
                TaskEntryPoint = TaskEntryPoint,
                IsNetworkRequested = true
            };

            // Fire every 15 minutes
            builder.SetTrigger(new TimeTrigger(15, false));

            // Only when internet is available
            builder.AddCondition(
                new SystemCondition(SystemConditionType.InternetAvailable));

            builder.Register();
            return true;
        }

        public static void Unregister()
        {
            foreach (var t in BackgroundTaskRegistration.AllTasks)
            {
                if (t.Value.Name == TaskName)
                {
                    t.Value.Unregister(true);
                    return;
                }
            }
        }

        public static bool IsRegistered()
        {
            foreach (var t in BackgroundTaskRegistration.AllTasks)
                if (t.Value.Name == TaskName) return true;
            return false;
        }
    }
}
