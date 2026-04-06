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
            // 1. Get the exact folder where TasksBar.exe lives
            string baseDir = System.AppContext.BaseDirectory;

            // 2. Combine the folder path with the file names
            string credsPath = System.IO.Path.Combine(baseDir, "credentials.json");
            string tokenPath = System.IO.Path.Combine(baseDir, "token.json");

            // 3. Use the absolute paths!
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