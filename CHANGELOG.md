# 1.12.0

## Extra assignments, global feature keys and DEBUG readability

- Restricted beacon/matrix assignments to 1-12. Menu selectors offer Disabled plus those twelve IDs; omitted/-1 values stay disabled. Invalid legacy IDs, including 0 and 13+, are disabled without touching another extra. Duplicate and missing-extra protections remain.
- Removed feature key fields and keybind INI reads from vehicle/global profiles. Rumbler, FIAMMS, beacon and matrix input now uses Config.ini exclusively. Save Profile stops writing these bindings; legacy profile entries are ignored.
- Added typed DEBUG text styles, cyan section headings, green active states, gray inactive states, amber loading/interruption states, red errors, lavender configuration details and muted WAV filenames. Added bold emphasis with the existing text API and increased panel contrast. Layout caching includes style changes and measures the emphasis width; the renderer still uses immutable snapshots only.
- Kept DEBUG discovery of actual native extras independent of the 1-12 assignment range. Updated boundary, global-key reload and DEBUG coverage (28 regression checks total), examples and instructions. Syntax and project paths checked; compilation, test execution and in-game rendering remain unverified here.

# 1.11.1.0

## Vehicle horn interruption and keybind help

- Moved Horn Interrupts Siren beside Horn Cycles Siren in the Vehicle Specific profile editor. Save Profile persists each model's choice; both controls are disabled in Global Default mode.
- Playback and DEBUG now read the cached vehicle profile's interruption setting. Missing values default to true, and old Config.ini/Global.ini interruption entries are ignored. Silent horn cycling still changes tone immediately without interruption.
- Added the requested Windows Forms Keys reference URL as an INI comment in generated/loaded Config.ini files, saved profiles, and both examples. Comment insertion preserves existing file bytes, encoding and line endings and skips duplicate insertion.
- Extended horn-profile isolation/reload coverage and added an encoding-preservation regression check (27 checks total). C# syntax and project paths checked; compilation, regression execution and in-game testing remain unverified in this environment.

# 1.11.0.0

## Per-vehicle horn routing and DEBUG

- Replaced the global horn-cycle checkbox with a Vehicle Specific profile selector: native car horn, custom siren horn, Off, or silent cycling. Saved as Settings/HornCycleMode; Global.ini/Config.ini no longer control the mode.
- Route the horn independently of the main tone cycle. Silent mode suppresses native/custom horns and cycles immediately without stopping the main siren. Car-horn mode allows native input and skips the WAV; siren-horn mode suppresses native input and uses the profile/rumbler Horn WAV. Off keeps ordinary horn behavior without cycling.
- Apply native horn suppression to addon vehicles without a HasSiren flag. Missing custom Horn WAVs in siren-horn mode cycle silently with a notification. Retain once-per-press input behavior, empty-slot skipping, main-siren-off behavior and FIAMMS independence.
- Added the global Debug setting and Misc Settings DEBUG checkbox. A top-right overlay shows current vehicle audio states/files, output status, horn route, rumbler/FIAMMS toggles, lights and actual extra states; it hides on foot or when disabled.
- Discover extra IDs progressively, refresh status at 10 Hz, and reuse text layout. The render callback reads immutable snapshots only; native/entity/profile reads stay on the game fiber. Clear state on vehicle changes and unregister the render callback on unload. A debug rendering failure does not stop the sirens.
- Added five regression checks (26 total) and updated configuration examples and acceptance checks. C# syntax, references and bundled drawing API names checked. Compilation, regression execution and in-game rendering/audio remain unverified here.

# 1.10.0.0

## FIAMMS, controls, and pause

- Added FIAMMS as an independent looping siren voice, with normal/rumbler WAV slots, separate volumes, a per-vehicle menu toggle, and an F9 default binding. FIAMMS follows light restriction and exit/cleanup rules while remaining independent of main-tone changes, horn interruption, and manual muting.
- Removed player panic/scan bindings from configuration fields, load/save, and ELS import. Removed player auto-scan input handling, state, and timers. The controller on/off button now selects the first configured tone and keeps it selected; tone-cycle and AI rotation remain.
- Added the Horn Cycles Siren menu/config option. Horn presses use the existing tone-cycle behavior once per press. Horn interruption defers playback of the next selected tone until release. Held presses are not re-triggered by enabling the option or returning from menus/pauses.
- Added true audio pause before mixer reads for game pause, loading, zero time scale, and a stale game heartbeat. WAV cursors, reverb state, and gain ramps stop advancing; newly loaded voices wait for resume. Device creation/disposal remains off the game fiber, and pause/resume reuses the output.
- Suspend configuration-menu input during game pause, preserve fade/AI deadlines across any paused game-clock advance, and process released controls before resuming audio. F10 menu muting retains its existing behavior.
- Added four regression checks (21 total) plus live-output pause coverage, updated INI examples, and expanded in-game acceptance instructions. Syntax/reference checks pass; compilation, regression execution, and game testing remain unverified in this environment.

# 1.9.0.0

## Requested controls and sound banks

