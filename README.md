<div align="center">
  <img src="TagThemAll-icon-01.png" alt="TagThemAll Logo" height="200" />
  
  <h1><font size="7">TagThemAll</font></h1>
  
  <h3>Fast, native, and lightweight Windows file tagger & fuzzy finder.</h3>
  
  <p>
    Tag, organize, and find any file instantly across your drives — without moving, copying, or modifying your originals.
  </p>

  <br/>

  <p>
    <a href="https://github.com"><img src="https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-0078D6?logo=windows&logoColor=white" alt="Platform Windows" height="20" /></a>
    <a href="https://dotnet.microsoft.com"><img src="https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet&logoColor=white" alt=".NET 9" height="20" /></a>
    <a href="#"><img src="https://img.shields.io/badge/UI-Fluent%20Modern%20Dark-2d3748" alt="UI" height="20" /></a>
    <a href="#"><img src="https://img.shields.io/badge/Architecture-Zero%20Electron-success" alt="Zero Electron" height="20" /></a>
    <a href="LICENSE"><img src="https://img.shields.io/badge/License-GPLv3-blue.svg" alt="License GPLv3" height="20" /></a>
    <a href="../../releases/latest"><img src="https://img.shields.io/badge/Version-1.0.0-blue" alt="Latest Version" height="20" /></a>
  </p>
  
  <p>
    🇫🇷 <em><a href="README-fr.md">Documentation en français disponible ici</a></em>
  </p>
</div>

> [!NOTE]
> ### ☕ A quick note from the creator
> **Full disclosure:** I am not a software developer, and GitHub / open-source culture is completely new territory for me! 
> 
> I **vibe-coded** TagThemAll simply because I desperately needed a tool like this for my daily workflow and none of the existing solutions worked for me. 
> 
> If you submit Issues or Pull Requests, **please be patient and gentle with me**. I’ll do my best to review things when I can, but software development is not my day job. Thank you so much for your understanding, kindness, and support! ❤️

---

## 💡 Why TagThemAll?

Most file organizers force you into rigid folder structures, duplicate your heavy files, or run on clunky web wrappers (Electron/Chromium) that consume gigabytes of RAM.

**TagThemAll** adopts a **reference-based architecture** inspired by professional creative suites like **DaVinci Resolve** and **Adobe Lightroom**:
* Your files stay right where they are on your disks.
* TagThemAll stores references, metadata, and tags in an ultra-fast local database.
* Move or rename folders? The smart **Relink** engine finds them in one click.

---

## ⚡ Key Features

* **🚀 100% Native Windows & Zero Web Bloat**
  * Built with **C# / .NET 9 + Modern WPF (Dark / Fluent UI)**.
  * Zero Electron, zero Chromium, zero external servers or background tracking.
  * Instant startup, sub-second search responses, and DirectX hardware-accelerated rendering.

* **📥 Non-Destructive Drag & Drop Import**
  * Drop single files, multiple selections, or entire directory trees.
  * TagThemAll indexes paths and file metadata (name, extension, size, modification date).
  * **Zero byte modification**: your originals remain untouched.

* **🖼️ High-Performance Native Thumbnail Engine**
  * Directly leverages Windows native COM APIs (`IShellItemImageFactory`).
  * Reuses Windows File Explorer's existing thumbnail cache instantly.
  * Smooth, asynchronous background generation for new files without freezing the UI.
  * High-resolution shell icons as automatic fallback for non-media files (source code, documents, archives).

* **🔗 DaVinci Resolve-Style UNLINKED & RELINK Engine**
  * If a file is moved, renamed, or deleted outside the app, its card immediately displays a distinct red **`UNLINKED`** badge.
  * Filter all unlinked files with a single click.
  * **Smart Relink**: right-click any item or hit **Relink**, point to the new folder, and TagThemAll automatically matches files and restores all links without losing any assigned tags!

