# Flare Tools · Labels

The Flare Tools Windows app from Flare Fireplaces for turning Excel contacts into centered, printable Word address labels.

Import an `.xlsx`, `.xlsm` or `.csv` file, review contacts, and export a `.docx` using the supplied 4 × 2-inch template: two columns, five rows, US Letter paper. Every address block is centered horizontally and vertically.

Labels show the company, a first and last name only when both are present, and the mailing address. A complete personal name can be used when no company is provided. Incomplete selected addresses must be corrected or excluded; possible duplicates are flagged for review. Labels wrap and fit without dropping text. Starting positions support partially used sheets.

## Install and use

Download the installer from Releases. Each teammate installs it under their own Windows account; no admin rights, login or separate .NET installation is needed. The portable ZIP contains a runnable copy for backup or use without installation.

Print the Word document on US Letter paper, single sided, one page per sheet, at 100% scale. Test on plain paper first. Font size and printer calibration can be adjusted inside the app.

## Updates

The app checks this repository's stable GitHub releases on startup, at most once a day. **Check for updates** also checks on demand. An available update is offered to the user; the app does not interrupt work to install it.

Update manifests use RS256 signatures verified against the dedicated public key embedded in the app. The release version, exact GitHub installer path, byte size and SHA-256 checksum are checked before an installer can open. The private signing key is stored only in this repository's Actions secret, never in the application or backup ZIP.

## Build and release

Requirements: Windows 10/11 x64, .NET 10 SDK, and the Windows .NET Framework compiler included with Windows. The app contains its own .NET runtime when published.

```powershell
.\scripts\Build.ps1
```

The build compiles the app, runs the offline test suite against fictional contacts, renders the interface for review, builds and verifies the installer, and creates a portable ZIP. Output is under `dist/v<version>`.

Update the project version, commit the source, and push a matching tag such as `v1.0.2`. The GitHub release workflow repeats the checks, packages the installer and portable ZIP, signs and verifies the update manifest, and publishes the stable release only after those checks pass. A manual workflow run can create a draft release instead.

## Privacy and backup

Contact files stay on the user's computer. GitHub checks send no contact or spreadsheet data. Imported contact lists are not retained after closing the app. Settings and verified update downloads live outside the installation under `%LOCALAPPDATA%\Flare Labels`; user-generated Word documents are saved only to the location the user selects.

Source spreadsheets, generated labels, credentials, private keys, settings, logs and build output are excluded from Git. The test contacts are fictional. Microsoft .NET license and third-party notices are included with distributed app files.

## Flare Tools design

Version 1.1.0 introduces the shared black, red and lime (#99CC00) foundation, softer surfaces, refined icons, consistent dialogs and direct review actions. See [the reusable design foundation](docs/Flare-Tools-Design.md). Other Flare applications can adopt this foundation in subsequent updates.

Keyboard shortcuts: Ctrl+O imports, Ctrl+F searches, Ctrl+E saves labels, and Escape clears the active search. Click a label preview to select its contact; double-click to edit.
