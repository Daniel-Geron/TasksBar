using Google.Apis.Auth.OAuth2;
using Google.Apis.Tasks.v1;
using Google.Apis.Util.Store;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TasksBar
{
    public static class GoogleAuthManager
    {
        static string[] Scopes = { TasksService.Scope.Tasks };
        static string ApplicationName = "TasksBar";
        private static UserCredential _credential;

        public static async Task<UserCredential> LoginAsync()
        {
            // The app's API credentials, and the saved sign-in, live in TasksBar's data folder
            // (next to TasksBar.exe on Windows, ~/.config/TasksBar on Linux)
            string credsPath = AppPaths.CredentialsFile;
            string tokenPath = AppPaths.File("token.json");

            using (var stream = new FileStream(credsPath, FileMode.Open, FileAccess.Read))
            {
                _credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                    GoogleClientSecrets.FromStream(stream).Secrets,
                    Scopes,
                    "user",
                    CancellationToken.None,
                    new FileDataStore(tokenPath, true));
            }
            return _credential;
        }

        // Google answers "invalid_grant" when the saved refresh token has expired or been revoked.
        // Retrying can never succeed; the only fix is signing in again.
        public static bool IsSignInExpired(Exception ex)
        {
            for (var e = ex; e != null; e = e.InnerException)
            {
                if (e is Google.Apis.Auth.OAuth2.Responses.TokenResponseException tre && tre.Error?.Error == "invalid_grant")
                    return true;
            }
            return false;
        }

        // Forgets the dead token so the next LoginAsync opens the browser for a fresh sign-in
        public static async Task ClearSavedLoginAsync()
        {
            string tokenPath = AppPaths.File("token.json");
            await new FileDataStore(tokenPath, true).ClearAsync();
            _credential = null;
        }

        public static TasksService GetTasksService()
        {
            if (_credential == null) return null;

            return new TasksService(new Google.Apis.Services.BaseClientService.Initializer()
            {
                HttpClientInitializer = _credential,
                ApplicationName = ApplicationName,
            });
        }
    }
}