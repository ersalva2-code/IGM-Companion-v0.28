# IGM Companion 0.28

Windows x64 / .NET Framework 4.8. This source package is independent of IGM's DLL
sources. The UI layout, colours and gallery are retained from Companion 0.26.
The old hard-coded system list is removed. Data comes from the selected save and
installed IGM catalogue. The protected official emblem is retained.

## Code map

- `Core.cs`: models, bounded LZ4/JSON save reading, mapped/obfuscated field aliases,
  current-context selection, address normalization, owner/cache validation.
- `Storage.cs`: IGM file contract, merge planning, byte preservation, backups,
  change detection and staged atomic replacement. No DLL version-number gate.
- `Art.cs`: bitmap/legacy icon reading, BGRA180 export, official emblem and resources.
- `Interface.cs`: original WinForms canvas, input controls and explicit import/export.
- `AssemblyInfo.cs` / `app.manifest`: version, framework target, normal-user execution.

The code is formatted for reading rather than compressed into long lines.
`Assets.resources` is a standard .NET ResourceWriter data file. It contains 178
original symbol PNGs, a logo, glyph atlas and six-language dictionary. Its values
are byte arrays, not assemblies or executable programs. It can be inspected with
.NET's ResourceReader. Nothing is extracted or executed from it. `Companion.ico`
is the original icon artwork stored with smaller image encodings.

## Build on Windows

Use Microsoft Roslyn **4.11.0** and the .NET Framework 4.8 reference assemblies
(NuGet package Microsoft.NETFramework.ReferenceAssemblies.net48 **1.0.3** for the
release build). No compiler/runtime is bundled or downloaded by the script.
The legacy framework csc.exe is deliberately rejected for this verified build.

```powershell
.\build.ps1 -Compiler C:\path\roslyn\csc.exe -ReferenceDirectory C:\path\net48 -VerifyRebuild
```

The output is `build/IGM_Companion.exe`. `-VerifyRebuild` compiles twice and refuses
success if SHA256 hashes differ. Builds use deterministic compilation, C#5, x64,
optimization, no debug information and `/pathmap:<source>=/src/IGMCompanion`.
The release was independently rebuilt from a second source/output directory and
both hashes matched. This verifies our compiler/inputs, not every future compiler.
No game files are installed or modified. Framework references are mscorlib,
System, System.Core, System.Drawing, System.Windows.Forms and System.Web.Extensions.

## Compatibility contract and limits

Export keys `system_ua` and `alliance_id_words` stay first and compact because the
current IGM reader expects that shape. Unknown trailing JSON fields are preserved
verbatim; append-only additions never reserialize previous records. New names have
priority 1; existing system/planet metadata is retained. Current limits are 4096
catalogue systems, 8192 exploration entries and 128 custom icons in the IGM reader.
The adapter refuses unknown touched-file schemas rather than guessing. A future
IGM format change can require updating `Storage.cs`; version changes alone do not.

Saves and DLLs are never written. The app checks NMS process names but does not
open process memory, inject code, start other programs, access the network or
configure AV exclusions. Export requires the game to be closed. File comparisons
catch changes during planning/staging; an unrelated application ignoring the lock
can still race a final OS replacement, so this is not a multi-writer database.
Rollback retains backups; it does not overwrite a third party's detected changes.
A disk failure during rollback can require restoring a retained backup manually.

The old `copias_companion` backups are retained. Local preferences retain one `.bak`
per file rather than accumulating a backup on each editor keystroke.

## Verification

`tests/Checks.cs` runs 291 checks, including all galaxy indices, embedded resources,
BOM/obfuscated/expedition input, ownership, Unicode, merge/idempotence, unknown
schema rejection, preservation, backup and staging failures.
`tests/fixtures.py` generates synthetic data in a fresh `test-output` folder.
Compile Checks.cs as a console program referencing the built Companion and its
framework references, then run `Checks.exe <fixture-folder>`.
`tests/contracts.c` is an optional check against external, separately obtained
IGM 0.66 source headers (`exploration45.h`, `icon52_format.h`). These IGM headers
are not included. `tests/RealSave.cs` is an optional read-only test for a user's save.
No personal save is included. Test output/log summaries are in `VALIDACION.txt`.

Windows GUI testing, Windows runtime integration and a new VirusTotal scan remain
pending. Source static review and compilation do not establish zero AV detections.
The EXE is unsigned. See the release notes for resource provenance and rights.

## 0.28: automatic local union

Every save load resolves IGM_datos beside the installed Companion, reads the local
catalogue and merges by alliance ID and UA/planet key. It adds local-only alliances
and systems, keeps local names/art and preserves save ownership/selection. Repeating
the merge does not duplicate records. Loading and merging do not write IGM files.
A conflict retains the incoming assignment for inspection, clears selection and
blocks export/profile reassignment; the disk catalogue is not overwritten. A fresh
load from corrected inputs can clear the conflict. The UI/footer reports conflicts.
Local read failures are reported rather than hidden behind an import success notice.
Season/Expedition active-context aliases are both recognised.

The submitted pair of saves each contains six alliances. The submitted local
catalogue contains nine. Their union is 75 systems and eleven alliance IDs with
no conflicts in these inputs. None of those personal inputs is included here.

MergeChecks.cs adds 10 synthetic merge/access checks and five optional checks
per real save/catalogue pair. No personal files are included.
