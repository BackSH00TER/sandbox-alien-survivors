# Compile warning audit

Baseline: `origin/main` at `f41d5fe` (`game: Use Oxanium for HUD and lobby UI`).

## Results

Both builds succeeded with zero errors.

| Scope | Command | Warnings |
|---|---|---:|
| Game code | `dotnet build Code\alien_survivors.csproj --no-restore` | 3 |
| Editor and dependencies | `dotnet build Editor\alien_survivors.editor.csproj --no-restore --no-incremental` | 9 |

The editor build includes the three game-code warnings below and six warnings from the installed s&box `Base Editor Library` outside this repository.

## Project warnings

| Diagnostic | Location | Finding | Suggested follow-up |
|---|---|---|---|
| CS0612 | `Code/game/GamePlayerHeldWeaponModel.cs:34` | `Model.GetAttachment(string)` is obsolete. | Identify the supported attachment lookup API and migrate the muzzle lookup. |
| CS1574 | `Code/lobby/LobbyManager.cs:6` | The XML `cref` for `PlayerReadyState` cannot be resolved. | Correct the reference to the declared type or use plain text if it is not linkable. |
| CS0419 | `Code/shared/PlayerOutfit.cs:5` | `Dresser.Apply` is ambiguous between overloads. | Qualify the intended overload in the XML `cref`. |

## Installed engine warnings

The cold editor build reported six CS1573 warnings at `PublishSection.cs:50` in the installed s&box `Base Editor Library.csproj`. The XML comment for `PublishSection.PublishSection(Widget, string, Color, string, string, int?, Pixmap)` is missing parameter tags for `parent`, `title`, `accent`, `icon`, `count`, and `iconImage`. These files are outside the repository; address them upstream rather than changing the local project.

No warning fixes were made as part of this audit.
