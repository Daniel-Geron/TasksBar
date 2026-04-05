# TasksBar

<video src="https://github.com/user-attachments/assets/ab279dad-7f38-4aa3-98ba-8c6a9366229b" autoplay loop muted playsinline width="100%">
</video>
TasksBar is a lightweight, beautifully native Google Tasks widget for Windows. 

Built with WPF and the modern Windows 11 design language, it lives quietly in your System Tray and pops up exactly when you need it. It offers full two-way synchronization with your Google account, allowing you to manage your daily tasks without ever opening a web browser.

## ✨ Features

* **Native Windows 11 Design:** Uses `Wpf.Ui` to deliver gorgeous Mica and Acrylic backdrops, matching your system's light/dark themes seamlessly.
* **System Tray Integration:** Runs silently in the background as a lightweight NotifyIcon widget. 
* **Two-Way Google Sync:** Instantly pulls, creates, and completes tasks directly from your Google Account.
* **Auto-Startup:** Option to register directly with the Windows Registry to launch silently on boot.
* **Hyper-Optimized Memory:** Uses aggressive memory-flushing techniques and Workstation GC to drop idle RAM usage down to ~2MB while hidden.
* **Satisfying UX:** Features custom-built, bouncy physics animations for task completion.

<video src="https://github.com/user-attachments/assets/be4ac712-46f0-4aa8-84d6-0e651b8c8e34" autoplay loop muted playsinline width="100%">
</video>
---

## 🚀 Getting Started

### Prerequisites
To build and run this project, you will need:
* Visual Studio 2022 (or newer)
* [.NET 8.0 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
* A Google Cloud Console account (for the Tasks API)

### 1. Set up the Google Tasks API
Because this app connects to Google Tasks, you need to provide your own API credentials to build it.
1. Go to the [Google Cloud Console](https://console.cloud.google.com/).
2. Create a new project and enable the **Google Tasks API**.
3. Navigate to **APIs & Services > Credentials**.
4. Click **Create Credentials > OAuth client ID**.
5. Choose **Desktop app** as the application type and create it.
6. Click **Download JSON** on your new credential.
7. Name the downloaded JSON as `credentials.json` and drag & drop the file **into the project directory** (!Not the folder with the .sln file)


Clone this repository to your local machine:
   ```bash
   git clone https://github.com/Daniel-Geron/GTasksBar.git

