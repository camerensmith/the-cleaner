# thecleaner — manual test checklist (v1, Windows)

Run against a published `artifacts/win-x64/thecleaner.exe`.

Most of the engine is covered by automated tests (`dotnet test`), including real locked
files, remote handle close, process termination and recursive delete. What needs a human
is the part that requires clicking: the confirm view, the action buttons, Explorer
drag-drop, and the UAC prompt.

## Automated already — no need to repeat by hand

| Behaviour | Where |
|---|---|
| Locked file found, handle closed, file deleted | `WindowsLockKillerTests` |
| Locking process terminated / protected list respected | `ProcessTerminatorTests` |
| Recursive folder delete, read-only attributes | `DeleterTests`, `PathExpanderTests` |
| Volume root / profile root / Windows dir refusal | `PathSafetyTests` |
| Confirm view, results view, cancel, elevated relaunch | `MainWindowTests` (headless) |

## Needs a human

| # | Scenario | Steps | Expected |
|---|----------|-------|----------|
| 1 | Explorer drag onto exe | Open a temp `.txt` in Notepad. Drag it onto `thecleaner.exe` in Explorer. | Confirm view lists Notepad with its PID and the file path. |
| 2 | Unlock & Delete | From #1, click **Unlock & Delete**. | Notepad's hold is released (Notepad may close); file is gone; summary reads `1 deleted`. |
| 3 | Unlock only | Repeat #1, click **Unlock**. | File still exists and is no longer locked; summary reads `1 unlocked`. |
| 4 | Cancel | Repeat #1, click **Cancel**. | Nothing terminated, nothing deleted, window returns to the drop zone. |
| 5 | Locked folder, recursive | Create a folder with nested files; open one in Notepad. Drag the folder in. | File/folder counts shown before confirm; after Unlock & Delete the whole tree is gone. |
| 6 | In-app drop | Launch with no arguments; drag a file into the window. | Window stays open on launch (does not exit); the drop starts a scan. |
| 7 | Multi-path drop | Select two files, drag both in at once. | A single confirm covering both; summary counts both. |
| 8 | Volume root refused | Run `thecleaner.exe "C:\"`. | Hard refusal, nothing changed, `C:\` untouched. Log records the refusal. |
| 9 | Profile root refused | Run `thecleaner.exe "%USERPROFILE%"`. | Hard refusal, nothing changed. |
| 10 | Windows dir refused | Run `thecleaner.exe "C:\Windows"`. | Hard refusal, nothing changed. |
| 11 | No UAC on launch | Launch the exe normally. | **No UAC prompt appears.** The app runs unelevated. |
| 12 | Elevation when needed | Lock a file using an elevated process (elevated cmd holding it open), then Unlock & Delete unelevated. | UAC prompt appears only at this point; accepting completes the job without a second confirm. |
| 13 | Elevation cancelled | Repeat #12, click **No** at UAC. | Results say elevation required / cancelled. No crash, window stays usable. |
| 14 | Long path display | Target a file with a very long path. | Path is trimmed in the middle, window does not stretch. |
| 15 | Log | After any run, open `%LOCALAPPDATA%\thecleaner\last-run.log`. | Targets, holders, handles closed, terminations, per-path outcomes, summary. |

## Known v1 behaviour worth confirming, not a bug

- When the Restart Manager reports the holders, each row shows the **scanned root** rather
  than a per-file path — RM answers for the whole registered batch. The handle-scan
  fallback (used when RM finds nothing) shows exact per-file paths. The source is named in
  the log for each holder.
- Very large folders: only the first 2000 expanded paths are registered with the Restart
  Manager (`RestartManagerLockFinder.MaxRegisteredPaths`); the handle scan still covers
  the rest.
