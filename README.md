# TasksBar



## The Tasks App That Is in Your Taskbar
TasksBar is a lightweight, beautifully native Tasks widget for Windows. 

![tasksbar1](https://github.com/user-attachments/assets/8c08e5c1-df9c-4341-b00a-adcdae3c5c68)

Built with WPF and the modern Windows 11 design language, it lives quietly in your System Tray and pops up exactly when you need it. It offers full two-way synchronization with your Google account, allowing you to manage your daily tasks without ever opening a web browser.

## ✨ Features

* **Native Windows 11 Design:** Uses `Wpf.Ui` to deliver gorgeous Mica and Acrylic backdrops, matching your system's light/dark themes seamlessly.
* **System Tray Integration:** Runs silently in the background as a lightweight NotifyIcon widget. 
* **Two-Way Google Sync:** Instantly pulls, creates, and completes tasks directly from your Google Account.
* **Auto-Startup:** Option to register directly with the Windows Registry to launch silently on boot.
* **Hyper-Optimized Memory:** Uses aggressive memory-flushing techniques and Workstation GC to drop idle RAM usage down to ~2MB while hidden.
* **Satisfying UX:** Features custom-built, bouncy physics animations for task completion.

![tasksbar2](https://github.com/user-attachments/assets/fe112f23-ef07-40fd-b0a9-6ef12638bfde)

---

## 🚀 Getting Started

### Prerequisites
To build and run this project, you will need:
* Visual Studio 2022 (or newer)
* [.NET 8.0 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
* A Google Cloud Console account (for the Tasks API)

### Setting up the Google Tasks API
Because this app connects to Google Tasks, you need to provide your own API credentials in order to save and sync tasks.

**Do not skip any step!**
1. Go to the [Google Cloud Console](https://console.cloud.google.com/).
2. Create a new project.
3. Navigate to **APIs & Services > Library**, search for **Google Tasks API**, and click **Enable**.
4. Navigate to **APIs & Services > OAuth consent screen**.
5. Choose **External** and click Create.
6. Fill in the required fields (App Name, Support Email, Developer Email) and click Save and Continue.
7. Skip the Scopes page (click Save and Continue).
8. **Add Test Users:** Because the app you created is in "Testing" mode, Google will block anyone from logging in unless they are on this list. Click **+ Add Users** and type in the exact Gmail address(es) you plan to use with the app. Click Save and Continue.
9. Navigate to **APIs & Services > Credentials**.
10. Click **Create Credentials > OAuth client ID**.
11. Choose **Desktop app** as the application type, name it, and click Create.
12. Click **Download JSON** on your new credential.
13. Rename the downloaded file to exactly `credentials.json`.
* **If building from source:** Drag and drop `credentials.json` into the root project directory in Visual Studio (make sure its properties are set to *Copy if newer*).
* **If using the compiled Release (downloaded the zip from Releases):** Drop `credentials.json` directly into the same folder where `TasksBar.exe` resides.




Clone this repository to your local machine:
   ```bash
   git clone https://github.com/Daniel-Geron/TasksBar.git