* **👁️ Instant QuickLook Preview (macOS-Style)**
  * Press <kbd>Space</kbd> (or double-click) on any file to open an ultra-fast high-res preview window.
  * Instantly preview full-resolution images, videos, audio, text, or source code files with metadata & dimension overlays.
  * Seamlessly navigate through your filtered files using arrow keys without ever closing the preview.

* **🚀 Outbound Drag & Drop (Seamless Creative Workflow)**
  * Drag any file directly **out of TagThemAll** into third-party software: Premiere Pro, DaVinci Resolve, Photoshop, Blender, Discord, Slack, or File Explorer.
  * No need to dig through nested disk folders: find your asset by tag, grab it, and drop it straight onto your timeline or project canvas!

* **🏷️ Fast Tagging & Dynamic Autocomplete**
  * Multi-file tagging (Ctrl + Click, Shift + Click, or <kbd>Ctrl</kbd> + <kbd>A</kbd>).
  * Quick entry: type a tag name and hit <kbd>Enter</kbd> to apply instantly.
  * Live autocomplete with intelligent suggestions based on existing tags.
  * Automatically generated, harmonious color palettes for visual clarity.
  * Remove tags effortlessly with one click on the tag's `×` badge.

* **🔍 Real-Time Fuzzy Search & View Customization**
  * Instant fuzzy search across tags, file names, or both.
  * Quick category tabs: `All`, `UNLINKED`, `Untagged`, `Images`, `Videos`, `Audio`, `Documents`.
  * Flexible sorting: Name, Date Modified, Date Added, File Size, Tag Count (Ascending/Descending).
  * Dual display modes: **Thumbnail Grid View** (with smooth zoom slider) and **Detailed List View**.

---

## 📦 Download & Installation

Head over to the **[GitHub Releases](../../releases/latest)** page to download the latest version:

| Package | Description | How to Run |
|---|---|---|
| **`TagThemAll-Setup.exe`** | Standard Windows Installer | Run the setup file to install TagThemAll with Desktop & Start Menu shortcuts. |
| **`TagThemAll-Portable.zip`** | Standalone Portable Version | Extract the `.zip` anywhere (e.g. a USB flash drive) and run `TagThemAll.exe` directly without installation. |

> **Requirements**: Windows 10 (version 1809+) or Windows 11 (64-bit).

---

## ⌨️ Keyboard Shortcuts

| Shortcut | Action |
|---|---|
| <kbd>Space</kbd> | Open / close instant QuickLook preview on selected file |
| <kbd>←</kbd> / <kbd>→</kbd> (in QuickLook) | Navigate to previous / next file in preview |
| <kbd>Ctrl</kbd> + <kbd>A</kbd> | Select all files in current view |
| <kbd>Esc</kbd> | Deselect all / dismiss autocomplete dropdown |
| <kbd>Enter</kbd> (in tag input) | Validate and apply tag to selected files |
| <kbd>Down Arrow</kbd> (in tag input) | Navigate autocomplete suggestions |
| <kbd>Del</kbd> | Remove selected file(s) from TagThemAll catalog |
| <kbd>F5</kbd> | Refresh thumbnails and reload catalog |

---

## 💾 Storage & Data Privacy

* **Local Database**: Embedded **SQLite** database optimized with Write-Ahead Logging (`WAL` mode) for maximum speed and zero data corruption.
* **Default Location**: `%LocalAppData%\TagThemAll\tagthemall.db`
* **True Portable Mode**: If a `portable.txt` file exists in the same folder as `TagThemAll.exe`, the database (`tagthemall.db`) is stored locally next to the executable, allowing 100% self-contained portable use on external drives.
* **100% Offline & Private**: TagThemAll never accesses the Internet. Your catalog and file organization stay exclusively on your machine.

---

## 📄 License

This project is licensed under the GNU General Public License v3.0 - see the [LICENSE](LICENSE) file for details.
