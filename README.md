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
  <a href="https://discord.gg/sSeWgDJJjY"><img alt="Discord" height="36" src="https://img.shields.io/badge/Discord-5865F2?style=for-the-badge&logo=discord&logoColor=white"></a>
</p>

<p align="center">
  <img alt="Unity 2022.3" src="https://img.shields.io/badge/Unity-2022.3-555?style=flat-square">
  <img alt="Editor only" src="https://img.shields.io/badge/editor-only-555?style=flat-square">
  <a href="LICENSE"><img alt="MIT license" src="https://img.shields.io/badge/license-MIT-blue?style=flat-square"></a>
  <img alt="No telemetry" src="https://img.shields.io/badge/telemetry-none-2ea44f?style=flat-square">
</p>

<p align="center">
  <img alt="One avatar in its original look, then wiped over to three new looks made with Avistry: crimson, frost and venom green" src=".github/media/looks.webp" width="880">
  <br><sub>One avatar, three looks, all made in Avistry. The original files never changed.</sub>
</p>

## How it works

Four steps, one window. Open **Tools › Avistry › Textures**, pick your avatar, and the avatar in the Scene view shows
every change as you make it.

<table>
  <tr>
    <td width="50%" valign="top">
      <img alt="A click on the forearm sleeve on the model picks it, and the same pieces light up on the texture" src=".github/media/en/how-1.webp" width="100%">
      <br><b>1. Pick a part.</b> Click it on the model or on the texture. The same pieces light up in both.
    </td>
    <td width="50%" valign="top">
      <img alt="The glove is tinted from black to blue while the colour bar is dragged" src=".github/media/en/how-2.webp" width="100%">
      <br><b>2. Choose a colour.</b> Shift the colour. Shading and patterns stay where they were.
    </td>
  </tr>
  <tr>
    <td width="50%" valign="top">
      <img alt="A pink stroke dragged along the forearm lands only on the picked pieces" src=".github/media/en/how-3.webp" width="100%">
      <br><b>3. Paint a detail.</b> Brushes and gradients, kept inside the pieces you picked.
    </td>
    <td width="50%" valign="top">
      <img alt="Save as new PNG adds Glove_ats.png, a material copy and an edit recipe next to the unchanged originals" src=".github/media/en/how-4.webp" width="100%">
      <br><b>4. Save a new copy.</b> A new PNG and material next to your originals, which stay as they were.
    </td>
  </tr>
</table>

<p align="center"><sub><a href="https://avistry.github.io/avistry/#tour">Watch the three-minute tour</a> on the website, with voice-over in English, Japanese, Korean and Chinese.</sub></p>

## Select exactly what you mean

An avatar texture is a flat sheet of many small pieces: a sleeve, a strap, a wing. Avistry knows where each piece sits
on the model. Pick one on the texture and it lights up on the avatar, or click the avatar and it lights up on the
texture. Colour, paint and layers then change only what you picked. There are six ways to pick:

<table>
  <tr>
    <td width="33%" valign="top">
      <img alt="Click: pieces picked one at a time on the model" src=".github/media/en/sel-click.webp" width="100%">
      <br><b>Click.</b> One piece at a time. Shift adds, Ctrl removes.
    </td>
    <td width="33%" valign="top">
      <img alt="Box: a rectangle dragged over the arm picks every piece inside it" src=".github/media/en/sel-box.webp" width="100%">
      <br><b>Box.</b> Drag around pieces, on the texture or straight on the model.
    </td>
    <td width="33%" valign="top">
      <img alt="Lasso: a free shape drawn around the arm picks the pieces inside it" src=".github/media/en/sel-lasso.webp" width="100%">
      <br><b>Lasso.</b> Draw any shape around the pieces you want.
    </td>
  </tr>
  <tr>
    <td width="33%" valign="top">
      <img alt="Magic wand: one click picks every piece of the same colour" src=".github/media/en/sel-wand.webp" width="100%">
      <br><b>Magic wand.</b> Click a colour and every piece that has it is picked.
    </td>
    <td width="33%" valign="top">
      <img alt="By part: one click takes every piece of the part" src=".github/media/en/sel-part.webp" width="100%">
      <br><b>By part.</b> One click takes every piece of a part.
    </td>
    <td width="33%" valign="top">
      <img alt="By UV tile: every piece in one tile is picked" src=".github/media/en/sel-tile.webp" width="100%">
      <br><b>By UV tile.</b> Every piece in a tile, for avatars spread over several tiles.
    </td>
  </tr>
