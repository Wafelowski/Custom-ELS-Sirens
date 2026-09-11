# Custom ELS Sirens — 1.10.0.0

This revision adds an independent FIAMMS siren layer, optional horn tone cycling, and true audio pause. Player panic/auto-scan controls are removed. Six selectable main tones, normal/rumbler banks, and beacon/matrix extra controls remain. One persistent stereo output mixes all custom voices; WAV decoding and audio-device work stay on a background worker. No vehicle or RAGE native calls run on that worker.

## Verification status

- All 13 C# source files passed parser syntax checks.
- Both project files are valid XML; every included source file and bundled dependency path resolves.
- The new NAudio API names were checked against the bundled DLL metadata strings. This is not a type-checked compilation.
- **Compilation, regression-test execution, and GTA V playback were not possible in the editing environment.** The local .NET runtime could not initialize, and GTA V/RAGE Plugin Hook are unavailable. No rebuilt plugin DLL is included.
- `Tests/` contains 21 regression checks against the actual audio/configuration/playback-rule source and bundled NAudio DLLs. It substitutes a fake output device and small RAGE test doubles, and does not play sound or load the game. New checks cover FIAMMS profiles and independent layering, horn cycle/interrupt ordering, pause/resume sample continuity, and voices loaded during pause. Existing audio, rumbler, six-tone, extra, modifier, and save/reload checks remain. Native vehicle-extra calls and menu behavior still require in-game verification.

## Build on Windows

Open a Visual Studio Developer Command Prompt with MSBuild, C# build tools, and the .NET Framework 4.8 targeting pack available. From this folder, run:

```bat
build.cmd
```

The script builds the plugin, builds the regression test executable, and runs the checks. It stops on the first failure. The plugin output is:

```text
bin\Release\CustomELSSirens.dll
```

You can also open `CustomELSSirens.csproj` in Visual Studio and select **Release / Any CPU**. All DLL references now point into the supplied `dependencies` folder; no NuGet packages or machine-specific GTA paths are required. The test project is `Tests\RegressionTests.csproj`.

## Install the built plugin

1. Unload the old plugin or close GTA V before replacing its DLL.
2. Copy the newly built `CustomELSSirens.dll` into the game's `Plugins` folder, replacing the previous plugin.
3. Keep the existing matching NAudio and RAGENativeUI runtime DLLs available to the game. The supplied dependencies are unchanged. For the original layout, NAudio DLLs are in the GTA V root; retain that layout.
4. `RagePluginHookSDK.dll` is a build reference, not a runtime plugin. It is intentionally excluded from the build's copied runtime files.
5. Keep your existing audio and profiles in `Plugins\CustomSirens\WAVs` and `Plugins\CustomSirens\Profiles`. `Config.ini` remains in `Plugins\CustomSirens`.

The menu key remains F10 by default. It now toggles the menu on a fresh key press, including when a submenu is open. Siren controls are suspended while the plugin menu or game pause menu is open.

## Six tones and rumbler setup

1. Enter the vehicle, open **F10**, and select **Profile Mode > Vehicle Specific**.
2. Select **WAV set to edit > Normal / rumbler OFF**. Assign Tone 1 through Tone 6, Airhorn, an optional Manual siren, and an optional FIAMMS WAV. Each has its own volume.
3. Select **WAV set to edit > Rumbler ON** and assign the alternate WAVs and volumes. Switching this selector keeps edits to both banks until you save. An unassigned, empty, or missing alternate WAV falls back to the normal WAV for that slot. An omitted rumbler volume defaults to that slot's normal volume.
4. Check **Enable rumbler for this profile**, then choose **Save Profile**. This saves both banks, including the one currently hidden. Changing profile mode, reloading, or closing/reopening the menu discards unsaved WAV selections.
5. Use **Rumbler active in current vehicle** in the menu, or close the menu and press **F11**, to toggle the saved banks. The selected siren restarts with the new bank. Toggling while the siren is off selects the bank for its next activation.

The rumbler toggle starts OFF and is remembered for each individual vehicle during the plugin session. Two vehicles of the same model share their saved profile but can have different current rumbler states. Disabling rumbler support returns that vehicle to the normal bank. A rumbler switch while the horn is held keeps the main siren stopped until release.

There are six selectable main-siren slots per bank. Tone cycling, manual fallback, and AI selection all include slots 5 and 6 and skip unassigned slots. If Manual is unassigned, it uses the next available main tone. Existing four-tone profiles remain valid; new slots default to None. Light-stage tracking remains a separate 1–4-stage setting.

## FIAMMS and horn cycling

Assign **FIAMMS** and its volume in the vehicle's normal WAV bank, then **Save Profile**. Use **FIAMMS active in current vehicle** in the menu or **F9** outside the menu to toggle it. The FIAMMS voice loops independently of the selected main tone: main tone changes, horn use, manual use, and switching the main siren off do not mute or stop FIAMMS. It can also play by itself. It follows the vehicle's siren light restriction and automatic driver-exit cutoff, and stops when changing vehicles, reloading profiles, or unloading the plugin.

