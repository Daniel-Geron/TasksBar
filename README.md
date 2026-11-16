

# TasksBar



## The Tasks App That Is in Your Taskbar
TasksBar is a lightweight, beautifully native Tasks widget for Windows, and now [Linux](#-linux-preview) too.

<img width="658" height="762" alt="Recording 2026-11-13 145500" src="https://github.com/user-attachments/assets/b0426097-40e1-409b-bd14-0c9b25d63d74" />

Built with WPF and the modern Windows 11 design language, it lives quietly in your System Tray and pops up exactly when you need it. It offers full two-way synchronization with **Google Tasks** or **Tasks.org** (and other CalDAV servers), allowing you to manage your daily tasks without ever opening a web browser.


## ✨ Features

* **Native Windows 11 Design:** Uses `Wpf.Ui` to deliver gorgeous Mica and Acrylic backdrops, matching your system's light/dark themes and your Windows accent color seamlessly.
* **Hyper-Optimized Memory:** Uses aggressive memory-flushing techniques and Workstation GC to drop idle RAM usage down to ~2MB while hidden.
* **System Tray Integration:** Runs silently in the background as a lightweight NotifyIcon widget, with a themed right-click menu to switch between your lists.
* **Two-Way Sync, Your Choice of Service:**
  * **Google Tasks:** Instantly pulls, creates, and completes tasks directly from your Google Account.
  * **Tasks.org:** Connect your Tasks.org Cloud account with an app password, and your tasks stay in sync with the Tasks.org app on your phone.
  * **Any CalDAV server:** Nextcloud, Fastmail, iCloud, Radicale and other servers that store tasks over CalDAV work too.
  * **Or none at all:** Keep everything locally on your PC.
* **Task Management:** Add, edit, complete and delete tasks, and move them between lists with **Move to...**. Due dates, reminders and anything else set in other apps are left untouched.
* **Drag to Reorder:** Pick up any task and drop it where you want it, like moving apps on your phone. The new order syncs to Google Tasks and Tasks.org.
* **Keyboard Friendly:** Press Enter to add the next task, and use the Up/Down arrows to move between a task's title, its details and the tasks around it.
* **Sticky Notes:**
  * Write, draw, and paste images (like screenshots) straight into a note, then move, resize and rotate them.
  * Notes sync across your devices through your Google or Tasks.org account, including drawings and images.
  * Give notes a title (or let TasksBar name them after their first line, drawing or images) and rename them from Settings.
  * Choose which notes open when TasksBar starts, from Settings or from each note's ⋯ menu.
  * Drawings use a format shared by the Windows and Linux versions, so a note drawn on one shows up on the other.
  * System-wide keyboard shortcuts to create a new note or open all notes from any app.
* **Auto-Startup:** Option to register directly with the Windows Registry to launch silently on boot.
* **Satisfying UX:** Bouncy, custom-built animations for task completion: a glowing check circle, a particle burst, and a celebration toast with confetti and combos when you finish several tasks in a row.

<img width="494" height="552" alt="Recording 2026-11-16 070806" src="https://github.com/user-attachments/assets/0576c226-1a16-422e-90a3-f75dc88ba038" />

 # * You can also put cool cat images in your notes
 
<img width="776" height="860" alt="Recording 2026-11-13 150752" src="https://github.com/user-attachments/assets/d18c6a9b-bdc9-49c9-9a50-e28abc866c76" />
---


---
## 🚀 Downloading
1. Head over to [Releases](https://github.com/Daniel-Geron/TasksBar/releases/latest) and grab the latest release.
2. Unzip it anywhere on your computer.
3. launch `TasksBar.exe`.

To sync your tasks, set up **one** of the services below and pick it under **Settings → Behavior & Sync → Task Sync**. Without sync, tasks and notes are saved on your PC only.

### (Optional) Syncing with Tasks.org

Tasks.org sync uses **Tasks.org Cloud**, which needs a Tasks.org subscription that includes cloud sync.
1. Create an app password, either in the Tasks.org app (**Settings → your Tasks.org account → App passwords**) or by signing in at [tasks.org/account](https://tasks.org/account).
2. Copy the **username** and **password** it shows you. **They're only shown once**, so keep the page open until you've finished the next step.
3. In TasksBar, open **Settings → Behavior & Sync**, set **Task Sync** to `Tasks.org / CalDAV`, and keep the server on `Tasks.org Cloud`.
4. Paste the username and app password and click **Connect**. TasksBar shows how many task lists it found.
5. Close Settings and your Tasks.org lists will appear in TasksBar.

Note: if you delete the app password in Tasks.org, TasksBar will tell you and ask you to connect again with a new one. The password is stored encrypted for your Windows user.

### (Optional) Syncing with another CalDAV server

Same as above, but choose `Other CalDAV server` and enter your server's address (for example `https://cloud.example.com` for Nextcloud) along with your username and password. If your provider offers app passwords, use one.

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
14. Launch the app and in **Settings → Behavior & Sync** set **Task Sync** to `Google Tasks`.
15. A browser window will popup (if not just click `Log in to Google`) prompting you to log in. **!Make sure to log in with the same account(s) you added in step 8!**
16. If done correctly, a success message will pop up and all of your Google Tasks lists, tasks and details will be synced with TasksBar.

Note: For security reasons, every once in a while Google will prompt you to log in again.

### Updating from 1.2 or older

Starting with this version, sticky note drawings are saved in a new format that both the Windows and Linux versions can read. The first time you start the new version, TasksBar converts your existing drawings and syncs them once. If you use TasksBar on several PCs, update all of them: older versions can't read the new format and show those drawings as empty.


## 🐧 Linux (Preview)

TasksBar runs on Linux too (Ubuntu with GNOME, and KDE Plasma), with the same look and behavior as on Windows. It's built with [Avalonia](https://avaloniaui.net/) and shares all of its sync and data code with the Windows app, so tasks and sticky notes, including drawings and images, move freely between your Windows and Linux machines.

Everything from the Windows version is there: the flyout with all its animations, drag to reorder and keyboard navigation; sticky notes with drawings and images; the Settings window (sync, appearance, notes, lists); sticky note shortcuts; hover and click sounds; and launching when you log in. The only Windows-only feature is the Stream Deck plugin.

### Installing on Linux
* **Ubuntu, Debian, Mint, Pop!_OS, KDE neon:** download `tasksbar_<version>_amd64.deb` from [Releases](https://github.com/Daniel-Geron/TasksBar/releases/latest) and open it, or run `sudo apt install ./tasksbar_<version>_amd64.deb`.
* **Other distributions:** download `tasksbar-<version>-linux-x64.tar.gz`, unpack it anywhere and run `./tasksbar/install.sh` to add TasksBar to your app menu (no root needed).

Then start **TasksBar** from your app menu. It lives in the system tray: click the icon to open your tasks. On Ubuntu the tray works out of the box; on other GNOME desktops you may need the *AppIndicator* extension.

### Linux notes
* Settings, tasks and notes are kept in `~/.config/TasksBar`. For Google sync, put `credentials.json` there.
* **Sticky note shortcuts:** on GNOME (including Ubuntu), TasksBar adds them to GNOME's keyboard shortcuts for you, so they work on Wayland too. On other X11 desktops they're registered directly. On KDE Plasma with Wayland, Settings shows the commands to bind in **System Settings → Keyboard → Shortcuts**.
* **Commands:** `tasksbar --new-note`, `tasksbar --open-notes` and `tasksbar --toggle` act on the running TasksBar, handy for your own shortcuts or scripts. Right-clicking TasksBar in your dock or app menu also offers *New Sticky Note* and *Open All Sticky Notes*.
* Sounds are played with `pw-play` (PipeWire) or `paplay` (PulseAudio), which desktop installs already have.



## Building Project 
### Prerequisites
To build and run this project, you will need:
* Visual Studio 2022 (or newer)
* [.NET 8.0 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)


Clone this repository to your local machine:
   ```bash
   git clone https://github.com/Daniel-Geron/TasksBar.git
   ```

The solution has three projects:
* **TasksBar** – the Windows app (WPF).
* **TasksBar.Linux** – the Linux app (Avalonia).
* **TasksBar.Core** – everything the two share: settings, local data, Google Tasks and CalDAV sync, and sticky note sync.

### Building for Linux

The Linux app can be built on Windows or Linux. This makes a self-contained build that doesn't need .NET installed on the Linux machine:
   ```bash
   dotnet publish TasksBar.Linux -c Release -r linux-x64 --self-contained -o publish/linux-x64
   ```
Copy the `publish/linux-x64` folder to your Linux machine and run `./tasksbar`. It needs `libice6` and `libsm6`, which every Ubuntu and KDE desktop already has (on a minimal system: `sudo apt install libice6 libsm6`).

To build the release packages (`.deb` and `.tar.gz`) from that folder, run this on Linux or in WSL (it needs `dpkg-deb`, which Debian and Ubuntu have):
   ```bash
   bash packaging/linux/make-packages.sh publish/linux-x64 1.3.0 publish
   ```