</table>

## What else it does

<table>
  <tr>
    <td width="50%" valign="top">
      <img alt="Quick material: the Poiyomi glow and glitter colours change on an editable copy of the material" src=".github/media/en/material.webp" width="100%">
      <br><b>Recolour a whole material.</b> On Poiyomi and lilToon the shadow, rim, matcap, outline and emission
      colours follow along, through the shader's own settings.
    </td>
    <td width="50%" valign="top">
      <img alt="Face paint drawn on one side is mirrored to the other, then a gradient dragged across the texture turns the hair from red to violet" src=".github/media/en/paint.webp" width="100%">
      <br><b>Paint.</b> 11 brushes with pen pressure, fill, gradients, blur and smudge. Paint on the texture or straight
      onto the 3D model, mirrored if you like.
    </td>
  </tr>
  <tr>
    <td width="50%" valign="top">
      <img alt="Colour Match: the horn is picked and the wing trim takes on its colours" src=".github/media/en/match.webp" width="100%">
      <br><b>Match colours.</b> Give the hair the colours of the outfit, or the other way round, and keep the original
      shading.
    </td>
    <td width="50%" valign="top">
      <img alt="Blend modes cycling on the top layer while the wings update" src=".github/media/en/blend.webp" width="100%">
      <br><b>Work in layers.</b> 28 blend modes, masks, clipping, image layers and adjustment layers. Hide, reorder or
      undo any step.
    </td>
  </tr>
</table>

Also in the box:

- **Bake helper maps.** Ambient occlusion, a normal map from the texture and a face shadow map for anime-style faces.
- **Check your materials.** Pink shaders, empty slots, missing textures and wrong import settings, explained in plain
  words, with a one-click fix where it is safe.
- **Learn as you go.** Simple and Advanced modes, info icons, a 65-word glossary and a skill tree for Unity beginners.
- **Your language.** The whole tool is in English, Japanese, Korean and Simplified Chinese, and follows the Unity
  Editor's language by default.

