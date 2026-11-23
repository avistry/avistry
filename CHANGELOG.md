# Changelog

All notable changes to Avistry. Versions follow [Semantic Versioning](https://semver.org):
`MAJOR.MINOR.PATCH`. While the version starts with `0.`, it is a test build and things may still change.

How this file works: every pull request adds a line under `## [Unreleased]`. When we release, the script
`.github/scripts/changelog-release.mjs` turns that section into the new version, and the GitHub release shows it as
the release notes.

## [Unreleased]

- Website: the tour video has a language switch (EN, 日本語, 한국어, 中文) that keeps the same second, and keeps playing in a mini player you can move and resize while you visit the other pages.
- Website: the tour has its own controls under the picture (play, ±10 s, time line, volume, speed, full screen, keyboard keys) so the subtitles stay clear, starts centred on the screen, slows down like a tape when paused and glitches over when you switch its language.
- Website: painted effects on every page: page heads that draw like the tool (brush, pencil, airbrush, pattern, colour match, layers), brush strokes under the titles and doodles that draw as you scroll, a painted footer, sections that come in as you reach them, the six learning tiers lighting up as you scroll, a Discord help note on Learn, and the gallery spin without a frame.
- Website: the home page opens with the painted Avistry name in the top bar and the links in the middle, with one of four short entrances when the page opens and a different edited look on every visit or refresh, folding into the normal bar as you scroll; a smaller, tidier footer; a Performance mode switch next to Light mode that turns off the drawing and motion; the gallery spin has no title of its own (the close-ups title sits beside it), starts playing straight away and glitches as a new look sweeps across it; videos that do not play on their own show their own first frame instead of a stretched picture; a quick glitch on the home picture when you switch looks; quicker animations; the page opens straight in Japanese, Korean or Chinese when the browser asks for it; Performance mode starts on by itself on very small devices and with data saver.
- Website: a Download page with the latest version, every older version with its files, and download counts from GitHub Releases (updated every six hours), plus a "Build it with us" section to get the source and add features with an AI coding assistant; the other pages keep a small Get it card that links there, and Download sits in the top bar in place of the Get it button. The look you pick (Crimson, Frost, Venom) now colours the whole site: buttons, links, titles, painted heads, brush strokes, doodles, the close-up captions with a label naming the look, and the tour player. Every page has the four looks as icons in the top bar (on the home page they fly in once the picture's own buttons scroll away); switching paints the new look over the page in a circle from the button, and the close-ups glitch as the new look lands. The footer has the languages on the left and the two switches on the right. The hero and gallery looks switch in place (the picture glitches where you are looking and the hero seam stays put) and the gallery spin keeps playing when you switch quickly. Features has a Scroll / Overview switch for the layers and blend modes (Overview shows every layer and every mode as cards, grouped; switching keeps your place; Performance mode always uses it),, and the selection tools play one after another in a small player with back, pause and next. Features also explains the material sliders in three steps with compact cards, shows ambient occlusion and the normal map in their own rows, plays the undo history by itself and gives "Your files stay safe" and the material audit a full row each; its Learn part has one button to the Learn page. Every page has a language button in the top bar, and switching language scrambles the words on screen into the new language and glitches the pictures that change with it. A scrollbar in the look's colours with a soft glow while you scroll. Every page has its own share picture in all four languages, and the address shows the language (/ja/, /ko/, /zh-CN/) so a shared link previews in that language; the page pictures also appear on the top-bar links, in the phone menu, on the home page's Want more cards and in a Keep going row that leads each page on towards Download. The Gallery has the same Scroll and Overview switch as Features, and links from Learn open Features in Overview. The Download page shows versions as a short timeline and has a clearer Build it with us section. Performance mode now turns itself on for weak devices or after the tab crashed or ran short of memory, never just for strong phones, and says so with a way to turn it off. Switching language no longer moves the page while the words scramble. The Download page shows the newest version's painted release banner, and the Japanese, Korean and Chinese text was checked again for meaning and terms.
- Website: GitHub and Discord links in the top bar, a Discord community invite on the home page, the avatar's head no longer cut off on big screens or in "In any world", and the Face, Horns and Arm pictures open their own close-up.
- Website: the Avistry mascot joins the pages: moving emoji beside the titles and lines, stickers that slap on as you scroll (tap one to slap it again) and a mascot peeking over the footer; with reduced motion or Performance mode they stand still.
- Website: on phones the Menu button is a round + button, so the top bar no longer runs off the screen in Japanese, Korean and Chinese.

## [0.2.0] - 2026-11-22

First public release of Avistry.

### Highlights
- Recolour a part of your avatar through the shader's own colour settings (Poiyomi and lilToon), or paint a new
  texture on layers, and save the result as a new PNG. Your original files are never changed.
- Pick parts and pieces on the texture or right on the model, with Box, Lasso, Wand, Part and Tile tools.
- A painting app inside Unity: brushes, fill, gradients, blur and smudge, mirror painting, and 28 blend modes.
- Layers with masks, clipping and correction layers (hue, levels, tone curve and more), plus image layers for
  stickers and logos.
- Colour Match makes one material take the colours of another, for example hair matched to an outfit.
- Bake ambient occlusion, a normal map and a face shadow map, and put them on the material in one click.
- A built-in Glossary with info cards and a skill tree for people new to Unity.
- An optional AI helper that works through the free MCP for Unity package. It starts Off, and every change can be
  undone.

### Added

**Target and parts**
- Open it from **Tools › Avistry › Textures**. Tabs: 1 · Pick, 2 · Recolour, 3 · Limit, 4 · Paint, 5 · Save. A mesh
  is called a *part* and a UV island a *piece*.
- **Edit several parts at once** (*Parts on this texture*): parts whose own material uses the same texture (hair
  pieces with separate materials, for example) can be ticked. Their pieces join the texture view, the preview shows on
  them, and one save gives each of their materials the new texture. Ticking is undoable.
- *Choose a part from a list…* and *Pick another part* when a material slot is empty or a part has no UVs; *Fix N
  image settings* fixes every import setting problem at once.
- **Check this avatar's materials**: finds broken shaders, missing textures, placeholder materials and wrong
  texture settings, with safe one-click fixes.
- The tool finds your part again, with the same material, texture and selection, after Unity restarts or a scene is
  reopened.
- **Change the earlier edit instead**: open a texture Avistry made and it offers the layers that made it, so you can
  change them instead of painting on top.
- **Compare: Original** in the toolbar shows the untouched texture, on the texture view and on the avatar. The **View**
  menu turns the UV wireframe, the selected pieces, the transparency checkerboard, the live preview on the avatar and
  the Scene view highlight on or off, and *Resolution while editing* sets how big the working copy is.

**Recolour**
- Recolour materials through the shader's own colour settings (Poiyomi Pro and lilToon), with an Unlock line for
  locked Poiyomi materials.
- Shift hue, saturation and brightness, or move one colour of the texture to a new one (*Or shift the selected colour
  to*). Shadow, rim light, matcap and outline colours can follow; glow (Emission) only when you switch it on.
  **Also recolour other materials** takes along the materials that share the texture, with *Same texture*, *All* and
  *Only this*.
- **Quick material**: change tint, glow, rim light, matcap, shadow colour, outline, metal and glitter for Poiyomi and
  lilToon materials. It only edits a copy Avistry made. **Make an editable copy** makes one and puts it on the part,
  and Ctrl+Z puts the original back.
- **Recolour by part** (*One piece at a time*): colour one part of the model, then the next, and every part keeps its own colour. Pick parts
  on the model in the Scene view or on the texture view. Each part becomes a *Recolour part* layer that is saved into
  the PNG.
- **White and grey parts take a colour**: *Shift colours | Tint* per part, and *Tint the material (Main Color)* for a
  grey main texture. The Hue / Saturation / Lightness layer has *Adjust | Add colour | Set colour* (OKLCh) so greys
  can take a colour too.
- *Colour changes on this texture*: a summary of every kind of change that is on, each with Reset, Remove or Show.
- **Colour Match layer**: takes another material's colours (hair matched to the outfit, say) while keeping shading and
  detail. Finds and pairs the main colours of both textures, with editable pairs, softness, brightness and colour
  amount. It can match to a material or to any texture, and *Keep whites and blacks* keeps highlights and shadows crisp.
- **Match on the model**: click a spot on your part and a spot on the part to match; the tool compares their
  rendered colours and adjusts until they look the same, in one undo step.
- **Also match material settings**: copies shadow, lighting, optional rim, shine, outline and glow (Emission) settings
  from the matched material when both use the same shader. It shows the list first and is undoable.
- Colour swatches and the Scene picker use the colour a material gives its texture (lilToon Main Color, HSV / Gamma,
  gradation map, adjust mask, 2nd and 3rd layers, and Poiyomi's colour adjust), so picks match what you see.

**Select**
- **Selection tools**: Box, Lasso, Wand (pick by colour), Part and Tile, in the texture view and on the model in the
  Scene view. Shift adds and Ctrl removes. **Grow** and **Shrink** add or remove the pieces that touch the selection.
  On the model, Box and Lasso skip pieces that other parts cover, and a click selects the piece you can see.
- **Wand layer mask**: the Wand can also mask the current layer to one colour area, pixel by pixel.
- **Scene bar**: a small bar at the bottom left of the Scene view with the selection tools, Everything, Nothing,
  Invert, Spotlight, and brush size and strength while painting.
- **UV tiles**: when a part's UVs go beyond the texture square, a tile grid shows the tiles and selects one with a
  click, and 3 · Limit lists them.

**Paint**
- Brushes (presets; round, square, bristle and spray tips; airbrush build-up; grain, jitter, pen pressure), and
  **Save this brush** to keep your own under *More brushes*. Eraser, Fill (whole piece or similar colour, tolerance, area
  scaling) and Gradient (linear, circle, ellipse, edge process, gradient sets, dithering).
- **Blend tool**: Blur and Smudge, kept inside each piece so seams stay clean, on a Paint layer so it can be erased or
  undone.
- **Colour mixing brush** (amount of paint, colour stretch), a **picker circle**, and **colour jitter** (hue,
  saturation, luminosity, blend with sub colour).
- **Only recolour existing paint**: new strokes only change what is already on the layer.
- **Main and sub colour** with X to swap, and a "Main colour → sub colour" gradient option.
- **Colour palettes** (History, Colour set, Approximate colour, Intermediate colour) and **Eyedropper settings**
  (Reference: image, original texture, current layer or top layer; average colour; add picks to a set). Pick colours
  from the texture, the model or anything in the Scene view.
- **Symmetry painting**: strokes are mirrored through the 3D model, with automatic mirror-plane detection, a line-up
  score and the plane drawn in the Scene view. It only mirrors where the other side really matches.
- **3D brush** (Paint across seams): strokes painted on the model are spheres on its surface, so they cross seams and
  are not distorted by the UV layout. Works with round tips, Symmetry, blur, colour mixing and mask painting, on one-sided
  and two-sided parts; other tips, scatter and Smudge paint on the texture as usual.
- **Mask builder**: creates and assigns empty shader masks (emission, rim, matcap…) ready to paint, with per-layer
  R / G / B / A channel switches.

**Layers**
- Paint layers and adjustment layers: hue / saturation / lightness, levels, gradient map, solid colour, brightness /
  contrast, colour balance, tone curve, posterise, invert, threshold, ombré and drawn gradients, with 28 blend modes
  (CLIP STUDIO PAINT names), opacity and ready-made looks.
- **Layer masks** for every layer type: paint with Reveal / Hide / Fill, start from "show everything" or "hide
  everything", invert or delete. Hidden areas are shown in red while you edit.
- **Clip to layer below** for every layer type; clipped layers are marked ↳.
- **Limit to pieces**: a layer can show on the whole texture or only on selected pieces.
- **Image layer** for stickers and logos, with handles on the texture view: drag the corners to scale (Shift keeps the
  shape), the circle to rotate (Shift snaps to 15°), the centre dot to move.
- **Flatten decals** (Poiyomi Decal 1–4, lilToon 2nd / 3rd texture) with exact placement; the decal is switched off on
  the saved material.
- **My looks**: *Save these layers as my look…* keeps the whole layer stack, and it appears with the ready-made looks
  (*Show my looks folder* opens where they are kept).
- **Copy / paste layer settings**: right-click a layer for *Copy settings* and *Paste settings*, also across parts and
  sessions. One undo step.
- **History list** under the layer list: the steps of this edit, newest first. Click a step to go back to it.
- "Where the paint goes" explains why an edit might not show (clipped layers, hidden base layers, strong Emission).

**Maps**
- **Ambient occlusion bake** (*Make maps*): soft shading in creases and where parts are close, baked on the GPU from
  the model in its current pose, with no seams across pieces. Saved as `<texture>_AO.png` and added as a Multiply layer
  you can fade or mask.
- **Normal map from the texture**: bumps from the light and dark details, with Strength, Smoothness and *Dark parts
  stick out*. Saved as `<texture>_normal.png`. *Combine with …* keeps the material's existing normal map, and *Use it*
  puts it in the normal map slot (Poiyomi and lilToon), undoable.
- **Face shadow map**: a toon face shadow ("SDF") map made from the face part, for lilToon and Poiyomi. Saved as
  `<texture>_FaceShadow.png`; *Use it* assigns it and switches the shading to SDF (undoable), and *Swap left and
  right* fixes a flipped shadow.

**Save**
- Bakes a new PNG and a copy of the material; the original files are never changed. *Save edit for later* keeps the
  layers so you can reopen and change them (edits saved by earlier test builds still open).
- Three save choices: *New PNG + new material copy*, *New PNG, update the existing material* and *New PNG only*
  (Simple mode shows the first). The Save tab and the confirm dialog say which other parts change too. Names never chain (`White_ats_2`, not `White_ats_ats`).
- *Dither* (Advanced) keeps smooth gradients from showing steps in the saved PNG.
- **Seam padding on save**: the empty space around pieces is filled with their edge colours, so mip maps don't pull the
  background into seams.
- Save state at a glance: a dot or tick on the Save tab, a "Not saved yet" pill, and a Saved block with Show file.
- **Copy diagnostics** (Help menu, and the dialog when saving fails): a short report of Unity, graphics and texture
  details, shown before anything is copied, with file paths and user names removed.

**Learning and help**
- **Simple and Advanced mode** (toolbar button, panel header, or Help › Advanced mode). Simple shows the everyday
  controls; Advanced shows every setting. The choice is remembered.
- **Info icons and a Glossary** (Help › Glossary…): click an ⓘ for a plain-language card with What it is, When to use
  it, an example, a **Try it** box with **Show me** (opens the right tab and outlines the control), and a **Watch out**
  line.
- **Skill tree** in the Glossary: 40 skills in 6 tiers, from the Unity windows to baked maps. A skill is marked Done
  when you do the thing in the tool, or with **I've got it**. Nothing is ever locked. Tier medals and an "Avistry
  graduate" medal, with toasts you can turn off. Progress stays on this computer, and **Reset skill progress** clears it.
- **Learn more online**: every Glossary card links to the same word in the online glossary and, where there is one,
  to the part of the feature tour that shows it (most with a short video). The skill tree links to the same skill on
  the website, and Help › Online guide opens the tour for the tab you are on. Each link shows its address and opens in
  your browser only when you click it; nothing is loaded before that.
- A short "New to Unity?" note at the top of the panel, with *Start the skill tree*, *Glossary…* and *Hide this for
  good*. The Glossary's menu has *I know Unity already*, A–Z order, *Show toasts*, *Show dots on new info icons* and
  *Bring back my skill progress*.
- **Help › Keyboard shortcuts…** lists the keys (S, B / E, G, J, I, X, [ ], F and more).
- **Update check (optional)**: asks to check GitHub once a day for a new version (or any time with **Tools › Avistry ›
  Check for Updates**). It is off until you turn it on, only
  asks GitHub's public "latest release" page for the version number and sends nothing about you or your project. A
  small bar shows What's new, How to update and Skip this version.
- Messages offer a one-click fix where one is safe (Undo, Paint anywhere, Set to 100%, Affect everything, Switch it on,
  Turn all on, Try again, Use a copy instead).
- Tools › Avistry › Validate glossary checks the Glossary text.
- **Japanese, Korean and Simplified Chinese** (Help › Language): the window, the Glossary and the skill tree follow
  Unity's editor language, or the language you choose. The translations started from DeepL and were checked for meaning and
  terms (the Glossary and skill tree line by line); text without a translation stays in English.

**AI helper (optional)**
- With the separate free MCP for Unity package, an AI app can pick parts, select pieces, recolour and add layers.
  Help › Connect an AI helper… explains the steps. It starts Off, with Look only and Look and edit levels and a Stop
  button. Each change is one Ctrl+Z step named "AI: …". Saving, making maps or seeing a picture of your texture waits
  for your Allow (or *Allow saves / pictures until Unity closes*), and *Recent helper actions* lists what it did. See the README section "Use it with an AI helper".

**Side panel**
- A narrow side panel (260 px wide), with Painting options and Layer settings starting closed, brush buttons with a
  small stroke preview, and the panel shown beside the canvas in windows 540 px or wider.

### Changed
- Opening a part starts on its main colour texture instead of a mask or normal map.

### Fixed
- Baking ambient occlusion no longer saves an all-white map when a Scene view camera is active.
- Painting on the model with "Smooth across joins" no longer draws nothing while the Scene view's camera is active.
- Clicking the model selects the piece you can see, not one hidden under clothing, and parts whose renderer is
  switched off can no longer be picked.
- The side panel no longer clips at its narrowest width, and the Paint panel no longer runs wider than the window.
- The AI helper window is translated too, and brush buttons and the Scene bar's Spotlight button no longer cut off
  Japanese, Korean and Chinese names.
- About 300 Japanese, Korean and Chinese labels that were translated in the wrong sense are corrected (for example
  Fill, Outline, Original, Matcap, Ellipse and Tip), and tab names inside messages match the real tabs. Labels that
  were cut off in Colour Match, Save and Quick material are shorter.
- Empty layers left by undo or a script reload are dropped (with one Console warning) instead of breaking the layer
  list and the save.
- Unlocking Poiyomi 9.1 materials works again.
- The window no longer throws errors when the texture being edited is deleted or replaced while it is open.
- The Save tab no longer says "No edits yet" right after a save, and the layers are only called "not saved yet" when
  they really are unsaved.
- Changing "Only recolour existing paint" now counts as an edit, and the "Switch effect on" button acts on the right
  material.

[Unreleased]: https://github.com/avistry/avistry/compare/v0.2.0...HEAD
[0.2.0]: https://github.com/avistry/avistry/releases/tag/v0.2.0
