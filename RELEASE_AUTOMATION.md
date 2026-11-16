# Quantum Conveyors release automation

This repository includes a GitHub Actions workflow that can build a release on
the Windows PC with Moon Sweepers installed and upload it to the Quantum
Conveyors Nexus Mods page.

## One-time setup

1. In the GitHub repository, open **Settings > Actions > Runners** and add a
   **new self-hosted runner** for Windows x64.
2. Install the runner on the same PC that has the Moon Sweepers game with BepInEx and
   MoonFramework installed. Give the
   runner the custom label `moonsweepers`.
3. Add these repository variables under **Settings > Secrets and variables >
   Actions > Variables**:

   - `MOONSWEEPERS_GAME_ROOT`: the full Moon Sweepers install path (the build
     compiles against its BepInEx and MoonFramework).
   - `NEXUSMODS_FILE_ID`: the file ID for the main Quantum Conveyors file on
     Nexus. This is visible in Nexus file management/advanced details.

4. Add a repository secret named `NEXUSMODS_API_KEY`. Use a Nexus API key with
   permission to upload to the Quantum Conveyors mod page. Never commit this
   key to the repository.

The workflow uses Nexus mod-page ID `68` for Quantum Conveyors. This is separate
from `NEXUSMODS_FILE_ID`: the file ID identifies the existing downloadable file,
while `68` identifies the mod page used for changelog updates.

## Publishing a release

1. Update `PluginVersion` in `src/QuantumConveyors.Plugin/Plugin.cs`.
2. Add the matching version section at the top of `CHANGELOG.md`.
3. Commit and push those changes.
4. Create and publish a GitHub Release whose tag matches the version, for
   example `v2.0.0`.

The `release-nexus.yml` workflow checks out the release tag, builds the plugin,
creates the normal `BepInEx/plugins/QuantumConveyors` archive, and uploads it to
Nexus. It also sends the matching changelog section to Nexus.

The workflow can also be started manually from **Actions > Build and publish
Quantum Conveyors > Run workflow**. Manual runs are useful for testing the
runner and packaging without publishing a GitHub Release first.

## Why the workflow uses a self-hosted runner

Quantum Conveyors references Moon Sweepers' generated IL2CPP interop assemblies
and the local BepInEx runtime. Those files should not be committed to or
redistributed from this repository, and a normal GitHub-hosted runner does not
have them. The self-hosted runner keeps those private local build dependencies
on the development PC while letting GitHub handle the repeatable release and
Nexus upload steps.
