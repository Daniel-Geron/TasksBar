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
Because this app connects to Google Tasks, you need to provide your own API in order to save and sync tasks.
1. Go to the [Google Cloud Console](https://console.cloud.google.com/).
2. Create a new project and enable the **Google Tasks API**.
3. Navigate to **APIs & Services > Credentials**.
4. Click **Create Credentials > OAuth client ID**.
5. Choose **Desktop app** as the application type and create it.
6. Click **Download JSON** on your new credential.
7. Name the downloaded JSON as `credentials.json` and drag & drop the file **into the project directory**
   
   NOTE: if you are just grabbing the release, drop the file to where `TasksBar.exe` resides




Clone this repository to your local machine:
   ```bash
   git clone https://github.com/Daniel-Geron/TasksBar.git
