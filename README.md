# Custom ELS Sirens — 1.12.0.0

Extra assignments now use Disabled or IDs 1-12. Rumbler, FIAMMS, beacon and matrix keybinds are global in Config.ini, and DEBUG uses colored status text and bold emphasis. Horn cycling and horn interruption are configured per vehicle model. Six main tones, normal/rumbler WAV banks, independent FIAMMS, beacon/matrix extras, and true audio pause remain. One persistent stereo output mixes all custom voices; WAV decoding and audio-device work stay on a background worker. No vehicle or RAGE native calls run on that worker.

## Verification status

- All 15 C# source files passed parser syntax checks.
- Both project files are valid XML; every included source file and bundled dependency path resolves.
- The new NAudio API names were checked against the bundled DLL metadata strings. This is not a type-checked compilation.
- **Compilation, regression-test execution, and GTA V playback were not possible in the editing environment.** The local .NET runtime could not initialize, and GTA V/RAGE Plugin Hook are unavailable. No rebuilt plugin DLL is included.
- `Tests/` contains 28 regression checks against the actual audio/configuration/playback/debug source and bundled NAudio DLLs. It substitutes a fake output device and small RAGE test doubles, and does not play sound or load the game. Checks cover 1-12 extra assignment bounds, global feature keys and ignored legacy profile overrides, all four horn routes, per-model isolation of both horn settings, INI comment preservation, bounded extra discovery, debug text/styles, and voice status reporting. Existing audio, FIAMMS, rumbler, six-tone, pause, modifier, and save/reload checks remain. Native horn/extra behavior, menu behavior, and overlay rendering still require in-game verification.

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

FIAMMS has its own `[Sirens] FIAMMS` filename and `[SirenVolumes] FIAMMSVol` value. An optional `[RumblerSirens] FIAMMS` and `[RumblerVolumes] FIAMMSVol` select its alternate WAV/volume when rumbler is on. An unassigned alternate falls back to the normal FIAMMS WAV. Setting FIAMMS to None in both banks leaves it unavailable. Its global toggle key is `Toggle_FIAMMS` under Config.ini's `[Keybinds]` section.

For horn cycling, enter the vehicle, select **F10 > Profile Mode > Vehicle Specific**, choose **Horn Cycles Siren**, then **Save Profile**. The selector is disabled in Global Default mode. Each vehicle model stores its own choice under `[Settings] HornCycleMode` in its profile:

| Menu option | INI value | Behavior |
| --- | --- | --- |
| With horn (car horn) | `CarHorn` | Play the native car horn and cycle the main tone; suppress the custom siren-horn WAV. |
| With horn (siren horn) | `SirenHorn` | Play the profile's Airhorn/Horn WAV and cycle the main tone; suppress the native car horn. |
| Off | `Off` | Disable horn cycling. Keep ordinary horn behavior: custom Horn WAV if assigned, otherwise native horn. |
| Without horn | `Silent` | Suppress both horns and cycle the main tone immediately, without interrupting it while the key is held. |

Cycling happens once per horn press using the same six-slot rule as `Snd_SrnTonX`, skipping unassigned slots and wrapping around. In the audible modes, the vehicle's **Horn Interrupts Siren** setting controls timing: when enabled, the next tone starts from the beginning on release; when disabled, it changes immediately. FIAMMS stays independent, and an inactive main siren stays off. If SirenHorn mode has no usable Horn WAV, cycling is silent and a notification explains the missing assignment. Rumbler selects its alternate Horn WAV when one is configured.

Set **Horn Interrupts Siren** beside the cycle selector in **Vehicle Specific** mode, then **Save Profile**. This checkbox is also disabled in Global Default mode. It writes `[Settings] HornInterruptsSiren=true/false` in that model's profile and defaults to true if omitted. With cycling Off, it still controls whether ordinary horn use stops/restarts the main siren or plays alongside it. Silent cycling never interrupts the main siren.

Unconfigured or invalid modes default to Off. Neither horn setting is inherited from Global.ini or Config.ini. Old global `HornCyclesSiren` and `HornInterruptsSiren` entries are ignored and can be removed; set each vehicle profile's choices explicitly. Cars of the same model share the saved profile.

`Snd_SrnTonX` remains available (default number-row 6, or your ELS binding). `Snd_SrnPnic` and `Snd_SrnScan` are no longer read, written, imported, or processed; old INI entries can be deleted. Player auto-scan is removed. Pressing a main tone key toggles that tone on/off; the controller's D-pad Down still toggles the main siren on/off using the first configured tone. D-pad Right cycles it. Nearby AI keep their existing automatic tone rotation.

## DEBUG overlay

Toggle **F10 > Misc Settings > DEBUG** to enable or disable the overlay immediately. The global setting is saved as `[Settings] Debug=true/false` in `Plugins\CustomSirens\Config.ini` and defaults to false. It is independent of the selected profile and automatically hides when the player leaves the vehicle.

