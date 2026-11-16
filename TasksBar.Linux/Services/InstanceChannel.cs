using System;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace TasksBar.Linux.Services
{
    /// <summary>
    /// Keeps TasksBar to one running copy, and lets commands reach it: starting <c>tasksbar</c> again
    /// shows the flyout, and <c>tasksbar --new-note</c> / <c>--open-notes</c> / <c>--toggle</c> act on the
    /// running app. That's also how keyboard shortcuts set up in the desktop's settings work.
    /// Commands go over a Unix socket in $XDG_RUNTIME_DIR (private to the user).
    /// </summary>
    public static class InstanceChannel
    {
        public const string NewNote = "--new-note";
        public const string OpenNotes = "--open-notes";
        public const string Toggle = "--toggle";
        public const string Show = "--show";

        public static readonly string[] Commands = { NewNote, OpenNotes, Toggle, Show };

        private static Socket? _listener;

        /// <summary>Raised on the UI thread when another <c>tasksbar</c> sends a command.</summary>
        public static event Action<string>? CommandReceived;

        private static string SocketPath
        {
            get
            {
                string folder = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } runtime && Directory.Exists(runtime)
                    ? runtime
                    : Path.GetTempPath();
                // One app per data folder (a test copy with its own TASKSBAR_DATA_DIR runs separately)
                string id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(AppPaths.DataDirectory)))[..12].ToLowerInvariant();
                return Path.Combine(folder, $"tasksbar-{id}.sock");
            }
        }

        /// <summary>Sends the command to an already running TasksBar. False when none is running.</summary>
        public static bool TrySend(string command)
        {
            string path = SocketPath;
            if (!File.Exists(path)) return false;
            try
            {
                using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                socket.Connect(new UnixDomainSocketEndPoint(path));
                socket.Send(Encoding.UTF8.GetBytes(command + "\n"));
                return true;
            }
            catch
            {
                return false; // Left over from a crash: nobody is listening
            }
        }

        public static void StartListening()
        {
            string path = SocketPath;
            try
            {
                if (File.Exists(path)) File.Delete(path);
                _listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                _listener.Bind(new UnixDomainSocketEndPoint(path));
                AppPaths.MakePrivate(path);
                _listener.Listen(4);
                _ = AcceptLoopAsync(_listener);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"TasksBar: couldn't open the command socket: {ex.Message}");
            }
        }

        private static async Task AcceptLoopAsync(Socket listener)
        {
            while (true)
            {
                Socket client;
                try { client = await listener.AcceptAsync(); }
                catch { return; } // Closed on exit

                _ = Task.Run(async () =>
                {
                    using (client)
                    {
                        var buffer = new byte[256];
                        int read;
                        try { read = await client.ReceiveAsync(buffer, SocketFlags.None); }
                        catch { return; }
                        string command = Encoding.UTF8.GetString(buffer, 0, read).Trim();
                        if (Array.IndexOf(Commands, command) < 0) return;
                        Dispatcher.UIThread.Post(() => CommandReceived?.Invoke(command));
                    }
                });
            }
        }

        public static void Stop()
        {
            try
            {
                _listener?.Dispose();
                File.Delete(SocketPath);
            }
            catch { }
        }
    }
}
