# Contributing to Avistry

Thank you for helping. Bug reports, feature requests, translations and code are all welcome.

## Report a bug

Open a [bug report](https://github.com/avistry/avistry/issues/new?template=bug_report.yml). The most useful things to include:
- the tool's version (shown under the **?** button in the window) and your Unity version
- the shader (Poiyomi, lilToon or other)
- the exact steps, and the Console error if there is one
- the **Copy diagnostics** text, once your version has it. It contains no file paths or user names.

Please do not paste file paths, your Windows user name, or avatar files you are not allowed to share.

## Ask for a feature

New features are discussed with the maintainers on our [Discord](https://discord.gg/PM8uC2UnT) first, so the work fits the plan and nobody builds the same thing twice. You are welcome to join. Then open a [feature request](https://github.com/avistry/avistry/issues/new?template=feature_request.yml) and pick the idea from the list, or "Something else".

We plan with real demand: a planned idea moves up when enough people ask for it. A 👍 on an existing request counts too, so search before opening a new one.

## Translate

The README is available in English, [日本語](i18n/README.ja.md), [한국어](i18n/README.ko.md) and [简体中文](i18n/README.zh-CN.md). The landing page's text lives in `Website~/`. Corrections by native speakers are very welcome.
- Use the words the tool itself uses (a mesh is a *part*, an island is a *piece*) so the README matches the tool.
- Keep the structure of the English README, so the files stay easy to compare.
- Do not translate the names in the language row at the top.

## Change the code

### Set up
1. Make a VRChat avatar project with Unity 2022.3 (the VRChat version) in the Creator Companion or ALCOM.
2. Clone your fork into the project's `Packages/` folder:
   ```sh
   git clone https://github.com/<you>/avistry.git Packages/com.avistry.toolkit
   ```
   Unity loads it as an embedded package and recompiles when you save a file.
3. Open **Tools > Avistry > Textures**.

### AI help

AI help is welcome; slop is not. You stay responsible for what you submit: read the code, try it in Unity, and make sure you understand it. A good contribution has intent and taste and fits the vision in the [README](../README.md#why-avistry). Questions and ideas are welcome on our [Discord](https://discord.gg/PM8uC2UnT).

### Before you open a pull request
- [ ] Unity compiles with **no errors and no warnings** from this package.
- [ ] Every new file or folder has the `.meta` file Unity made, committed with it. Moved files keep their `.meta` (use `git mv` on both). Never change a GUID.
- [ ] You tried the change by hand, with Poiyomi or lilToon if it touches materials.
- [ ] UI changes have before/after screenshots in Unity's light and dark skins.
- [ ] UI text uses the tool's own words (a mesh is a *part*, an island is a *piece*).
- [ ] Comments say why, not what; new types and members are `internal` by default; renamed serialised fields keep `[FormerlySerializedAs]`.
- [ ] A line under `## [Unreleased]` in [CHANGELOG.md](../CHANGELOG.md) for anything a user would notice. It becomes part of the release notes, so write it for users, in plain words. A check on the pull request fails when `Editor/` or `package.json` change and no line was added; if users will not notice the change, ask a maintainer for the `no-changelog` label.

### What happens next

The maintainers review every pull request, test it in Unity with their own test kit and release it properly. We may ask for changes first. The project is AI-assisted and human-reviewed: an experienced C# developer reads and checks the code ([how this was made](../README.md#how-this-was-made)).

Keep pull requests small and about one thing. For a large change, open an issue first so we can agree on the approach.

### Ground rules
- **Never overwrite the user's files.** Saving makes new files. This is the tool's main promise.
- **Editor only.** Nothing may end up in the uploaded avatar.
- **No telemetry, and no network access except the opt-in update check** (`Editor/UpdateCheck.cs`, a public GitHub API call).
- Poiyomi, lilToon and NDMF are optional. The package must compile and work without them.

## Licence

By contributing, you agree that your contribution is released under the same licence as the project ([LICENSE](../LICENSE)).
