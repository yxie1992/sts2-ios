# Local iOS build and maintenance notes

Recorded after a successful personal-device installation on 2026-10-14. Read
[AGENTS.md](../AGENTS.md) first. These notes supplement the normal workflow with
observations from this build; they do not contain game assets or signing values.

## Verified environment and result

| Component | Tested version or configuration |
| --- | --- |
| Xcode / iPhoneOS SDK | Xcode 27.0 (27A266a), SDK 27.0 |
| .NET | SDK 9.0.319, shared runtime 9.0.21, iOS workload 18.0.9617/9.0.100 |
| Godot and export templates | 4.5.1 stable **mono**, engine commit `f62fdbde1` |
| Game | User's installed macOS arm64 Steam copy; exported app version 0.107.1 |
| FMOD | Official iOS SDK 2.03.06; GDExtension tag `6.1.0-4.5.0` |
| Spine | Official 4.2 runtime, built against the exact Godot 4.5.1 API |
| Native extensions | Device arm64, minimum iOS 15.0 |
| Device | iPhone 17 Pro, iOS 26.6.2 |

The native app launched, completed shader warmup, played combat, saved progress,
and handled background/resume. The user confirmed smooth gameplay after initial
stuttering, then confirmed the desktop save import worked. This is evidence for
this combination, not a guarantee for future game or SDK versions.

All 37 patch hooks matched. The validated content pack had 672 script
placeholders, 12,999 entries, no active Sentry extension/autoload, and a size of
1,901,186,484 bytes. The installed app without that pack was about 166 MiB.
Treat these as diagnostics for this game version, not permanent expected sizes.

## Toolchain and native-library preparation

- Installing Xcode did not change the system's selected Command Line Tools.
  The ignored `ios-export/config.sh` sets
  `DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer`. Use this environment
  for `xcodebuild` and `xcrun`; do not assume a successful bare `xcrun` selects
  the intended Xcode.
- Set `DOTNET_ROOT` and prepend `STS2_DOTNET_ROOT` to `PATH`. This build used
  `$HOME/.dotnet`, including the iOS workload. The installed workload completed
  NativeAOT successfully with this Xcode; no speculative workload upgrade was
  necessary.
- The editor and templates must both be the mono 4.5.1 release. The editor alone
  is insufficient; the template directory must contain `ios.zip`.
- FMOD requires the **iOS Engine SDK**, including both Core and Studio static
  libraries. Build the GDExtension and Spine against the API dumped from the
  actual 4.5.1 editor. A similarly named upstream tag is not proof of ABI parity.
- Native libraries must be device arm64/iOS binaries, not simulator binaries.
  Check platform/minimum OS, exported entry symbols, dependencies, and install
  names with `vtool`, `nm`, and `otool`. Both extensions worked with minimum iOS
  15.0; the Godot export preset now uses the same minimum.
- Local vendor rebuild helpers remain outside the repository at
  `$HOME/sts2-ios-userlibs/build-fmod-ios.sh` and `build-spine-ios.sh`. They pin
  source revisions and use private Python/SCons environments. Keep those local
  helpers and official SDK downloads when maintaining this machine. See
  [THIRD_PARTY_LIBS.md](THIRD_PARTY_LIBS.md) for upstream sources and requirements.

## Build fixes now in the repository

1. **Create `game-refs/` on a fresh checkout.** The patch project needs the user's
   current `sts2.dll` and `GodotSharp.dll`. `build-ios.sh` now copies these into
   the ignored reference directory before building patches.
2. **Resolve dependencies beside the original assembly.** An isolated copy of
   `sts2.dll` caused Cecil to fail resolving adjacent dependencies such as
   Sentry. The weaver reads the original assembly with `ReadWrite=false` and
   writes the woven output to `.work/`. Original DLL hashes were verified
   unchanged. Third-party managed dependencies are retained; assemblies supplied
   by the installed .NET shared runtime are excluded from `prebuilt/deps`.
3. **Include a solution and an original installation scene.** `sts2.sln` supplies
   Godot's expected Debug/ExportDebug/ExportRelease mappings. The small
   `scenes/game.tscn` is an installation status screen, not extracted game content.
4. **Extension descriptors are required.** Framework files alone did not make
   Godot collect the extensions. The build extracts the original FMOD/Spine
   `.gdextension` descriptors from the user's PCK into ignored `addons/` and
   verifies discovery in `.godot/extension_list.cfg`. It also converts the user's
   app icon with `sips` into that ignored directory.
