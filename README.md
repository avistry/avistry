<h1 align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="Website~/media/lockup.svg">
    <img src="Website~/media/lockup-light.svg" height="64" alt="Avistry">
  </picture>
</h1>

<p align="center">
  English |
  <a href=".github/i18n/README.ja.md">日本語</a> |
  <a href=".github/i18n/README.ko.md">한국어</a> |
  <a href=".github/i18n/README.zh-CN.md">简体中文</a>
</p>

<p align="center">
  Recolour, paint and fix your VRChat avatar's textures inside Unity.<br>
  Pick a part, change it, watch the avatar update, save a new PNG. Your original never changes.
</p>

<p align="center">
  <a href="https://avistry.github.io/avistry/"><img alt="Add to VCC" height="36" src="https://img.shields.io/badge/Add%20to%20VCC-1f6feb?style=for-the-badge"></a>
  <a href="https://github.com/avistry/avistry/releases/latest"><img alt="Download .unitypackage" height="36" src="https://img.shields.io/badge/Download%20.unitypackage-2ea44f?style=for-the-badge&logo=unity&logoColor=white"></a>
  <a href="https://avistry.github.io/avistry/"><img alt="Website" height="36" src="https://img.shields.io/badge/Website-30363d?style=for-the-badge"></a>
  <a href="https://discord.gg/PM8uC2UnT"><img alt="Discord" height="36" src="https://img.shields.io/badge/Discord-5865F2?style=for-the-badge&logo=discord&logoColor=white"></a>
</p>

<p align="center">
  <img alt="Unity 2022.3" src="https://img.shields.io/badge/Unity-2022.3-555?style=flat-square">
  <img alt="Editor only" src="https://img.shields.io/badge/editor-only-555?style=flat-square">
  <a href="LICENSE"><img alt="MIT license" src="https://img.shields.io/badge/license-MIT-blue?style=flat-square"></a>
  <img alt="No telemetry" src="https://img.shields.io/badge/telemetry-none-2ea44f?style=flat-square">
</p>

<p align="center">
  <img alt="Four steps in Unity: click the forearm sleeve to pick it, tint it blue, paint a pink stroke that stays on the picked pieces, then save a new PNG next to the untouched original" src=".github/media/flow.webp" width="880">
</p>

<p align="center">
  <b>1</b> Pick a part &nbsp;·&nbsp; <b>2</b> Choose a colour &nbsp;·&nbsp; <b>3</b> Paint a detail &nbsp;·&nbsp; <b>4</b> Save a new copy
  <br><sub>Recorded in Unity. <a href="https://avistry.github.io/avistry/#tour">Watch the three-minute tour</a> on the website.</sub>
</p>

## Why Avistry

You bought an avatar. Avistry is how it becomes yours. It is a VRChat avatar toolkit made with care: every tool has to
solve a real creator's problem, in plain words, and feel finished before it ships.

- **Safe by design.** It never overwrites your originals, lives only in the Unity Editor and sends no data. These are
  rules in the code, not settings.
- **Craft over volume.** One tool that works beats ten that almost do. We say no to features that add clutter.
- **Grown with the community.** Ideas are discussed on Discord, contributions are often built with AI agents, and the
  maintainers review and test every change in Unity. AI is a tool here, never the author.

## What it does

<table>
  <tr>
    <td width="50%" valign="top">
      <img alt="Quick material: the Poiyomi glow and glitter colours change on an editable copy of the material" src=".github/media/material.webp" width="100%">
      <br><b>Recolour a whole material.</b> On Poiyomi and lilToon the shadow, rim, matcap, outline and emission
      colours follow along, through the shader's own settings.
    </td>
    <td width="50%" valign="top">
      <img alt="A gradient dragged across the texture, shown live on the avatar's hair" src=".github/media/paint.webp" width="100%">
      <br><b>Paint.</b> 11 brushes with pen pressure, fill, gradients, blur and smudge. Paint on the texture or straight
      onto the 3D model, mirrored if you like.
    </td>
  </tr>
  <tr>
    <td width="50%" valign="top">
      <img alt="Colour Match: the horn is picked and the wing trim takes on its colours" src=".github/media/match.webp" width="100%">
      <br><b>Match colours.</b> Give the hair the colours of the outfit, or the other way round, and keep the original
      shading.
    </td>
    <td width="50%" valign="top">
      <img alt="Blend modes cycling on the top layer while the wings update" src=".github/media/blend.webp" width="100%">
      <br><b>Work in layers.</b> 28 blend modes, masks, clipping, image layers and adjustment layers. Hide, reorder or
      undo any step.
    </td>
  </tr>
</table>

Also in the box:

- **Edit one piece at a time.** Click a sleeve, a ribbon or a belt; only that piece changes, in the Scene view too.
- **Bake helper maps.** Ambient occlusion, a normal map from the texture and a face shadow map for anime-style faces.
- **Check your materials.** Pink shaders, empty slots, missing textures and wrong import settings, explained in plain
  words, with a one-click fix where it is safe.
- **Learn as you go.** Simple and Advanced modes, info icons, a 41-word glossary and a skill tree for Unity beginners.