The panel appears in the upper-right corner and shows:

- Current vehicle model, audio output/pause/menu-mute state, and master volume.
- Selected main tone, siren horn, manual and FIAMMS playback states, including loading, muted, fading, and stopped-for-horn states, plus WAV filenames.
- Horn cycling mode, the vehicle's horn interruption setting, native horn input/suppression, rumbler ON/OFF, and FIAMMS ON/OFF.
- Vehicle siren flag, light restriction readiness, and tracked light stage.
- Actual ON/OFF states for the configured beacon/matrix extras, including unassigned/missing mappings, plus every discovered enabled extra ID on the current vehicle.

Bold cyan headings separate **AUDIO**, **CONTROLS**, and **VEHICLE & EXTRAS**. Active voices and toggles are green, inactive items gray, loading/paused/muted/interrupted states amber, and missing extras or an unavailable audio device red. Configuration details use lavender and WAV filenames use a softer blue-gray. Bold emphasis highlights active states and the current vehicle, and a darker panel improves contrast. State words remain visible alongside colors. Emphasis uses a narrow second text pass through the supplied drawing API; no font objects are created each frame.

The vehicle siren flag reports GTA's vehicle state; the car-horn row reports the native horn input/routing. These do not claim that audio controlled by another plugin is audible. Audio marked PLAYING may be globally paused or menu-muted; the Audio row shows that shared output state.

Status refreshes at most ten times per second. Mapped extras are checked immediately; discovery of the other possible IDs is spread across updates and briefly shows “scanning”. Only existing extras are polled after discovery. Text layout is reused while unchanged, and the render callback draws an immutable snapshot without calling natives or accessing entities. No extra discovery runs while DEBUG is disabled. Rendering errors hide the overlay and log once without stopping siren processing; toggle DEBUG off/on to retry.

## Game pause

Pausing GTA V, opening its pause menu, loading, or setting the game time scale to zero pauses every custom voice: main sirens, FIAMMS, horns, manual, rumbler-bank playback, and AI. The output returns silence before reading the mixer, preserving WAV positions, reverb buffers, and gain ramps. A WAV that finishes loading during pause waits at its beginning. Fade progress and AI tone deadlines are preserved if the game clock advances while paused. On resume, held/released controls are reconciled before audio resumes; the existing output device is reused.

The plugin's F10 configuration menu retains its existing mute behavior. Game pause takes priority while that menu is open. This plugin controls its own audio; GTA's native audio follows the game's pause behavior.

## New keybinds and vehicle extras

Add the new keys under `[Keybinds]` in `Plugins\CustomSirens\Config.ini`. `Examples\Config-keybinds.ini` contains mergeable additions. These new keys are read even when `UseElsKeybinds=true`.