- Horn interruption now cancels the primary siren voice and restarts the latest selected tone from the beginning on release. Holding the horn does not repeatedly issue stop/start requests. Switching the siren off during interruption prevents a later restart.
- Added Tone5 and Tone6 to profiles, volumes, menu editing, direct keys, manual fallback, auto-scan, and AI rotation. Defaults are D8/D9 to preserve existing ELS scan/cycle/panic bindings. Single-tone auto-scan no longer restarts the same WAV every six seconds.
- Added per-profile rumbler support and separate normal/ON WAV and volume banks for all six tones, horn, and manual. Empty/missing alternate slots use the normal sound. Both menu banks retain selections while switching the editor and save together.
- Added a live per-vehicle rumbler menu switch and configurable F11 default toggle. Runtime rumbler state is separate for each vehicle; it also follows vehicles tracked as AI. Preloading covers both banks using the existing background worker.
- Added vehicle-specific RedBeacon and MatrixText1/2/3 extra IDs plus configurable keybinds. The beacon is independent; matrix selections are mutually exclusive and can be toggled off. Missing/unassigned extras remain inactive; duplicate and out-of-range IDs are normalized safely. Extra IDs never inherit globally across unrelated vehicle meshes.
- Added modifier-chord handling and unbound-key checks. New feature bindings work alongside ELS imports and support per-profile overrides. Held feature keys do not trigger on vehicle entry or menu/pause exit.
- Preserve saved missing/nested WAV filenames when editing another setting. Preserve precise volume values when merely switching banks instead of rounding them to the menu's slider step.

## Build and verification

- Added PlaybackRules.cs and VehicleExtras.cs to the plugin project, and linked the pure playback/control rules into the regression project.
- Added six regression checks (17 total) and example Config/profile INIs. Updated setup, behavior, and in-game acceptance instructions.
- C# syntax and project paths checked; original dependency DLLs retained unchanged. Compilation, regression execution, and GTA V behavior remain unverified because the local .NET runtime cannot initialize and the game is unavailable.

# 1.8.1.0

## Freeze-related fixes

- Removed `WaveOutEvent.Stop/Dispose/Init/Play` from `SirenPlayer.Play` on the game fiber. One persistent output is created and maintained by an audio worker.
- Removed synchronous WAV decoding from vehicle entry, tone changes, and AI discovery. CachedSound now represents a lazy audio handle; decoding/resampling happens on the worker.
- Added cancellation for pending playback. A delayed load cannot resurrect a released horn, stopped siren, or removed vehicle voice.
- Fixed the infinite loop in `CachedSampleProvider.Read` for zero-length looping data. Empty WAVs are also rejected during decoding.
- Replaced the unretained timer and cross-thread AI dictionary iteration with a scalar heartbeat checked inside the output callback.

## Audio correctness and resource use

- Standardized sample formats before mixing; downmix stereo once while caching.
- Made stereo loop crossfade weights identical for both channels of each frame.
- Added short gain ramps and output clipping protection; reject/sanitize non-finite audio/configuration values.
- Continue fade completion while a voice is force-muted by the menu or an interrupting sound.
- Stop detached/deleted vehicle voices immediately so they cannot be left fading without an updater.
- Preserve the main siren's playback position across horn/manual interruptions instead of recreating it on release.
- Handle failed loads once, emit useful console diagnostics, bound cache retention, and retry an unavailable device without blocking the game fiber.
- Prune ended/cancelled mixer inputs even if the output device is unavailable; release output/cache resources on worker exit.

## Profiles, controls, and AI

- Cache profile settings, selections, and per-tone volumes. Steady-state per-frame checks no longer open INIs or call File.Exists.
- Apply vehicle-specific manual volume, and update active voices when their volume controls change.
- Invalidate actual audio handles on WAV reload, including failed loads. Refresh snapshots on configuration/profile reload.
- Prevent programmatic menu synchronization from firing save/volume side effects. Prevent empty vehicle-profile filenames and use case-insensitive WAV selection matching.
- Toggle the menu on the key's rising edge; close submenus consistently and suspend siren inputs during menus/pauses.
- Fix tracked light-stage timing and honor disabled light restriction before checking tracked stages.
- Use VCF stage configuration for the current vehicle's decorator fallback; exclude backup VCFs from lookup.
- Cancel a tracked AI voice immediately when entering its vehicle; enforce a lowered AI limit and avoid scanning the world when already at capacity.
- Allow AI to start with another configured tone when Tone1 is unavailable; use wrap-safe comparisons for scheduled tone changes/scans.
- Avoid muting the main siren for a missing manual sound. Retain the existing native-horn interruption option.
- Read decimal-dot and legacy decimal-comma settings, write floats consistently, clamp invalid values, and save MinDistance correctly.
- Preserve subdirectories when backing up ELS VCFs.

## Build and verification

- Replaced all absolute dependency HintPaths with relative `dependencies` paths.
- Added an explicit C# 7.3 setting and disabled copying the RAGE SDK reference as a runtime DLL.
- Added `build.cmd`, a Windows regression project, and installation/acceptance instructions.
- Syntax and project-path checks passed. Compilation and the regression/in-game runs remain unverified in this environment; see README.md.
