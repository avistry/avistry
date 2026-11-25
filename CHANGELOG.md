# Changelog

All notable changes to Avistry. Versions follow [Semantic Versioning](https://semver.org):
`MAJOR.MINOR.PATCH`. While the version starts with `0.`, it is a test build and things may still change.

How this file works: every pull request adds a line under `## [Unreleased]`. When we release, the script
`.github/scripts/changelog-release.mjs` turns that section into the new version, and the GitHub release shows it as
the release notes.

## [Unreleased]

## [0.3.0] - 2026-11-25

### Highlights

- **Shade a curved surface.** Draw a gradient across the model, then drag its ends or edit its colour and opacity stops in the Scene view.
- **Paint exactly the faces you pick.** Select an earcup, a row of faces or the faces along a rim. Start a brush layer or gradient inside that selection.
- **Give a brush its own texture.** Search for fur, scratches, stitches, rivets or drips. Stabilise a mouse stroke, or tune how a pen responds to pressure and tilt.
- **Hand someone an editable look.** Save unfinished work as a named draft. Use Pack and Share to send layers and added artwork that another creator can preview on their own avatar.

### Feature drops

**Scene tools and gradients**

- Switch between Select, Recolour and Paint beside the model. Isolate edited parts to reach behind clothing. On a shared renderer, isolation can hide a clothing slot with its own submesh in the shaded Built-in Scene view.
- Place linear, circular or elliptical gradients across UV seams. Move the whole gradient, rotate it about its centre or extend it past the silhouette. When overlapping UVs would give a pixel two different colours, Avistry explains the conflict.

**Selection, brushes and Fill**

- Box and Lasso preview while you drag. Wand, Part and Tile show their result before you click. Select a face or boundary loop in either view. Advanced adds Face Loop, Vertex and Edge. The model and texture each have a UV wireframe switch.
- Select, Paint and Recolour use compact icon palettes beside the model. The editor pairs the same icons with short names for brush, fill, gradient, blend and recolour tools.
- Switch from Paint to picking and back with the current brush and layer. Recolour can highlight its own pieces while keeping your paint selection, including individual faces.
- Hovered and selected pieces have smooth outlines in the Scene view. A softer rim shows where a piece continues behind clothing, and the selection stripes are gone.
- Return to a recently edited part or texture to pick up its face and UV-piece selection where you left it. Clearing a selection is remembered too.
- Connected Fill can close small gaps in outlines. Fill matching colours on the clicked piece, the selection or the whole texture. Limit new marks to a band or box, or mirror brush, eraser and mask strokes on both axes or a custom plane.
- Set up a pen in either painting view. Try light and firm pressure on the test pad, see how the brush responds and save the range in a preset. Choose a fixed tip angle, follow the stroke or use tilt or rotation when the pen reports it.
- Switch Bristle between Dry, Natural and Loaded to change how much paint the strands lay down. The same control works when brushing a mask.
- Pick a brush shape by clicking its thumbnail or name. Use the arrow keys to browse the grid. The picker now recovers after Unity reloads scripts while the brush library is open.
- Paint colour, size and strength sit together above the brush library. Click a section title, chevron or count to open its controls in the editor or Scene view. Focused headings also respond to the left and right arrow keys.

**Layers and templates**

- Preview a template on the current edit and selection before applying it. Group neighbouring layers, merge compatible Paint layers while keeping strokes editable, or preview a rendered merge that you can separate later.
- Stack up to four additional RGB blend modes on a layer and set their order and strength. Copy a layer’s appearance while preserving the destination’s paint, placement and mask.

**Colour and materials**

- Colour Match can pair by brightness, closest colour or most-used colours. Advanced Copy lets you compare settings before creating a material copy for a compatible slot.
- Start painting from a white texture on a textureless Unity Standard or Standard Specular material. Quick material exposes tint and glow on both. On Poiyomi, review animated settings and clear an individual mark on an unlocked copy.

**Drafts and sharing**

- Search drafts by name, source texture or project folder. Drop a saved edit onto the canvas to reopen it.
- Review edits in a style pack together, see missing artwork or incompatible UVs, then open a draft or apply the checked parts. The recipient supplies the original textures, materials and meshes. One Undo restores the previous material assignments after a batch apply.

**Learning in Unity and on the website**

- Open **Tools → Avistry → Skill tree** to resume a lesson or explore a chapter. Open **Glossary** from the same menu to look up a term while you work.
- Follow 24 new lessons on painting and selection, editable layers and materials, and preparing an avatar for VRChat. Practise pen pressure, trace a toggle from the Expressions Menu to the FX controller, or work through Build & Test before uploading.
- The Learn page, skill tree and glossary cover the same 65 entries in English, Japanese, Korean and Simplified Chinese. Existing learning progress carries forward.

### Editor and performance

- Open Settings from the toolbar, including in narrow windows. Open the Avistry website there or choose **Reload Avistry** to refresh the tools and texture preview while keeping your layers.
- Choose **Review support report…** in Settings to see and copy editor and graphics details for Discord or GitHub. Avistry also offers a report after a detected Avistry error or an interrupted editor session.
- Picking a texture prepares its UV clipping while the preview opens, including at 4K. Avistry prepares the active brush during idle time and backs off when editor frames slow down. Long strokes reuse earlier marks, and unchanged layer masks and clipping reuse their prepared data.
- Normal maps, face shadows and mask textures process pixels in the background. Ambient occlusion spreads its GPU work across editor updates. You can cancel a bake, and changing the target discards an outdated result.
- Previewing a rendered layer merge keeps the result on the GPU until you choose **Keep merged layer**. Saving a texture moves seam padding and PNG encoding to a worker so the editor can respond during export.
- The first brush press on a selected UV piece builds its full-resolution mask faster, including the padding around seams.
- The Scene brush finds the surface under the pointer faster on clothed avatars. The UV canvas skips unused wireframes and empty selection overlays.
- Spray, bristles and drips share direction and variation across each dab. On supported graphics hardware, the Scene brush now fills the padding along UV seams in one pass, even on a 4K texture.
- Japanese, Korean and Simplified Chinese have clearer labels for selection, brushes, material settings and saving.
- Website: Follow a leather headset through selection, painting, layers, Colour Match and baking in the Features tour. The recordings show the Scene tools beside the model and the matching editor controls in English, Japanese, Korean and Simplified Chinese. Watch a mesh selection return on both the model and its UV map.

### Fixes

- Scene picks and strokes follow posed and unevenly scaled parts. Round brushes cross eligible UV seams with Smooth across joins, and covered surfaces break the stroke. Switching Unity stages cancels an unfinished drag.
- Interrupted gradients clear their temporary preview. Removing an inverted piece limit restores the whole layer. A missing Scene wireframe rebuilds when needed.
- Applying a normal map enables it on Unity Standard materials. If you re-enable a map that was already assigned, Unity may keep it enabled after Undo. Avistry explains this when you re-enable it.
- Textures with the same name keep separate baked files. PNG-only and cancelled saves preserve the material assignment.
- History restores this edit’s layers independently of other Unity work. Earlier saved edits can be found again after their files move or are renamed in Unity.

### Compatibility

- Avistry runs in the Unity 2022.3 Editor. A pen is optional. Pen tilt and rotation depend on the information Unity receives from the device.
- Existing saved edits still open from their original folders. New drafts go in `Assets/Avistry/Drafts` and generated edits and material copies in `Assets/Avistry/Output`. Style packs with ordinary brush layers can open without optional pen-direction samples.

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

[Unreleased]: https://github.com/avistry/avistry/compare/v0.3.0...HEAD
[0.3.0]: https://github.com/avistry/avistry/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/avistry/avistry/releases/tag/v0.2.0