5. **Use recovery mode for import and export.** Loading desktop extensions during
   headless editor documentation generation crashed in `DocTools::_gen_extensions_docs`.
   `--recovery-mode` avoids loading them while preserving resource discovery and
   iOS library collection. The actual exported app contained both native
   extensions and the NativeAOT game framework.
6. **Keep exporter credentials private.** The build derives ignored
   `.godot/export_credentials.cfg` from `config.sh`, uses project-only export and
   ad-hoc signing during export, then restores Apple Development identities in
   the generated Xcode project. It saves/restores tracked `export_presets.cfg`
   around Godot invocations because the editor can rewrite it with local values.
7. **Check exporter status, not just existing files.** A stale Xcode project can
   survive an export failure. The build now checks the Godot process's exit code
   through the logging pipeline before accepting the output.

## First signing and the two different PCKs

The successful first setup ran weaving, NativeAOT, resource import, and export
in staged steps, followed by an unsigned Xcode build and the first signed GUI
build. It did **not** validate an untouched invocation of the entire final
`build-ios.sh` as a single end-to-end command. An ignored bootstrap stopped
before step 5.5 while signing setup was pending. Its temporary export-only team
placeholder was replaced with the real team before signing or installation.

The generated Xcode project initially needed **Automatically manage signing**
and **Increased Memory Limit** enabled. Verify the actual provisioning profile
and signed app both contain `com.apple.developer.kernel.increased-memory-limit`.
An entitlement file alone is not proof that provisioning permits it.

Do not infer `STS2_TEAM_ID` from the parentheses in the certificate display name:
that value can identify the person rather than the team. Read the selected
Xcode project's `DEVELOPMENT_TEAM` or the profile's `TeamIdentifier`. Keep all
personal configuration in ignored `config.sh`; generated projects and profiles
are also local artifacts, never source files to commit.

The connected phone must be unlocked, trusted, and in Developer Mode. Xcode may
need a selected physical destination before it can create the device profile.
macOS can ask for keychain access during both the first build and later command
line re-signing. The user enters the password in the system dialog. iOS may also
require trusting the Developer App under Settings → General → VPN & Device
Management. A locked phone can still allow file operations while refusing launch.

**The generated PBX project references `build/StS2.pck` directly.** Godot initially
writes the small installation-shell pack there, but step 5.5 replaces it with the
real patched game pack. Building in Xcode after replacement therefore embeds the
large game pack until `deploy-slim.sh` removes it. It is not automatically an
empty shell at that point, despite the simplified description in AGENTS.md.

For the small base used in this setup, the shell pack was kept through the first
signed Xcode build, and the validated game pack was copied into `build/StS2.pck`
afterward. Local copies are `.work/shell-base.pck` and `.work/game-content.pck`.
A future Godot export overwrites `build/StS2.pck`; restore/rebuild the patched game
pack before using `push-pck.sh`. Rebuilding from Xcode after that restoration can
embed the large pack again, so run `deploy-slim.sh` before considering it the final
app. Never uninstall to update or renew.

Deploy in order:

```sh
bash ios-export/deploy-slim.sh
bash ios-export/push-pck.sh
```

The installed app loads `--main-pack user://StS2.pck`; the real content lives at
`Documents/StS2.pck`. Match `STS2_BUNDLE_ID_SIGNED` to the actual installed bundle.
Free provisioning in this setup lasted seven days. Check the current profile's
expiration each time; USB deployment does not configure automatic renewal.
See [RENEWAL.md](RENEWAL.md) for the separate renewal workflow.

## Deployment and verification traps

- Standard Xcode output is at
  `DerivedData/<project>/Build/Products/<configuration>-iphoneos/StS2.app`.
  The discovery depth must be **5**, not 4. The unsigned test build under `.work/`
  is intentionally outside the deployment script's normal search path.
- Copying raw NativeAOT `sts2.dylib` into `Frameworks/sts2.framework/sts2` also
  requires correcting `LC_ID_DYLIB` from `@rpath/sts2.dylib` to
  `@rpath/sts2.framework/sts2` before signing. `deploy-slim.sh` now does this.
