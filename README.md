
# TasksBar



## The Tasks App That Is in Your Taskbar
TasksBar is a lightweight, beautifully native Tasks widget for Windows. 

<img width="658" height="762" alt="Recording 2026-11-13 145500" src="https://github.com/user-attachments/assets/b0426097-40e1-409b-bd14-0c9b25d63d74" />

Built with WPF and the modern Windows 11 design language, it lives quietly in your System Tray and pops up exactly when you need it. It offers full two-way synchronization with your Google account, allowing you to manage your daily tasks without ever opening a web browser.


## ✨ Features

* **Native Windows 11 Design:** Uses `Wpf.Ui` to deliver gorgeous Mica and Acrylic backdrops, matching your system's light/dark themes seamlessly.
* **Hyper-Optimized Memory:** Uses aggressive memory-flushing techniques and Workstation GC to drop idle RAM usage down to ~2MB while hidden.
* **System Tray Integration:** Runs silently in the background as a lightweight NotifyIcon widget. 
* **Two-Way Google Sync:** Instantly pulls, creates, and completes tasks directly from your Google Account.
* **Sticky Notes:** Quickly write down anything you want.
* **Auto-Startup:** Option to register directly with the Windows Registry to launch silently on boot.
* **Satisfying UX:** Features custom-built, bouncy physics animations for task completion.

<img width="532" height="586" alt="Recording 2026-11-13 145924" src="https://github.com/user-attachments/assets/430899e1-23db-4c31-8cff-041e1cbd2f66" />

 # * You can also put cool cat images in your notes
 
<img width="776" height="860" alt="Recording 2026-11-13 150752" src="https://github.com/user-attachments/assets/d18c6a9b-bdc9-49c9-9a50-e28abc866c76" />
---


---
## 🚀 Downloading
1. Head over to [Releases](https://github.com/Daniel-Geron/TasksBar/releases/tag/1.1.2) and grab the latest release.
2. Unzip it anywhere on your computer.
3. launch `TasksBar.exe`.

### (Optional) Setting up Google Tasks API

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
14. Launch the app and in the settings turn on `Google Tasks Sync`.
15. A browser window will popup (if not just click `Log in to Google`) prompting you to log in. **!Make sure to log in with the same account(s) you added in step 8!**
16. If done correctly, a success message will pop up and all of your Google Tasks lists, tasks and details will be synced with TasksBar.

Note: For security reasons, every once in a while Google will prompt you to log in again.



## Building Project 
### Prerequisites
To build and run this project, you will need:
* Visual Studio 2022 (or newer)
* [.NET 8.0 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)


Clone this repository to your local machine:
   ```bash
   git clone https://github.com/Daniel-Geron/TasksBar.git