Config.ini receives a comment linking to the [Windows Forms Keys fields](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.keys?view=windowsdesktop-10.0#fields) when loaded or saved. The global keybind example includes it too. Repeated loads/saves do not duplicate the comment.

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
```

Assign the global toggle keys in `Plugins\CustomSirens\Config.ini`:

```ini
[Keybinds]
Toggle_Rumbler=F11
Toggle_FIAMMS=F9
Toggle_RedBeacon=Control, B
Toggle_MatrixText1=Control, NumPad1
Toggle_MatrixText2=Control, NumPad2
Toggle_MatrixText3=Control, NumPad3
```

Those extra IDs are examples: replace them with IDs actually present on your model. The menu offers **Disabled** and **1-12**. An omitted ID or `-1` disables the option. IDs must be unique and within 1-12; 0, out-of-range values and later duplicate mappings are disabled with a console message. Invalid values are never clamped to a different extra. A mapped extra missing from the actual vehicle is left alone. Extra IDs are only read from the vehicle-specific profile. Global IDs are never applied to unrelated models. DEBUG can still report other native extras that are active on the vehicle.

The beacon toggles independently. Turning on a matrix text turns off the other configured matrix texts; pressing the active text again turns it off. These options toggle existing vehicle model extras, so the model must already contain the beacon and text meshes. Choose extras that ELS does not continually control. Feature keys act only while occupying a live vehicle and are suspended in menus/pauses.

Rumbler, FIAMMS, beacon and matrix keys are read **only from Config.ini** and apply to every vehicle. Set a binding to `None` there to leave it unbound. Old `[Keybinds]` entries in vehicle profiles and Global.ini are ignored and can be removed; Save Profile no longer writes them. If you previously customized these keys in a profile, put your preferred common bindings into Config.ini. Use **Reload Configurations** after editing. Tone keys also remain in Config.ini (the original four can still be imported from ELS).

`Examples\VEHICLE.ini` is a profile template with both banks, volumes, extra IDs and per-model horn options. `Examples\Config-keybinds.ini` contains the global feature bindings. WAV names are placeholders; copy in your own sounds. No example configuration is installed automatically.

## Audio behavior

- Mono/stereo WAVs are decoded and converted to 44.1 kHz mono once on the worker, then panned into the stereo mixer. Common PCM bit depths and different sample rates are supported. The original AudioFileReader decoder is retained; compressed formats depend on the codecs available on Windows. Unsupported codecs and multichannel surround files produce a console message; mono/stereo PCM WAV is the most portable choice.
- WAV decoding is requested in advance when entering a vehicle, including the rumbler bank when enabled. The first uncached tone can take a moment to become audible while loading, without waiting on the game fiber. Releasing/stopping it while loading cancels the pending playback.
- Normal tone changes and rumbler switches reuse the audio output. With the vehicle profile's `HornInterruptsSiren=true` (default), pressing an audible horn cancels the main voice; release starts the selected tone from sample zero. Changing the selected tone while holding the horn changes which tone restarts. Turning the siren off while holding the horn prevents a restart. Set this option false to mix the horn and main siren together. Manual interruption retains its existing mute/resume behavior.
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
5. Map real extras and global keys. Check Disabled and IDs 1/12 in the menu, beacon independence, all three matrix texts, and pressing the active text to turn it off. Test nonexistent/duplicate mappings and INI values 0/13; invalid assignments must stay disabled. Give two profiles different legacy keys and confirm both use Config.ini's bindings, including None and modifier chords after reload. Confirm feature keys do nothing on foot, in menus/pauses, or on unconfigured vehicles.
6. Check multiple nearby AI units, enter a vehicle already tracked as AI, leave the vehicle, and delete/despawn a sounding vehicle. Check for duplicate or orphaned audio and AI use of Tone 5/6.
7. Open the plugin menu during a fade, pause, and resume. Confirm mute/fade behavior and menu key toggling. Hold a feature key across menu closure or vehicle entry and confirm it requires a fresh press.
8. Change global and vehicle-specific volumes, including manual and rumbler volumes. Save/reload and check selections again. Replace a WAV using the same filename and confirm Reload WAV Files uses its new contents.
9. Test an empty/corrupt WAV and inspect `[CustomSirens]` console messages. Repair it and use Reload WAV Files before retrying.
10. Unload/reload the plugin and confirm sound stops. If practical, disconnect/reconnect the output device and check recovery.
11. Assign FIAMMS, save, and toggle it with F9 and the menu. Check it layers with every main tone and continues during horn/manual use and main-siren off. Check its rumbler WAV/volume, light restriction, driver-exit cutoff, and cleanup on vehicle change/reload. Toggle it off while an uncached WAV is loading and check it does not start later.
12. Save different horn modes and opposite Horn Interrupts Siren settings for two vehicle models. Check all four modes: native-only horn, WAV-only horn, ordinary horn without cycling, and silent immediate cycling. Verify custom/native horns do not leak into the wrong modes, including addon vehicles. Test both interruption settings, a missing Horn WAV, wraparound/empty slots, rumbler ON, and a main siren that is off. Hold the horn across menu closure, vehicle entry, or pause/resume and check there is no extra cycle. Reload and switch vehicles; confirm each model retains both of its own settings, both menu controls are disabled in Global Default mode, and DEBUG shows the current interruption choice.
13. Play main + FIAMMS with nearby AI, then pause for several seconds. Confirm all custom audio becomes silent and resumes from the same positions. Repeat with horn/manual held, release them during pause, and resume. Test pausing while a new WAV loads and while the F10 menu is open.
14. Enable DEBUG while driving and change tones, horn mode, rumbler, FIAMMS, volume, light stages and extras. Confirm the upper-right status follows the current vehicle, reports unmapped active extras, and refreshes after another plugin changes an extra. Check green active, gray inactive, amber paused/loading and red missing states, the section headings and bold emphasis. Exit, switch vehicles, disable DEBUG, pause/resume, and reload the plugin; check stale vehicle text is never left on foot. Check panel fit/readability at your game resolution and confirm unchanged frame pacing.

The ELS VCF patch action remains manual. Backups now preserve the source subfolder structure, preventing same-named VCF files from sharing a backup path. Existing backups are not overwritten.

## Reference

The resampling choice follows NAudio's managed [resampling documentation](https://github.com/naudio/NAudio/blob/main/Docs/Resampling.md). Runtime dependencies remain the binaries supplied with this project.

The overlay uses RAGE's documented [DrawText API](https://docs.ragepluginhook.net/html/M_Rage_Graphics_DrawText.htm) and [RawFrameRender event](https://docs.ragepluginhook.net/html/E_Rage_Game_RawFrameRender.htm); all native/entity reads stay outside that render callback, as required by the event documentation.
