# Security policy

## Supported versions

Only the [latest release](https://github.com/avistry/avistry/releases/latest) gets fixes. Please update before reporting.

## Report a vulnerability

Please **do not** open a public issue for a security problem.

Report it privately through GitHub: [Security > Report a vulnerability](https://github.com/avistry/avistry/security/advisories/new). Include the version, what an attacker could do, and the steps to reproduce it. You should hear back within 7 days.

## What the package does and does not do

Avistry is an editor-only Unity package. Knowing its scope helps you judge a report:

- It runs only inside the Unity Editor. No code from it is uploaded with an avatar.
- It collects no telemetry. Its only network call is an optional, off-by-default update check that asks GitHub's public API for the latest version number and sends nothing about you or your project.
- It reads and writes files only inside your Unity project. Saving always creates new files; it never overwrites your original textures.
- It stores a few preferences (colour sets, brush presets) in Unity's EditorPrefs on your computer.

## Verifying a download

- Every release lists SHA-256 checksums for its files. Check a download with `certutil -hashfile <file> SHA256` (Windows) or `sha256sum <file>` (macOS and Linux).
- Each release is scanned on VirusTotal, and the release notes link to the reports. Editor scripts can occasionally get one or two false positives from heuristic scanners. If a result worries you, open an issue with the report link.
- The source is public, and the release files are built from it by GitHub Actions.
