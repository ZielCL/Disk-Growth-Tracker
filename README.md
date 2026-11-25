# Disk Growth Tracker

Disk Growth Tracker is a portable Windows app that shows which folders are getting bigger between scans. It keeps up to 12 scans per location on your PC.

## What you can do

- Scan a drive or folder and compare it with an earlier scan.
- Find growing folders anywhere within a scanned location.
- Inspect up to 100 of the largest files in a folder and its subfolders.
- Copy file and folder paths or find them in File Explorer.
- Switch between English and Spanish while the app is running.

## Use the app

Open the executable, choose a drive or folder, and select **Scan now**. The first scan is your baseline. Scan the same location again to see how much its folders changed. Use **Growth only** to focus on folders that got bigger.

The scanner uses file sizes. It skips reparse points such as junctions and marks values affected by unreadable entries with `≈`. History and the language preference are stored in `%LOCALAPPDATA%\CrecimientoDisco`.

## Build on Windows

The app uses C# and Windows Forms on .NET Framework. The build script uses the Windows .NET Framework C# compiler.

```powershell
.\build.ps1
```

The executable will be at `artifacts\Disk Growth Tracker.exe`. Add `-RunChecks` to build and run the checks with temporary sample folders.

```powershell
.\build.ps1 -RunChecks
```
