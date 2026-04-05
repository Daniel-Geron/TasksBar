using Google.Apis.Auth.OAuth2;
using Google.Apis.Tasks.v1;
using Google.Apis.Util.Store;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GTasksBar
{
    public static class GoogleAuthManager
    {
        static string[] Scopes = { TasksService.Scope.Tasks };
        static string ApplicationName = "GTasksBar";
        private static UserCredential _credential;

        public static async Task<UserCredential> LoginAsync()
        {
            // THE FIX: Using your exact credentials.json file name
            using (var stream = new FileStream("credentials.json", FileMode.Open, FileAccess.Read))
            {
                string credPath = "token.json";
                _credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                    GoogleClientSecrets.FromStream(stream).Secrets,
                    Scopes,
                    "user",
                    CancellationToken.None,
                    new FileDataStore(credPath, true));
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