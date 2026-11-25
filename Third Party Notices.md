# Third Party Notices

## TextureCraft

Several blend-mode and layer formulas in `Editor/Shaders/ATS_Layer.shader`, brush presets, procedural brush
shapes and gravity tracks are adapted from [TextureCraft](https://github.com/storytold/texturecraft)
(`crates/core/src/blend.rs`, `crates/core/src/layer.rs`, `crates/paint/src/presets.rs`, `alphas.rs`, `brush.rs`,
`particles.rs` and `crates/texel/src/noise.rs`). The brush library includes its original generated shapes and
stencil glyphs. TextureCraft is dual-licensed MIT OR Apache-2.0; it is used here under the MIT license:

> MIT License
>
> Copyright (c) 2026 ArtCraft Team and the TextureCraft contributors
>
> Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated
> documentation files (the "Software"), to deal in the Software without restriction, including without limitation the
> rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit
> persons to whom the Software is furnished to do so, subject to the following conditions:
>
> The above copyright notice and this permission notice shall be included in all copies or substantial portions of the
> Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
> WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
> COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
> OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

Poiyomi Toon Shader, lilToon, NDMF and the VRChat SDK are not included in this package. When they are installed, the
tool talks to them through their public editor APIs.