- On this macOS version, `codesign -d --entitlements -` prints a human-readable
  dictionary. Use `codesign -d --xml --entitlements - <app>` when parsing it as a
  plist. Verify the final staged app, including nested frameworks.
- With `set -o pipefail`, `devicectl ... | grep -q ...` can falsely fail when
  `grep` finds a match and closes the pipe while `devicectl` is still writing.
  The reproduced upstream exit was **141 / SIGPIPE**. The three deployment and
  transfer checks now capture complete output and its status before matching.
  A real device-query error must still fail even if its output contains the path.
- Check the transfer destination and byte size using `devicectl ... info files`
  JSON (`result.files[].relativePath` and `metadata.size`). Do not re-transfer a
  1.9 GB pack solely because a broken text pipeline reported failure.
- `devicectl device process launch --console` is useful for logs, but catchable
  signals to that command are forwarded to the app. Do not use Ctrl-C casually
  while the user is playing or a save is being written.

Useful startup log markers include `Skipping Steam initialization (mobile)`,
`[TimeScale]`, `[ShaderWarmup]`, and the pause-menu `[QuickRestart]`/`[Snapshot]`
messages. First-run warmup took roughly three minutes in this session while play
continued. It finished and wrote `Documents/shader_warmup_version`; later starts
skip it. Some disposed-material warnings were handled during warmup without
preventing completion. A successful build alone does not establish smooth play:
verify combat, audio/touch with the user, and saved progress on the device.

## Importing desktop progress without changing phone graphics

Read [SAVE_SYNC.md](SAVE_SYNC.md) and the actual `SyncImportPatch` implementation
before choosing a direction. Stop the desktop game before copying its save tree.

- For a requested **Mac → phone** import, use `push-save.sh`, not the bidirectional
  `sts2_save_sync.sh`. The latter can overwrite the Mac with a newer phone save.
- Despite a comment describing manual force-push, `push-save.sh` still uses the
  newest desktop file modification time. The importer only accepts an inbox
  directory timestamp **strictly greater** than every local phone save-file
  timestamp. An older incoming save is discarded. Here the desktop was newer,
  so no timestamp override was needed. Do not silently fake a newer timestamp
  during ordinary synchronization; replacing newer progress is a separate intent.
- The full desktop tree includes `settings.save` and its backup. Desktop values
  here included 16:9, MSAA 2, and adaptive VSync; the working phone used Auto,
  MSAA 0, and VSync On. The `.mobile_defaults_applied` marker is outside the tree
  being replaced, so mobile defaults will not automatically run again.
- To preserve working phone graphics, read only the phone's `settings.save` and
  `settings.save.backup`, push the desktop tree, then replace those two files in
  `Documents/sync_inbox/<timestamp>/1/` **before launching the game**. Keep the app
  closed throughout staging. This was the successful path in this session.
- A cold launch imports the staged directory into `Documents/default/1`. Confirm
  `[SyncImport] imported desktop save` in the console or `Documents/sync_log.txt`,
  then verify progression and history in the destination. The built-in importer
  retains replaced trees as `sync_replaced_*` (last two); a separate manual phone
  backup was skipped at the user's request.
- Compare parsed save data as well as hashes: launch can reserialize saves, add
  first-run tutorial completion, or set mobile telemetry preferences. Here run
  histories matched byte-for-byte; progression differed only in tutorial state,
  and the original Mac files and timestamps remained unchanged. There was no
  active desktop run to import, so the phone's test run was replaced rather than
  merged. Automatic two-way sync remains a separate opt-in task.

## Local evidence and future checks

The ignored `ios-export/.work/` directory contains `setup-state.json`, build/export
logs, `managed-check/fresh-check-report.json`, `pck-validation.json`, unsigned and
signed app validation reports, `pck-verification-check/report.json`, and the
`save-import-20261014/` transfer/semantic checks. These are machine-local evidence,
not portable source files. The preserved signed app is `.work/StS2-slim.app`;
its signature still expires with its embedded profile.

Validated checks included fresh reference creation, all 37 weave hooks, unchanged
original game files, recovery export and preset restoration on success/failure,
an unsigned arm64 Xcode build, signed device installation, content-pack structure,
and successful save import. Regression checks exercised large matching file
listings, missing paths, and failed queries that still contain a matching path.
For documentation or shell-check-only changes, use `bash -n` and `git diff --check`;
do not rerun the full export just to validate prose, since it regenerates the
working Xcode project and PCK.
