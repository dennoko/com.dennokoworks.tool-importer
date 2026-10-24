# dennokoworks Tool Importer (BLM Integration) User Manual

**dennokoworks Tool Importer** is a Unity editor extension that integrates with **BOOTH Library Manager (BLM)**, allowing you to easily browse, search, and import downloaded Unity packages (`.unitypackage`) directly into your Unity project.

---

## Table of Contents

1. [Overview & Key Features](#1-overview--key-features)
2. [System Requirements](#2-system-requirements)
3. [Installation](#3-installation)
4. [Launching the Tool](#4-launching-the-tool)
5. [Window Layout & UI Overview](#5-window-layout--ui-overview)
6. [Initial Setup (BLM Integration)](#6-initial-setup-blm-integration)
7. [Shop Selection and Management](#7-shop-selection-and-management)
   - [Switching Shops](#switching-shops)
   - [Managing Shops](#managing-shops)
   - [Default Shop Setting](#default-shop-setting)
8. [Browsing & Operating Item Cards](#8-browsing--operating-item-cards)
   - [Keyword Search & Status Filtering](#keyword-search--status-filtering)
   - [Infinite Scrolling](#infinite-scrolling)
   - [Import Buttons and Status Indication](#import-buttons-and-status-indication)
   - [Warning Badge (Hidden & Unextracted Items)](#warning-badge-hidden--unextracted-items)
9. [Importing Packages](#9-importing-packages)
   - [Single Package Import](#single-package-import)
   - [Multi-Package Selection Popup](#multi-package-selection-popup)
   - [Batch Import](#batch-import)
10. [Other Installed Tools (Project-level Tools)](#10-other-installed-tools-project-level-tools)
11. [Switching Languages (Japanese / English)](#11-switching-languages-japanese--english)
12. [Frequently Asked Questions & Troubleshooting](#12-frequently-asked-questions--troubleshooting)

---

## 1. Overview & Key Features

- **Direct Import from BLM Library**: Automatically detects assets downloaded via BOOTH Library Manager and allows one-click importing into your active Unity project.
- **Intuitive Status Badges**: Buttons clearly indicate package status (`Import`, `Re-import`, `Import Update`).
- **Import Status Filter**: Filter items by "Not Imported", "Update Available", or "Imported" with a single click.
- **Flexible Shop Filtering**: View all assets (`All Shops`), restrict to `dennokoworks` tools (with automatic version checking), or filter by custom registered BOOTH shops.
- **Batch Import**: Use Multi-Select Mode to check multiple assets and install them consecutively.
- **Smooth Infinite Scrolling**: High-performance chunked loading (batches of 30) ensures fast launch and smooth scrolling even with libraries containing hundreds of assets.
- **Automatic Version Checks**: For `dennokoworks` tools and the Tool Importer itself, remote GitHub release versions are checked automatically to notify you when updates are available.
- **Bilingual Support**: Instant toggle between Japanese and English.

---

## 2. System Requirements

| Item | Requirement |
| :--- | :--- |
| **Unity Version** | Unity 2022.3 or later |
| **Supported OS** | Windows 10 / 11, macOS |
| **Required Software** | [BOOTH Library Manager (BLM)](https://booth.pm) installed, with downloaded assets |

> [!NOTE]
> This tool references the local SQLite database (`data.db`) managed by BLM. Items must be downloaded in BLM first.

---

## 3. Installation

### Via VCC (VRChat Creator Companion)

1. Click the provided VPM repository link to register the repository in VCC.
2. Open your target project under "Manage Project" in VCC.
3. Locate **dennokoworks Tool Importer** in the package list and click the `+` button to install it.

---

## 4. Launching the Tool

From the Unity editor menu bar, select:

```text
dennokoworks > Tool Importer
```

*(You can also launch it via `Tools > dennokoworks > Tool Importer`)*

---

## 5. Window Layout & UI Overview

The main window consists of the following sections:

```text
┌─────────────────────────────────────────────────────────────┐
│ [dennokoworks Importer]  [Update available vX.X.X ↻] [EN/JA]│ ← Top Bar
│ Shop: [ dennokoworks ▾ ]  [＋]  [Manual]  [Settings]        │ ← Navigation Bar
├─────────────────────────────────────────────────────────────┤
│ (Settings Panel: BLM path override, clear cache)            │ ← Shown on "Settings"
├─────────────────────────────────────────────────────────────┤
│ [Check Remote Versions]  [Reload List]                      │
│ [ Filter search... ]  [▼ All ▾]  [☑ Multi-select Mode]     │ ← Action Bar
├─────────────────────────────────────────────────────────────┤
│ [Select All] [Select Updates] [Deselect All] [☑ Interactive]│ ← Shown in Multi-Select
│ [ Batch Import (N items) ]                                  │
├─────────────────────────────────────────────────────────────┤
│ Item List (dennokoworks): N items  [⚠️ 2]                    │ ← Item Header
│ ┌─────────────────────────────────────────────────────────┐ │
│ │ [Thumb] Item Name                                       │ │
│ │         [BOOTH] [Folder]  [ Import ▾ ]                  │ │ ← Item Card
│ └─────────────────────────────────────────────────────────┘ │
│                                                             │
│ [Other Installed Tools]                                     │
├─────────────────────────────────────────────────────────────┤
│ [ Open BOOTH Shop ]                                         │ ← Footer
│ Ready                                                       │ ← Status Bar
└─────────────────────────────────────────────────────────────┘
```

---

## 6. Initial Setup (BLM Integration)

When launched, the tool automatically looks for BLM's database file (`data.db`) and downloaded item directory:

- **When Detected Automatically**: Ready to use immediately without manual configuration.
- **When Not Found**: The "Settings" panel expands automatically with path setup prompts. Click "Browse..." to select your `data.db` or item folder. Click "Auto" at any time to revert to auto-detection.
- **Clear Analysis Cache**: If package inspection cache gets out of sync, click "Clear package analysis cache" in the Settings panel.

> [!TIP]
> **Default `data.db` Locations**
> - **Windows**: `%APPDATA%\pm.booth.library-manager\data.db`  
>   (e.g., `C:\Users\<Username>\AppData\Roaming\pm.booth.library-manager\data.db`)
> - **macOS**: `~/Library/Application Support/pm.booth.library-manager/data.db`

---

## 7. Shop Selection and Management

### Switching Shops
Click "Shop: [ Current Shop ▾ ]" on the navigation bar:

- **All Shops**: Displays all importable assets across your entire BLM library.
- **dennokoworks**: Filters exclusively to dennokoworks tools (enables automatic remote version checking).
- **Custom Registered Shops**: Displays items belonging to a specific shop you have added.

### Managing Shops
Click "Manage Shops..." in the shop dropdown or click the "＋" button on the navigation bar:

1. **Add from BLM**: Select from shops found in your BLM purchase history.
2. **Add by Subdomain**: Enter a BOOTH subdomain (`https://[subdomain].booth.pm`) directly.
3. **Remove Shop**: Click "Delete" next to any custom shop (*`dennokoworks` is built-in and cannot be removed*).

### Default Shop Setting
At the top of the Shop Management dialog, configure "Default shop on startup" to control which shop is selected when opening the window.

---

## 8. Browsing & Operating Item Cards

### Keyword Search & Status Filtering
- **Keyword Search**: Type text into the search field to filter items by name and shop name in real time.
- **Status Filter**: Click the "▼ All" dropdown button to filter by:
  - **All**: Show all items.
  - **Not Imported**: Show only items not yet imported into the project.
  - **Update Available**: Show items that have a newer version available in BLM.
  - **Imported**: Show only items already imported.

### Infinite Scrolling
To maintain high responsiveness, the list initially loads 30 items. As you scroll near the bottom, the next 30 items are appended automatically. You can also click the "Load more (N remaining)" button at the end of the list.

### Import Buttons and Status Indication

| Button Label | Color | Status | Description |
| :--- | :--- | :--- | :--- |
| **Import** | Blue | Not Installed | Not yet imported into the active Unity project. |
| **Re-import** | Gray | Installed | Already present in the project. Used to overwrite or reinstall. |
| **Import Update** | Orange | Update Available | A newer package version exists in BLM than what is currently installed. |
| **No Package** | Disabled | Unavailable | No `.unitypackage` file was found in the item folder. |

*If an item contains multiple `.unitypackage` files, a dropdown arrow (▾) is displayed on the button.*

### Warning Badge (Hidden & Unextracted Items)
A warning badge (⚠️) appears next to the item count if some items could not be listed.
Hover over the badge to view details:
- **Items without `.unitypackage`**: 3D models, textures, or non-Unity files.
- **Items with unextracted zip archives**: Extract the zip archive in BLM, then click "Reload List".

---

## 9. Importing Packages

### Single Package Import
1. Click the **Import** button on the item card.
2. The standard Unity package import dialog will open. Review files and click "Import".

### Multi-Package Selection Popup
If an item contains multiple package files (e.g. variants, multiple version archives), clicking the Import button opens a package selection popup. Select your desired package and proceed.

### Batch Import
1. Check **Multi-select Mode** on the action bar to reveal item checkboxes.
2. Select target items using checkboxes:
   - "Select All": Select all visible items.
   - "Select Updates": Select only items with newer versions available.
   - "Deselect All": Clear selection.
3. **Interactive Mode**: When checked, Unity's import confirmation dialog will prompt for each package. When unchecked, packages import continuously.
4. Click **Batch Import (N items)** to begin sequential installation.
5. A progress card appears with options to "Skip Current Item" or "Cancel Remaining".

---

## 10. Other Installed Tools (Project-level Tools)

If you have other dennokoworks tools installed in `Assets/dennokoworks/` that were added manually outside BLM, they are listed at the bottom of the window under **Other Installed Tools**:

- **Version Check**: Automatically verifies if a newer version is published on GitHub Releases.
- **Select**: Highlights (`Ping`) the tool's `version.json` file in the Unity Project view.
- **BOOTH**: Opens the developer's BOOTH store page.

---

## 11. Switching Languages (Japanese / English)

Click the **EN / JA** button at the top-right corner of the window to toggle the UI language instantly. Your language preference is saved in `EditorPrefs`.

> [!NOTE]
> When set to English, clicking "Manual" opens the English PDF manual (`Docs/user_manual_en.pdf`). If the English PDF is not yet compiled, it will automatically fall back to the Japanese PDF.

---

## 12. Frequently Asked Questions & Troubleshooting

### Q. Some special characters or symbols in product names display as "□" (tofu)
**A.** Certain emojis (e.g., 🐾, 🎀, 👗) and decorative symbols frequently used in BOOTH titles may appear as "□" if the font used by Unity UI Toolkit (TextCore SDF) does not contain glyphs for those characters.  
This is a display-only font limitation in Unity UI Toolkit. It does not affect package searching, importing, or the tool's functionality in any way.

### Q. Items downloaded in BLM do not show up in the list
**A.** Please check the following:
1. **Is the zip file extracted?** Ensure BLM is configured to extract archives automatically upon download. Raw `.zip` files cannot be inspected directly by Unity.
2. **Does the item contain a `.unitypackage`?** Items consisting only of raw FBX or textures without a `.unitypackage` are excluded from the import list.
3. **Check active shop and status filters:** Confirm that your active shop and filter settings match the item. Try selecting "All Shops" and "▼ All".

### Q. Import queue gets stuck or takes too long
**A.** In the import progress card, click "Skip Current Item" to continue with remaining packages, or click "Cancel Remaining" to abort the batch import.

### Q. Changes made in BLM are not reflected
**A.** After downloading or extracting new items in BLM, click **Reload List** on the action bar to refresh the local cache and reload database entries.

---

## Support & Feedback

For bug reports, feature suggestions, or inquiries, please contact the developer via the official BOOTH store or open an issue on GitHub.