FIAMMS has its own `[Sirens] FIAMMS` filename and `[SirenVolumes] FIAMMSVol` value. An optional `[RumblerSirens] FIAMMS` and `[RumblerVolumes] FIAMMSVol` select its alternate WAV/volume when rumbler is on. An unassigned alternate falls back to the normal FIAMMS WAV. Setting FIAMMS to None in both banks leaves it unavailable. Its toggle key is `Toggle_FIAMMS`, with the same per-vehicle/global override rules as rumbler.

Enable **Misc Settings > Horn Cycles Siren**, or set `[Settings] HornCyclesSiren=true` in Config.ini, to advance the active main siren once per horn press. It uses the same six-slot cycling rule as `Snd_SrnTonX`, skips empty slots, and wraps around. With `HornInterruptsSiren=true`, the horn stops the main voice and release starts the newly selected tone from the beginning. With interruption disabled, the tone changes immediately while the horn plays. The horn still sounds normally, FIAMMS stays independent, and an inactive main siren stays off. Horn cycling defaults to false.

`Snd_SrnTonX` remains available (default number-row 6, or your ELS binding). `Snd_SrnPnic` and `Snd_SrnScan` are no longer read, written, imported, or processed; old INI entries can be deleted. Player auto-scan is removed. Pressing a main tone key toggles that tone on/off; the controller's D-pad Down still toggles the main siren on/off using the first configured tone. D-pad Right cycles it. Nearby AI keep their existing automatic tone rotation.

## Game pause

Pausing GTA V, opening its pause menu, loading, or setting the game time scale to zero pauses every custom voice: main sirens, FIAMMS, horns, manual, rumbler-bank playback, and AI. The output returns silence before reading the mixer, preserving WAV positions, reverb buffers, and gain ramps. A WAV that finishes loading during pause waits at its beginning. Fade progress and AI tone deadlines are preserved if the game clock advances while paused. On resume, held/released controls are reconciled before audio resumes; the existing output device is reused.

The plugin's F10 configuration menu retains its existing mute behavior. Game pause takes priority while that menu is open. This plugin controls its own audio; GTA's native audio follows the game's pause behavior.

## New keybinds and vehicle extras

Add the new keys under `[Keybinds]` in `Plugins\CustomSirens\Config.ini`. `Examples\Config-keybinds.ini` contains mergeable additions. These new keys are read even when `UseElsKeybinds=true`.

| Action | INI key | Default |
| --- | --- | --- |
| Tone 5 | `Snd_SrnTon5` | `D8` (number-row 8) |
| Tone 6 | `Snd_SrnTon6` | `D9` (number-row 9) |
| Toggle rumbler | `Toggle_Rumbler` | `F11` |
| Toggle FIAMMS | `Toggle_FIAMMS` | `F9` |
| Red beacon | `Toggle_RedBeacon` | `None` |
| Matrix text 1 | `Toggle_MatrixText1` | `None` |
| Matrix text 2 | `Toggle_MatrixText2` | `None` |
| Matrix text 3 | `Toggle_MatrixText3` | `None` |

Tone 5/6 defaults remain compatible with the previous release. Key values use Windows Forms names, such as `B`, `NumPad1`, or `Control, B`. Modifier chords require the exact Ctrl/Shift/Alt combination. `None` leaves a control unbound.

Assign extra IDs in the **Vehicle Specific** menu, or under `[VehicleExtras]` in that vehicle model's profile:

```ini
[VehicleExtras]
RedBeacon=1
MatrixText1=2
MatrixText2=3
MatrixText3=4

[Keybinds]
Toggle_Rumbler=F11
Toggle_FIAMMS=F9
Toggle_RedBeacon=Control, B
Toggle_MatrixText1=Control, NumPad1
Toggle_MatrixText2=Control, NumPad2
Toggle_MatrixText3=Control, NumPad3
```

Those extra IDs are examples: replace them with IDs actually present on your model. An omitted ID or `-1` disables the option; extra 0 is valid. IDs must be unique and within 0–255. Invalid IDs and later duplicate mappings are disabled with a console message; a mapped extra missing from the actual vehicle is left alone. Extra IDs are only read from the vehicle-specific profile. Global IDs are never applied to unrelated models.

The beacon toggles independently. Turning on a matrix text turns off the other configured matrix texts; pressing the active text again turns it off. These options toggle existing vehicle model extras, so the model must already contain the beacon and text meshes. Choose extras that ELS does not continually control. Feature keys act only while occupying a live vehicle and are suspended in menus/pauses.

Rumbler, FIAMMS, and extra key precedence is **vehicle profile > Global.ini > Config.ini**. A saved `None` overrides a global key; remove the profile entry to inherit it. Save Profile adds missing feature-key entries for convenient editing. Edit these bindings in the INI and use **Reload Configurations** afterward. Tone keys remain in Config.ini (the original four can still be imported from ELS).

`Examples\VEHICLE.ini` is a complete profile template with both banks, volumes, extra IDs, and keybinds. WAV names are placeholders; copy in your own sounds. No example configuration is installed automatically.

## Audio behavior

