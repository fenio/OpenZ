# Unofficial macOS and Linux builds

These community builds are not official OpenZ releases. The existing Windows build commands are unchanged.

## Running the downloads

Downloads: [unofficial community release](https://github.com/fenio/OpenZ/releases/tag/v1.1-unofficial.1).

- **macOS Universal:** extract the ZIP, then open `OpenZ.app`. It runs natively on Apple Silicon, unlike upstream's Intel-only experimental Mac build. Apple Silicon gameplay has been tested locally; the Intel architecture is included but not runtime-tested. This community build is not notarized. Use macOS's per-app approval procedure if needed; do not disable Gatekeeper globally.
- **Linux x86-64 (experimental):** extract the `.tar.gz`, preserving permissions, open a terminal in the extracted directory, and run `./OpenZ`. A successful build is not a guarantee of compatibility: Linux gameplay has not yet been tested. Please report results to [the community fork](https://github.com/fenio/OpenZ/issues), not as official upstream platform support.

Both default to native-resolution fullscreen. High-DPI/Retina displays can therefore be much slower than a smaller Unity editor Game view. To run at 1920 × 1080 fullscreen, quit any running copy first, then run from the extracted directory:

```sh
# macOS
open ./OpenZ.app --args -screen-fullscreen 1 -screen-width 1920 -screen-height 1080

# Linux
./OpenZ -screen-fullscreen 1 -screen-width 1920 -screen-height 1080
```

Substitute your preferred width and height; use `-screen-fullscreen 0` for a window. Fullscreen behavior may vary by platform/display. No separate launcher script is needed. Enable **OPTIONS → FPS COUNTER** to compare performance in the same map and view.

Verify the downloaded archive against its adjacent `.sha256` file with `shasum -a 256 -c FILE.sha256` on macOS or `sha256sum -c FILE.sha256` on Linux. Binary packages include per-file checksums, licenses (`LICENSES.txt`, `LICENSE_ASSETS.txt`), credits, and `LAUNCHING.txt`. These releases contain binaries only. The game is built unmodified from the [upstream OpenZ 1.1 source archives](https://github.com/playopenz/OpenZ/releases/tag/v1.1); the only addition is the editor-only portable builder in this repository (see [Prerequisites](#prerequisites)).

## Prerequisites

- Unity 6.3 LTS (6000.3.25f1 or a later 6000.3 patch), with an activated license.
- The **complete project**: extract both source archives from the same upstream release into one directory. This Git checkout has no scenes or game assets.
- Copy `source/Assets/OpenZ/Scripts/Editor/OpenZPortableBuild.cs` **and its `.meta` file** from this checkout into the complete project's `Assets/OpenZ/Scripts/Editor/` directory.
- macOS builds: a Mac with the macOS build support installed. The default output is Universal (Intel and Apple Silicon).
- Linux builds: Linux Build Support matching the project's scripting backend (Mono or IL2CPP); IL2CPP additionally needs a supported native compiler/toolchain. Output is Linux x86-64. Linux builds can also be made from the Mac editor with the appropriate module installed.

Open the complete project once, allow package import/compilation to finish, and test `Assets/OpenZ/Scenes/MainMenu.unity`. Do not build while another editor has the project open.

## Editor menus

Use **OpenZ → Unofficial → Build macOS release** or **Build Linux release**, and select a parent output directory. Each command creates a new timestamped release directory.

The builder includes runtime shaders, uses the `OZ_RELEASE` define and existing release assembly filter, sets the company to OpenZ, and restores temporary player settings afterwards. It builds MainMenu first, then the three maps and Intro. Unlike the Windows script, it does not change editor PlayerPrefs. Build stamping and shader inclusion still update project assets, as the existing tools do.

## Command line

For example, on macOS (adjust all paths and the editor version):

```sh
UNITY="/Applications/Unity/Hub/Editor/6000.3.26f1/Unity.app/Contents/MacOS/Unity"
PROJECT="$HOME/Games/OpenZ_1.1_source"

"$UNITY" -batchmode -quit -projectPath "$PROJECT" \
  -buildTarget OSXUniversal \
  -executeMethod OpenZ.EditorTools.OpenZPortableBuild.Build \
  -ozPlatform macos -ozArchitecture universal \
  -ozOutput "$HOME/Games/builds/OpenZ-unofficial-macos-1.1" \
  -logFile "$HOME/Games/openz-macos-build.log"
```

Use `-ozArchitecture arm64` or `x64` to build a single macOS architecture.

For Linux, use the same editor entry point (either on a Mac or on Linux):

```sh
"$UNITY" -batchmode -quit -projectPath "$PROJECT" \
  -buildTarget Linux64 \
  -executeMethod OpenZ.EditorTools.OpenZPortableBuild.Build \
  -ozPlatform linux -ozArchitecture x64 \
  -ozOutput "$HOME/Games/builds/OpenZ-unofficial-linux-1.1" \
  -logFile "$HOME/Games/openz-linux-build.log"
```

On Linux, set `UNITY` to the installed editor executable. `-buildTarget` selects the platform before compilation; keep it consistent with `-ozPlatform`.

Exit status is 0 on success, 1 on builder failure; Unity startup/license errors also fail the process. Output directories must not already exist and must be outside `Assets/`. Failed builds are left for inspection, not silently overwritten. Build reports and external debug-symbol directories are siblings of the release directory and should not be distributed.

## Test and distribute

Neither platform is validated merely by a successful compilation. Test menu/preview video, all three levels, custom effects/shaders, audio, controls, fullscreen/Retina behavior, preferences, and performance on each supported architecture. The builder explicitly selects Metal on macOS; Linux uses the project's default graphics API configuration. Smoke-test on real Linux hardware before advertising Linux support.

- Label downloads **unofficial**, identify the upstream version and your fork commit, and keep upstream support separate.
- Include `LICENSES.txt`, the asset license and credits from `docs/` in each binary package, and point to the corresponding source: the upstream release's source archives plus this repository's builder and build instructions. See `LICENSE` and `docs/LICENSE-ASSETS.md`; asset redistribution is non-commercial and share-alike.
- The builder writes `SHA256SUMS.txt`. If you sign/notarize or otherwise alter files after building, regenerate it afterwards.
- Sign/notarize macOS distributions as appropriate; this builder does not provide signing credentials or perform notarization. Do not tell users to disable Gatekeeper globally.
- Archive the **whole release directory**, not just the executable. On Linux use `tar` to preserve executable permissions; on macOS preserve `.app` symlinks and permissions (for example with `ditto`).

Example packaging:

```sh
ditto -c -k --sequesterRsrc --keepParent "$HOME/Games/builds/OpenZ-unofficial-macos-1.1" "$HOME/Games/builds/OpenZ-unofficial-macos-1.1.zip"
tar -czf "$HOME/Games/builds/OpenZ-unofficial-linux-1.1.tar.gz" -C "$HOME/Games/builds" OpenZ-unofficial-linux-1.1
```

## Future CI

A CI job can invoke exactly these entry points. It must first download and verify the full-project release archives, overlay the chosen source revision without discarding `.meta` files, activate Unity using an appropriate license, and install the matching modules. Use separate workspaces for each target (the build stamp modifies assets). Pin the editor and source archive checksums, retain logs, and upload artifacts for testing before adding automatic release publishing. CI workflows and public publishing are intentionally not included yet.