Every feature is listed on the [Features page](https://avistry.github.io/avistry/features.html).

<!-- site-pages:start -->
## The website, page by page

Every page has its own picture. Click one to open it.

<table>
  <tr>
    <td width="50%" valign="top">
      <a href="https://avistry.github.io/avistry/features.html"><img alt="Features: Select pieces, paint on layers, recolour, tune the material and bake maps." src="Website~/media/og-features.jpg" width="100%"></a>
      <br><a href="https://avistry.github.io/avistry/features.html"><b>Features</b></a> · <sub>Select pieces, paint on layers, recolour, tune the material and bake maps.</sub>
    </td>
    <td width="50%" valign="top">
      <a href="https://avistry.github.io/avistry/gallery.html"><img alt="Gallery: Spin it, compare close-ups with the original and see it in three kinds of light." src="Website~/media/og-gallery.jpg" width="100%"></a>
      <br><a href="https://avistry.github.io/avistry/gallery.html"><b>Gallery</b></a> · <sub>Spin it, compare close-ups with the original and see it in three kinds of light.</sub>
    </td>
  </tr>
  <tr>
    <td width="50%" valign="top">
      <a href="https://avistry.github.io/avistry/learn.html"><img alt="Learn: Follow a brush stroke from Unity to a tested avatar. Open a lesson in the skill tree or look up a term as you work." src="Website~/media/og-learn.jpg" width="100%"></a>
      <br><a href="https://avistry.github.io/avistry/learn.html"><b>Learn</b></a> · <sub>Follow a brush stroke from Unity to a tested avatar. Open a lesson in the skill tree or look up a term as you work.</sub>
    </td>
    <td width="50%" valign="top">
      <a href="https://avistry.github.io/avistry/skills.html"><img alt="Skill tree: 64 hands-on skills, from your first brush stroke to testing an avatar toggle." src="Website~/media/og-skills.jpg" width="100%"></a>
      <br><a href="https://avistry.github.io/avistry/skills.html"><b>Skill tree</b></a> · <sub>64 hands-on skills, from your first brush stroke to testing an avatar toggle.</sub>
    </td>
  </tr>
  <tr>
    <td width="50%" valign="top">
      <a href="https://avistry.github.io/avistry/glossary.html"><img alt="Glossary: Find the Unity, shader and VRChat terms behind the controls you use." src="Website~/media/og-glossary.jpg" width="100%"></a>
      <br><a href="https://avistry.github.io/avistry/glossary.html"><b>Glossary</b></a> · <sub>Find the Unity, shader and VRChat terms behind the controls you use.</sub>
    </td>
    <td width="50%" valign="top">
      <a href="https://avistry.github.io/avistry/about.html"><img alt="About: What it is and is not, how to check a download yourself, and how to help." src="Website~/media/og-about.jpg" width="100%"></a>
      <br><a href="https://avistry.github.io/avistry/about.html"><b>About</b></a> · <sub>What it is and is not, how to check a download yourself, and how to help.</sub>
    </td>
  </tr>
  <tr>
    <td width="50%" valign="top">
      <a href="https://avistry.github.io/avistry/download.html"><img alt="Download: VCC is the easiest way, and it keeps Avistry up to date." src="Website~/media/og-download.jpg" width="100%"></a>
      <br><a href="https://avistry.github.io/avistry/download.html"><b>Download</b></a> · <sub>VCC is the easiest way, and it keeps Avistry up to date.</sub>
    </td>
    <td width="50%" valign="top">
      <a href="https://avistry.github.io/avistry/"><img alt="Home: Recolour parts, paint on layers and save a new PNG. Keep the original texture." src="Website~/media/og.jpg" width="100%"></a>
      <br><a href="https://avistry.github.io/avistry/"><b>Home</b></a> · <sub>Recolour parts, paint on layers and save a new PNG. Keep the original texture.</sub>
    </td>
  </tr>
</table>
<!-- site-pages:end -->

## Why Avistry

You bought an avatar. Avistry is how it becomes yours. It is a VRChat avatar toolkit made with care: every tool has to
solve a real creator's problem, in plain words, and feel finished before it ships.

- **Safe by design.** It never overwrites your originals, lives only in the Unity Editor and sends no data. These are
  rules in the code, not settings.
- **Craft over volume.** One tool that works beats ten that almost do. We say no to features that add clutter.
- **Grown with the community.** Ideas are discussed on Discord, contributions are often built with AI agents, and the
  maintainers review and test every change in Unity. AI is a tool here, never the author.

<p align="center">
  <img alt="Your originals, Hair.mat and Hair.png, stay as they were; Avistry adds a new PNG, a material copy and the edit recipe" src=".github/media/en/files.webp" width="640">
  <br><sub>Saving always makes new files. The original texture and material stay as they were.</sub>
</p>

<!-- release-banner:start -->
<p align="center">
  <a href="https://github.com/avistry/avistry/releases/tag/v0.3.0"><img alt="Avistry 0.3.0, Surface Studio. Brown headphones with painted cherry blossoms on the earcup." src="Website~/media/release/0.3.0/banner.jpg" width="880"></a>
  <br><sub>What's new in 0.3.0 · Surface Studio · <a href="https://github.com/avistry/avistry/releases/tag/v0.3.0">Read the release notes</a></sub>
</p>
<!-- release-banner:end -->

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
If you used the early AvatarTextureStudio test build, close Unity and move `Assets/AvatarTextureStudio` outside
the project before installing Avistry. Keep the folder as a backup in case it contains saved edits. The old build
looks for `Hidden/ATS/Layer`. If you see that shader name in an error after installing Avistry, check for old test
build files under Assets and open the current tool from **Tools › Avistry › Textures**.

## Quick start

1. Choose **Tools › Avistry › Textures**, select a part of your avatar and pick the texture.
2. Use **Colour** for a quick recolour, or **Select** a piece and **Paint** it. The avatar updates as you go.
3. In **Save**, press **Save as new PNG**.

Nothing is written until you save. Ctrl+Z undoes every edit, and closing with unsaved work asks you first.

## Safety and trust

- All the code is here, under the [MIT license](LICENSE). It is editor-only: nothing goes into your upload or runs in
  VRChat.
- Each download has a SHA-256 checksum in `SHA256SUMS.txt`. Compare it with
  `Get-FileHash <file> -Algorithm SHA256` in PowerShell. The release notes link to VirusTotal results when available.
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

Pull requests are welcome. New features are discussed with the maintainers on [Discord](https://discord.gg/sSeWgDJJjY) first; you are welcome
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
