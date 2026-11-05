# Access review — Companion 0.28

This is a source/assembly review and test report, not an independent security audit
or an antivirus verdict. No AV exclusions, scanner bypasses or obfuscation are used.

| Area | Access | Purpose / boundary |
|---|---|---|
| Processes | `Process.GetProcessesByName("NMS")` | Only checks whether export must be refused. Handles disposed; no process memory, injection, launch or termination. |
| Save | File size, bounded byte reads, SHA256 | Explicitly selected file. LZ4 and JSON decode in memory. No save writes. |
| IGM files | Read catalogue, exploration, palette, icon files | Selected/resolved installed data directory. Loading/merging has no IGM writes. |
| Export | Journal additions, new exploration names, custom BMPs | Explicit Export action. Existing raw bytes/unknown JSON fields preserved, schema checked, NMS must be closed. |
| Backups/staging | `copias_companion`, temporary files, export lock | Original snapshots, flush, change checks, atomic replacement, best-effort rollback; staging/lock cleanup. |
| Preferences | LocalApplicationData/ImperialGalacticMap | Language, selected alliance/profile, editor styles; one `.bak` per file. No registry writes or autorun. |
| Resource images | Standard embedded .NET resources and explicit image import | No resource extraction, executable payloads, downloads or external runtime resource files. |
| Permissions | Windows `asInvoker`, `uiAccess=false` | No elevation request. An unwritable game directory produces an error; the app does not elevate automatically. |
| Network | None in app code | No telemetry, HTTP clients or updater. |

Regression reflection checks find no P/Invoke methods in the Companion assembly.
The framework itself implements WinForms, drawing and filesystem access normally.

Reproducibility: Roslyn 4.11.0 / .NET 4.8 refs 1.0.3; deterministic + source path
mapping. Two separate build roots produced identical EXE hashes. `build.ps1`
provides the same flags and an optional two-build hash comparison. The PowerShell
script itself has not been run in Windows here.

Pending: actual Windows UI/runtime integration, full user export/reload, independent
third-party reproduction and a new VirusTotal analysis. Results from 0.27 do not
apply to the new 0.28 hash. No reduction in detections is claimed for this build.
