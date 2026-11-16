using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace TasksBar.Services.Sync
{
    /// <summary>
    /// Which sync service is in use and the connection to it. Only one is active at a time:
    /// Google Tasks, a CalDAV server (Tasks.org Cloud by default), or none (tasks stay on this PC).
    /// </summary>
    public static class TaskSync
    {
        public const string Local = "Local";
        public const string Google = "Google";
        public const string CalDav = "CalDav";

        public const string TasksOrgServerUrl = "https://caldav.tasks.org/";

        /// <summary>The connected service, or null when signed out, offline at startup, or sync is off.</summary>
        public static ITaskBackend? Current { get; private set; }

        public static string Provider => AppConfig.Settings.SyncProvider;

        public static bool IsCalDavConfigured =>
            !string.IsNullOrEmpty(AppConfig.Settings.CalDavHomeUrl) &&
            !string.IsNullOrEmpty(AppConfig.Settings.CalDavUsername) &&
            !string.IsNullOrEmpty(AppConfig.Settings.CalDavPassword);

        /// <summary>True when tasks come from a sync service instead of the local JSON files.</summary>
        public static bool IsEnabled =>
            Provider == Google || (Provider == CalDav && IsCalDavConfigured);

        /// <summary>Name shown in messages, e.g. "Google Tasks", "Tasks.org" or "nextcloud.example.com".</summary>
        public static string ProviderName => Provider switch
        {
            Google => "Google Tasks",
            CalDav => CalDavName,
            _ => "this PC"
        };

        /// <summary>"Tasks.org", or the host name of another CalDAV server.</summary>
        public static string CalDavName =>
            AppConfig.Settings.CalDavIsTasksOrg ? "Tasks.org" : HostOf(AppConfig.Settings.CalDavServerUrl);

        /// <summary>
        /// Identifies the account notes are synced with. When it changes, the notes' remote ids
        /// belong to another account and are reset (see StickyNoteSync).
        /// </summary>
        public static string AccountKey => Provider switch
        {
            Google => "google",
            CalDav => $"caldav:{AppConfig.Settings.CalDavUsername}@{AppConfig.Settings.CalDavHomeUrl}",
            _ => ""
        };

        /// <summary>
        /// Connects to the chosen service. For Google this may open the browser to sign in;
        /// CalDAV uses the saved server and app password. Throws if it can't connect.
        /// </summary>
        public static async Task<ITaskBackend?> ConnectAsync()
        {
            Current = null;
            if (Provider == Google)
            {
                await GoogleAuthManager.LoginAsync();
                var service = GoogleAuthManager.GetTasksService();
                if (service != null) Current = new GoogleTaskBackend(service);
            }
            else if (Provider == CalDav && IsCalDavConfigured)
            {
                Current = CalDavTaskBackend.Create(
                    AppConfig.Settings.CalDavHomeUrl,
                    AppConfig.Settings.CalDavUsername,
                    Unprotect(AppConfig.Settings.CalDavPassword),
                    ProviderName);
            }
            return Current;
        }

        /// <summary>Uses an existing Google sign-in without opening the browser (e.g. right after logging in from Settings).</summary>
        public static ITaskBackend? ConnectWithoutPrompt()
        {
            if (Provider == Google)
            {
                var service = GoogleAuthManager.GetTasksService();
                Current = service == null ? null : new GoogleTaskBackend(service);
                return Current;
            }
            return null;
        }

        public static void Disconnect() => Current = null;

        /// <summary>Forgets the saved sign-in after the server rejected it, so the user is asked again.</summary>
        public static async Task ForgetSignInAsync()
        {
            Current = null;
            if (Provider == Google)
            {
                await GoogleAuthManager.ClearSavedLoginAsync();
            }
            else if (Provider == CalDav)
            {
                AppConfig.Settings.CalDavPassword = "";
                AppConfig.Save();
            }
        }

        /// <summary>
        /// Checks the server address and app password, finds the task lists, and saves the account
        /// if it works. Returns how many task lists were found.
        /// </summary>
        public static async Task<int> SetUpCalDavAsync(string serverUrl, string username, string password, bool isTasksOrg)
        {
            var home = await CalDavTaskBackend.DiscoverHomeAsync(serverUrl, username, password);
            var backend = CalDavTaskBackend.Create(home.AbsoluteUri, username, password, isTasksOrg ? "Tasks.org" : HostOf(serverUrl));
            var lists = await backend.GetListsAsync();

            bool accountChanged = AppConfig.Settings.CalDavHomeUrl != home.AbsoluteUri || AppConfig.Settings.CalDavUsername != username;

            AppConfig.Settings.CalDavServerUrl = serverUrl;
            AppConfig.Settings.CalDavIsTasksOrg = isTasksOrg;
            AppConfig.Settings.CalDavUsername = username;
            AppConfig.Settings.CalDavPassword = Protect(password);
            AppConfig.Settings.CalDavHomeUrl = home.AbsoluteUri;
            if (accountChanged && Provider == CalDav) AppConfig.Settings.SelectedListId = "@default";
            AppConfig.Save();

            if (Provider == CalDav) Current = backend;
            return lists.FindAll(l => !StickyNoteSync.IsNotesList(l.Id, l.Title)).Count;
        }

        public static void ForgetCalDavAccount()
        {
            AppConfig.Settings.CalDavUsername = "";
            AppConfig.Settings.CalDavPassword = "";
            AppConfig.Settings.CalDavHomeUrl = "";
            if (Provider == CalDav) Current = null;
            AppConfig.Save();
        }

        /// <summary>True when the error means the saved sign-in no longer works.</summary>
        public static bool IsAuthError(Exception ex)
        {
            for (var e = ex; e != null; e = e.InnerException)
                if (e is RemoteAuthException) return true;
            return GoogleAuthManager.IsSignInExpired(ex);
        }

        public static bool IsNotFound(Exception ex) => ex is RemoteNotFoundException;

        // The app password is encrypted for the current Windows user, so config.json alone doesn't reveal it
        private static string Protect(string value) =>
            Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));

        private static string Unprotect(string value)
        {
            try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.CurrentUser)); }
            catch { return ""; } // From another PC or user: they'll need to enter it again
        }

        private static string HostOf(string url) =>
            Uri.TryCreate(url.Contains("://") ? url : "https://" + url, UriKind.Absolute, out var uri) ? uri.Host : "CalDAV";
    }
}