- Mono/stereo WAVs are decoded and converted to 44.1 kHz mono once on the worker, then panned into the stereo mixer. Common PCM bit depths and different sample rates are supported. The original AudioFileReader decoder is retained; compressed formats depend on the codecs available on Windows. Unsupported codecs and multichannel surround files produce a console message; mono/stereo PCM WAV is the most portable choice.
- WAV decoding is requested in advance when entering a vehicle, including the rumbler bank when enabled. The first uncached tone can take a moment to become audible while loading, without waiting on the game fiber. Releasing/stopping it while loading cancels the pending playback.
- Normal tone changes and rumbler switches reuse the audio output. With `HornInterruptsSiren=true` (default), pressing the horn cancels the main voice; release starts the selected tone from sample zero. Changing the selected tone while holding the horn changes which tone restarts. Turning the siren off while holding the horn prevents a restart. Set this option false to mix the horn and main siren together. Manual interruption retains its existing mute/resume behavior.
- Loop crossfades and short gain ramps reduce boundary/start clicks. The final mixed signal is clamped to the output range to prevent overflow when many sirens overlap.
- Empty, missing, corrupt, or unsupported WAVs fail once per cache lifetime and produce a console message. They do not spin in the audio callback or retry every frame.
- Decoded cache retention is limited to 256 MiB; least-recently-used cached arrays are released. Active voices retain their own array references until stopped. Each WAV is limited to 120 seconds; active voices and temporary decoding buffers are additional memory.
- The output callback also suspends mixer reads when the game heartbeat is more than one second old, covering a stalled game fiber. There is no background timer iterating the AI collection. An unavailable/stopped output device is retried on the worker at five-second intervals.

`Reload WAV Files` stops current voices, invalidates the decoded sound handles and profile snapshots, and rescans files. Use it after replacing or repairing a WAV. `Reload Configurations` reloads settings/keybinds and invalidates profile/ELS snapshots. Saving or reloading profiles stops existing custom tones so the next activation uses the new selections.

## In-game acceptance checks

After a successful Windows build and regression run:

1. Enter an emergency vehicle and activate all six configured tones repeatedly, including their first use. Check frame pacing, Tone 5/6 keys, manual fallback, tone cycling, controller on/off, and a profile with only Tone 6 assigned. Player tones should stay selected until you change them.
2. Hold the horn during a recognizable part of a siren WAV. Confirm the siren stops and restarts at its beginning on release. Change tone or turn the siren off while holding the horn, then release. Check native and custom horns, plus `HornInterruptsSiren=false` mixing.
3. Hold a horn/manual key briefly while a previously unused file loads, then release it. Confirm it does not start later after release.
4. Assign distinct normal/rumbler files and volumes in both menu banks, save, and reload. Toggle with the menu and key, including during a held horn and with an unassigned alternate slot. Check two vehicles of the same model keep independent rumbler states.
5. Map real extras and keys. Check beacon independence, all three matrix texts, and pressing the active text to turn it off. Test disabled, nonexistent, and duplicate mappings. Confirm feature keys do nothing on foot, in menus/pauses, or on unconfigured vehicles.
6. Check multiple nearby AI units, enter a vehicle already tracked as AI, leave the vehicle, and delete/despawn a sounding vehicle. Check for duplicate or orphaned audio and AI use of Tone 5/6.
7. Open the plugin menu during a fade, pause, and resume. Confirm mute/fade behavior and menu key toggling. Hold a feature key across menu closure or vehicle entry and confirm it requires a fresh press.
8. Change global and vehicle-specific volumes, including manual and rumbler volumes. Save/reload and check selections again. Replace a WAV using the same filename and confirm Reload WAV Files uses its new contents.
9. Test an empty/corrupt WAV and inspect `[CustomSirens]` console messages. Repair it and use Reload WAV Files before retrying.
10. Unload/reload the plugin and confirm sound stops. If practical, disconnect/reconnect the output device and check recovery.
11. Assign FIAMMS, save, and toggle it with F9 and the menu. Check it layers with every main tone and continues during horn/manual use and main-siren off. Check its rumbler WAV/volume, light restriction, driver-exit cutoff, and cleanup on vehicle change/reload. Toggle it off while an uncached WAV is loading and check it does not start later.
12. Enable Horn Cycles Siren. Tap, hold, and release the horn through all configured tones, including wraparound and unassigned slots. Check both interruption settings and a main siren that is off. Hold the horn across menu closure, vehicle entry, or pause/resume and check there is no extra cycle.
13. Play main + FIAMMS with nearby AI, then pause for several seconds. Confirm all custom audio becomes silent and resumes from the same positions. Repeat with horn/manual held, release them during pause, and resume. Test pausing while a new WAV loads and while the F10 menu is open.

The ELS VCF patch action remains manual. Backups now preserve the source subfolder structure, preventing same-named VCF files from sharing a backup path. Existing backups are not overwritten.

## Reference

The resampling choice follows NAudio's managed [resampling documentation](https://github.com/naudio/NAudio/blob/main/Docs/Resampling.md). Runtime dependencies remain the binaries supplied with this project.
