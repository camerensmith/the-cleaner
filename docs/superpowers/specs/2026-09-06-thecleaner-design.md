# thecleaner — Design Spec

**Date:** 2026-09-06  
**Status:** Approved for implementation planning  
**v1 platform:** Windows  
**Later platforms:** Linux, macOS (separate releases, shared UI/core)

## Problem

Windows (and other OSes) block delete/rename when another process holds a file. Finding the locker is a scavenger hunt. **thecleaner** accepts a drag-dropped file or folder, shows who holds it, then on confirm closes handles where possible, terminates locking processes, and optionally deletes the target — recursively for folders.

## Goals

- Drag-drop (or argv) → confirm UI with locker list → unlock / unlock & delete
- Recursive folder support
- Elevate only when needed (Windows: UAC relaunch)
- Always terminate locking processes after handle close attempts
- Cross-platform architecture; ship separate binaries per OS
- Windows-first implementation; Linux/macOS backends stubbed then filled in later

## Non-goals (v1)

- Kernel driver / kernel module
- Shell context-menu installer
- Scheduled or background cleaning
- Single binary that runs on all OSes
- Closing handles without user confirmation

## Architecture

**Stack:** .NET 8 + Avalonia UI.

| Layer | Responsibility |
|-------|----------------|
| `TheCleaner` | Avalonia shell: drop zone, confirm dialog, results |
| `TheCleaner.Core` | Path expansion, safety checks, orchestration, result models |
| `TheCleaner.Windows` | Restart Manager, NT handle close, terminate, elevate, delete |
| `TheCleaner.Linux` | Stub `ILockKiller` (v2) |
| `TheCleaner.Mac` | Stub `ILockKiller` (v2) |

```
Drop / argv
    → Core: validate + expand paths
    → Platform: find lockers
    → UI: warn + list (name, PID, path)
    → User: Unlock | Unlock & Delete (default) | Cancel
    → Platform: close handles → terminate lockers → optional delete
    → UI: result summary + last-run.log
```

Platform selection is compile-time / publish RID: each release links the matching backend.

## Interface (conceptual)

```csharp
interface ILockKiller
{
    Task<IReadOnlyList<LockHolder>> FindLockersAsync(IReadOnlyList<string> paths, CancellationToken ct);
    Task<KillResult> UnlockAsync(IReadOnlyList<string> paths, UnlockOptions options, CancellationToken ct);
}

record LockHolder(int Pid, string ProcessName, string Path);
record UnlockOptions(bool DeleteAfterUnlock, bool TerminateLockers /* always true in v1 */);
```

## Windows kill pipeline (v1)

1. **Expand** folders depth-first into a concrete path list (for delete counting and per-file unlock).
2. **Find lockers**
   - Primary: Restart Manager (`RmStartSession`, `RmRegisterResources`, `RmGetList`).
   - Fallback: `NtQuerySystemInformation` system-handle enumeration matched to target paths.
3. **Release**
   - Best-effort close remote handles (`DuplicateHandle` / NT object close into our process).
   - **Always terminate** remaining locking PIDs.
   - Never terminate: our own PID; hard-coded critical processes (`csrss`, `smss`, `wininit`, `services`, `lsass`, `System`). Report as protected/skipped.
   - Re-scan once; leftover locks → per-path failure in results.
4. **Delete** (if Unlock & Delete)
   - Files then directories bottom-up.
   - Clear read-only attribute when needed.
5. **Elevation**
   - Start unelevated.
   - On access denied for handle/process/delete → relaunch elevated with same paths + chosen action; unelevated instance exits.

## Confirm UI + safety

**UI**

- Show target path(s); truncate middle if long.
- Warning: handles will be closed; locking processes **will be terminated**.
- List: process name, PID, path (folders: “N locked items” summary plus holders).
- Buttons: Cancel | Unlock | **Unlock & Delete** (default, strongest affordance).
- No paths on launch: idle drop zone (do not exit).
- Multi-path drop: one confirm for all.
- After run: short summary (freed / deleted / failed + reason).

**Safety rails**

- Confirm required; never auto-nuke on drop.
- Refuse: volume roots (`C:\`), user profile root, Windows directory — hard error, no action.
- Folder delete: show file count before confirm.
- Log to `%LOCALAPPDATA%\thecleaner\last-run.log`.

## Linux / macOS (later releases)

Same UI and core. Differences:

| | Linux | macOS |
|---|--------|--------|
| Find | `/proc`, `fuser`/`lsof` | `lsof` |
| Release | Kill processes (no remote FD close) | Kill processes |
| Elevate | `pkexec` when needed | Admin authorization when needed |
| Drag-drop | Ship `.desktop` with `Exec=… %F` | `.app` / Finder drop target |

v1 stubs return a clear “not implemented in this release” if invoked.

Kernel backends remain out of scope unless a future major version is explicitly scoped.

## Project layout

```
thecleaner/
  src/
    TheCleaner/
    TheCleaner.Core/
    TheCleaner.Windows/
    TheCleaner.Linux/
    TheCleaner.Mac/
  docs/superpowers/specs/
```

## Releases

| Release | Artifact | Backend |
|---------|----------|---------|
| v1 | `thecleaner.exe` (win-x64, self-contained single-file) | Windows |
| v2 | Linux x64 (+ `.desktop`) | Linux |
| v3 | macOS (arm64/x64 as needed) | Mac |

Publish example (Windows):  
`dotnet publish src/TheCleaner -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true`

## Error handling

- Per-path results; one failure does not abort the rest.
- Protected process / refused path → explicit message, not a crash.
- Elevation cancel (UAC No) → abort with “elevation required / cancelled.”
- Unexpected exceptions → log + user-visible failure string.

## Testing (v1)

- Unit: path safety refusals, folder expansion, result aggregation.
- Manual: lock a file with Notepad / a holding process; drop on exe; Unlock; Unlock & Delete; folder recurse; UAC path; refuse `C:\`.

## Success criteria (v1)

- Drag file onto `thecleaner.exe` → see lockers → Unlock & Delete removes it without a scavenger hunt.
- Locked folder deleted recursively after terminate.
- Unelevated start; elevates only when required.
- Linux/mac projects compile as stubs without blocking Windows ship.