Every feature is listed on the [Features page](https://avistry.github.io/avistry/features.html).

<p align="center">
  <img alt="Saving adds Hair_ats.png, a material copy and an edit recipe next to the unchanged Hair.png and Hair.mat" src=".github/media/files.webp" width="640">
  <br><sub>Saving always makes new files. The original texture and material stay as they were.</sub>
</p>

## Install

You need Unity 2022.3, the version VRChat uses. Pick one way and stick with it for updates.

**VRChat Creator Companion (recommended).** Open [our page](https://avistry.github.io/avistry/) and press
**Add to VCC**, or paste `https://avistry.github.io/avistry/index.json` into VCC under
*Settings › Packages › Add Repository*. Then press **+** next to Avistry in *Manage Project*. Updates show up there.

**.unitypackage.** Download it from the [latest release](https://github.com/avistry/avistry/releases/latest) and drag
it into the Project window. The tool goes into `Packages/com.avistry.toolkit`, not under Assets.

**Git URL.** In *Window › Package Manager*, press **+**, choose *Add package from git URL…* and paste
`https://github.com/avistry/avistry.git`.

No other packages are needed. If Poiyomi or lilToon is installed, the Colour tab gets extra options for it.
If you tried the very first test build, delete `Assets/AvatarTextureStudio` first (VCC does this for you).

## Quick start

1. Choose **Tools › Avistry › Textures**, select a part of your avatar and pick the texture.
2. Use **Colour** for a quick recolour, or **Select** a piece and **Paint** it. The avatar updates as you go.
3. In **Save**, press **Save as new PNG**.

Nothing is written until you save. Ctrl+Z undoes every edit, and closing with unsaved work asks you first.

## Safety and trust

- All the code is here, under the [MIT license](LICENSE). It is editor-only: nothing goes into your upload or runs in
  VRChat.
- Each release file is scanned on VirusTotal and listed with its SHA-256 in `SHA256SUMS.txt`. Check a download with
  `Get-FileHash <file> -Algorithm SHA256` in PowerShell.
- No telemetry. The only network call is an optional update check (Help › Check for updates automatically). It is off
  until you turn it on, asks GitHub's public API once a day for the latest version and sends nothing about you.
- Back up your project before trying any new tool, this one included.

<details>
<summary><b>Use it with an AI helper (optional)</b></summary>

Avistry can work with an AI app on your computer, such as Claude Code, Cursor or VS Code, through **MCP for Unity**,
a free package made by another team. Everything in Avistry works without it.

1. In *Window › Package Manager*, add `https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity` from git URL.
2. Open *Window › MCP for Unity* and connect your AI app. It runs a server on this computer.
3. In Avistry, open **Help › Connect an AI helper…** and choose **Look only** or **Look and edit**. It starts **Off**.

Each AI action is one Ctrl+Z step named "AI: …". Saving and sending a picture of your texture wait for you to click
**Allow**. Our tools never overwrite your files, delete files or run code.

Good to know: MCP for Unity has its own tools, one of which can run any code in Unity, so keep your AI app's approval
prompts on. Names of parts, materials and textures go to the AI service you use. MCP for Unity collects usage data by
default; its documentation explains how to turn this off.
</details>

## FAQ

<details>
<summary>Will it change my original texture?</summary>

No. Saving creates a new PNG (<code>_ats.png</code>), a new material and a saved edit next to your originals.
</details>

<details>
<summary>Do I need Poiyomi or lilToon?</summary>

No. Painting, layers, selecting and saving work on any material. The shader-aware recolour and the face shadow map
need Poiyomi or lilToon.
</details>

<details>
<summary>Does it get uploaded with my avatar?</summary>

No. Only the PNG and material you save become part of your project.
</details>

<details>
<summary>My antivirus or VirusTotal flagged something.</summary>

Compare the SHA-256 of your download with the release's <code>SHA256SUMS.txt</code> and only download from this
repository's releases. Editor scripts sometimes get a false alarm from one or two engines. If it still looks wrong,
open an issue with the scan link.
</details>

<details>
<summary>It says test build. Is it stable?</summary>

Versions starting with <code>0.</code> are test builds. They work, but things still change. Back up first and tell us
what you run into.
</details>

<details>
<summary>How do I remove it?</summary>

In VCC, press <b>−</b> next to the package. Otherwise delete <code>Packages/com.avistry.toolkit</code>. Your saved
files stay.
</details>

## How this was made

Avatar creators tried the tool and told us what worked; we changed it based on that. We use AI on purpose, to build
faster and to check for security issues and bugs, and an experienced C# developer reads and checks the code. Our own
test harness drives the real Unity editor with automated feature tests, compile checks and screenshots. None of that
is a guarantee, so if you find a problem, please [open an issue](https://github.com/avistry/avistry/issues).

## Contribute

Pull requests are welcome. New features are discussed with the maintainers on [Discord](https://discord.gg/PM8uC2UnT) first; you are welcome
to join. The [contributing guide](.github/CONTRIBUTING.md) explains how to report a bug, ask for a feature or send a fix.

| Folder | What is in it |
|---|---|
| [`Editor/`](Editor) | The tool: C# and shaders. This is all that ships. |
| [`Website~/`](Website~) | The website. |
| [`.github/`](.github) | [Contributing guide](.github/CONTRIBUTING.md), [security policy](.github/SECURITY.md), translated readmes, release workflows. |

## Credits and license

Made by Avistry contributors under the [MIT license](LICENSE). Every version is in the [changelog](CHANGELOG.md).
Some blend-mode formulas are adapted from TextureCraft (MIT), see [Third Party Notices](Third%20Party%20Notices.md).
Poiyomi Toon Shader, lilToon and the VRChat SDK are not included and belong to their authors. This project is not
affiliated with VRChat Inc.
