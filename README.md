# thecleaner

Drag a locked file or folder onto `thecleaner.exe`. It shows which processes are
holding it, and on confirm closes their handles, terminates whatever still holds on,
and optionally deletes the target — recursively for folders.

## Build

```
dotnet build
dotnet test
```

## Run

```
dotnet run --project src/TheCleaner
```

Or pass targets directly:

```
dotnet run --project src/TheCleaner -- "C:\path\to\locked.txt"
```

## Publish (Windows v1)

```
dotnet publish src/TheCleaner -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -o artifacts/win-x64
```

Produces a single `artifacts/win-x64/thecleaner.exe` with no loose DLLs.

## What it does

1. Refuses volume roots, your user profile root, and the Windows directory outright —
   a refused path is reported and **nothing is done to it**.
2. Expands folders depth-first and counts what will be affected, shown before you confirm.
3. Finds lockers via the Restart Manager, falling back to a system handle-table scan
   (slower, but it attributes each holder to an exact path).
4. On confirm: closes the remote handles, re-scans, terminates whatever still holds the
   target, re-scans again, then deletes if you chose **Unlock & Delete**.
   Often the handle close alone is enough and nothing gets terminated.
5. Writes `%LOCALAPPDATA%\thecleaner\last-run.log`.

Critical system processes (`csrss`, `smss`, `wininit`, `services`, `lsass`, `System`,
`winlogon`), PID 0, PID 4, and thecleaner's own process are never terminated — they are
reported as skipped.

## Administrator rights

thecleaner does **not** require or request administrator rights to start. Its manifest
declares `asInvoker`, so it launches with exactly the rights you already have and shows
no UAC prompt. Unlocking a process you own needs no elevation at all.

Elevation is only ever requested *reactively*: if a terminate or delete comes back with
access denied, thecleaner offers to relaunch itself through UAC, carrying the same
targets and the action you already confirmed. Declining that prompt is handled — you get
"Elevation required / cancelled" in the results and the app stays open.

## Command line

```
thecleaner.exe <path> [<path> ...]
```

Targets are shown in the confirm view; nothing happens until you choose an action.

The elevated relaunch uses `--elevated --action unlock|unlock-delete -- <paths>`. That
form — and only that form — proceeds without re-confirming, because the user already
confirmed before the UAC prompt. The same flags without `--elevated` still stop at the
confirm view.

## Platforms

Windows is the v1 backend. Linux and macOS compile as stubs that report
"not implemented in this release"; their real backends ship in later releases.
The backend is chosen at compile time from the publish RID — a `linux-x64` build links
only `TheCleaner.Linux`, with no Windows code in the output at all.

## Layout

| Project | Responsibility |
|---|---|
| `TheCleaner` | Avalonia UI: drop zone, confirm, results |
| `TheCleaner.Core` | Path safety, expansion, orchestration, results, logging |
| `TheCleaner.Windows` | Restart Manager, handle scan/close, terminate, delete, elevation |
| `TheCleaner.Linux` / `TheCleaner.Mac` | v2/v3 stubs |

`TheCleaner.Core` contains no P/Invoke and no UI framework references; the platform
backends implement its `ILockKiller` interface.
