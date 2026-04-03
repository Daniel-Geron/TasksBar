using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Wpf.Ui.Controls;

namespace GTasksBar
{
    public partial class DesktopWidgetWindow : FluentWindow
    {
        // Win32 APIs to force the window to stay behind all other apps
        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        const uint SWP_NOSIZE = 0x0001;
        const uint SWP_NOMOVE = 0x0002;
        const uint SWP_NOACTIVATE = 0x0010;

        public DesktopWidgetWindow()
        {
            InitializeComponent();

            // Push to bottom when the window loads
            this.SourceInitialized += (s, e) => SendToBottom();

            // Prevent it from coming to the front if accidentally clicked
            this.Activated += (s, e) => SendToBottom();
        }

        private void SendToBottom()
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
    }
}