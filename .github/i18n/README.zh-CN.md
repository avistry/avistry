<h1 align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="../../Website~/media/lockup.svg">
    <img src="../../Website~/media/lockup-light.svg" height="64" alt="Avistry">
  </picture>
</h1>

<p align="center">
  <a href="../../README.md">English</a> |
  <a href="README.ja.md">日本語</a> |
  <a href="README.ko.md">한국어</a> |
  简体中文
</p>

<p align="center">
  在 Unity 里给你的 VRChat 模型换色、绘制、修补贴图。<br>
  选一个部位，改一改，看着模型实时更新，再存成新的 PNG。你的原图不会被改动。
</p>

<p align="center">
  <a href="https://avistry.github.io/avistry/"><img alt="添加到 VCC" height="36" src="https://img.shields.io/badge/Add%20to%20VCC-1f6feb?style=for-the-badge"></a>
  <a href="https://github.com/avistry/avistry/releases/latest"><img alt="下载 .unitypackage" height="36" src="https://img.shields.io/badge/Download%20.unitypackage-2ea44f?style=for-the-badge&logo=unity&logoColor=white"></a>
  <a href="https://avistry.github.io/avistry/"><img alt="网站" height="36" src="https://img.shields.io/badge/Website-30363d?style=for-the-badge"></a>
</p>

<p align="center">
  <img alt="Unity 2022.3" src="https://img.shields.io/badge/Unity-2022.3-555?style=flat-square">
  <img alt="只在编辑器里运行" src="https://img.shields.io/badge/editor-only-555?style=flat-square">
  <a href="../../LICENSE"><img alt="MIT 许可证" src="https://img.shields.io/badge/license-MIT-blue?style=flat-square"></a>
  <img alt="没有遥测" src="https://img.shields.io/badge/telemetry-none-2ea44f?style=flat-square">
</p>

<p align="center">
  <img alt="Unity 里的四个步骤：点一下前臂的袖子选中它，把它调成蓝色，画一笔粉色且只落在选中的区块上，最后保存成新的 PNG，原图原封不动地放在旁边" src="../../.github/media/flow.webp" width="880">
</p>

<p align="center">
  <b>1</b> 选择部位 &nbsp;·&nbsp; <b>2</b> 选择颜色 &nbsp;·&nbsp; <b>3</b> 绘制细节 &nbsp;·&nbsp; <b>4</b> 保存为新副本
  <br><sub>在 Unity 中录制。</sub>
</p>

## 为什么选 Avistry

你买下了一个模型，Avistry 能让它变成你自己的。这是一个用心打造的 VRChat 模型工具包：每个工具都要用大白话解决创作者的真实问题，并且在发布前就已经做到完善、好用。

- **从设计上就安全。**它不会覆盖你的原图，只在 Unity 编辑器里运行，也不发送任何数据。这些是写在代码里的规则，不是设置项。
- **精工胜过数量。**一个真正好用的工具，胜过十个差不多能用的。我们会拒绝只会增加杂乱的功能。
- **和社区一起成长。**想法在 Discord 上讨论，贡献常常借助 AI 代理完成，维护者会在 Unity 里审查并测试每一处改动。在这里，AI 是工具，从来不是作者。

## 它能做什么

<table>
  <tr>
    <td width="50%" valign="top">
      <img alt="Quick material：在材质的可编辑副本上，Poiyomi 的发光和闪粉颜色一起变化" src="../../.github/media/material.webp" width="100%">
      <br><b>整个材质换色。</b>在 Poiyomi 和 lilToon 上，阴影、边缘光、Matcap、描边和自发光的颜色会跟着一起变，
      走的是着色器自带的设置。
    </td>
    <td width="50%" valign="top">
      <img alt="在贴图上拖出一条渐变，同时在模型的头发上实时显示" src="../../.github/media/paint.webp" width="100%">
      <br><b>绘制。</b>11 种画笔，支持压感，另有填充、渐变、模糊和涂抹。可以画在贴图上，也可以直接画在 3D 模型上，
      需要的话还能镜像。
    </td>
  </tr>
  <tr>
    <td width="50%" valign="top">
      <img alt="Colour Match：选中角之后，翅膀的饰边换上了它的颜色" src="../../.github/media/match.webp" width="100%">
      <br><b>匹配颜色。</b>让头发用上衣服的配色，或者反过来，同时保留原来的明暗。
    </td>
    <td width="50%" valign="top">
      <img alt="最上层的图层依次切换混合模式，翅膀随之更新" src="../../.github/media/blend.webp" width="100%">
      <br><b>用图层工作。</b>28 种混合模式、蒙版、剪贴、图片图层和调整图层。任何一步都能隐藏、调整顺序或撤销。
    </td>
  </tr>
</table>

还有这些：

- **一次只改一块。**点一下袖子、丝带或腰带，只有那一块会变，Scene 视图里也一样。
- **烘焙辅助贴图。**环境光遮蔽、由贴图生成的法线贴图，还有给动漫风脸部用的脸部阴影图。
- **检查材质。**粉红色着色器、空槽位、缺失的贴图和不对的导入设置，都用大白话讲清楚，能安全修复的地方有一键修复按钮。
- **边用边学。**有 Simple 和 Advanced 两种模式、信息图标、41 个词条的术语表，还有给 Unity 新手的技能树。

所有功能都列在[功能页面](https://avistry.github.io/avistry/features.html)里。

<p align="center">
  <img alt="保存后，在没有改动的 Hair.png 和材质旁边多出了 Hair_ats.png、一个材质副本和一份编辑记录" src="../../.github/media/files.webp" width="640">
  <br><sub>保存总是生成新文件。原来的贴图和材质保持原样。</sub>
</p>

## 安装

你需要 Unity 2022.3，也就是 VRChat 使用的版本。选一种方式，之后更新也一直用这种。

**VRChat Creator Companion（推荐）。**打开[我们的页面](https://avistry.github.io/avistry/)，点 **Add to VCC**，
或者在 VCC 的 *Settings › Packages › Add Repository* 里粘贴 `https://avistry.github.io/avistry/index.json`。
然后在 *Manage Project* 里点 Avistry 旁边的 **+**。更新也会出现在那里。

**.unitypackage。**从[最新发布](https://github.com/avistry/avistry/releases/latest)下载，拖进 Project 窗口。
工具会装到 `Packages/com.avistry.toolkit`，不在 Assets 下面。

**Git URL。**在 *Window › Package Manager* 里点 **+**，选 *Add package from git URL…*，然后粘贴
`https://github.com/avistry/avistry.git`。

不需要其他包。如果装了 Poiyomi 或 lilToon，Colour 标签页会多出针对它们的选项。
如果你试过最早的测试版，请先删除 `Assets/AvatarTextureStudio`（VCC 会替你删掉）。

## 快速上手

1. 选 **Tools › Avistry › Textures**，选中模型的某个部位，再挑出要编辑的贴图。
2. 想快速换色就用 **Colour**，或者先 **Select** 选一块再 **Paint** 绘制。模型会随着你的操作实时更新。
3. 在 **Save** 里点 **Save as new PNG**。

保存之前不会写入任何东西。Ctrl+Z 可以撤销每一步编辑，带着未保存的内容关闭窗口时会先问你。

## 安全与信任

- 代码全部都在这里，采用 [MIT 许可证](../../LICENSE)。它只在编辑器里运行：不会有任何东西进入你的上传包，也不会在 VRChat 里运行。
- 每个发布文件都扫描过 VirusTotal，SHA-256 校验值列在 `SHA256SUMS.txt` 里。在 PowerShell 里用
  `Get-FileHash <文件> -Algorithm SHA256` 就能核对下载的文件。
- 没有遥测。唯一的网络请求是可选的更新检查（Help › Check for updates automatically）。它默认关闭，
  开启后每天只向 GitHub 的公开 API 询问一次最新版本，不会发送任何关于你的信息。
- 试用任何新工具之前，包括这个，都请先备份项目。

<details>
<summary><b>搭配 AI 助手使用（可选）</b></summary>

Avistry 可以通过 **MCP for Unity** 与你电脑上的 AI 应用（如 Claude Code、Cursor 或 VS Code）配合使用。
MCP for Unity 是另一个团队制作的免费包。没有它，Avistry 的所有功能照常可用。

1. 在 *Window › Package Manager* 里，用 git URL 添加 `https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity`。
2. 打开 *Window › MCP for Unity*，连接你的 AI 应用。它会在这台电脑上运行一个服务器。
3. 在 Avistry 里打开 **Help › Connect an AI helper…**，选择 **Look only**（只看）或 **Look and edit**（看和改）。默认是 **Off**。

AI 的每个操作都是一步名为“AI: …”的 Ctrl+Z。保存，以及把你的贴图图片发出去，都要等你点 **Allow** 才会进行。
我们的工具不会覆盖你的文件、删除文件或运行代码。

请注意：MCP for Unity 有它自己的工具，其中一个可以在 Unity 里运行任意代码，所以请保持 AI 应用的批准提示开启。
部位、材质和贴图的名称会发送给你使用的 AI 服务。MCP for Unity 默认会收集使用数据，关闭方法见它的文档。
</details>

## 常见问题

<details>
<summary>会改动我的原始贴图吗？</summary>

不会。保存会在原文件旁边生成一张新的 PNG（<code>_ats.png</code>）、一个新的材质和一份保存好的编辑记录。
</details>

<details>
<summary>一定要有 Poiyomi 或 lilToon 吗？</summary>

不用。绘制、图层、选择某一块和保存，在任何材质上都能用。针对着色器的换色和脸部阴影图需要 Poiyomi 或 lilToon。
</details>

<details>
<summary>它会随模型一起上传吗？</summary>

不会。只有你保存下来的 PNG 和材质会成为项目的一部分。
</details>

<details>
<summary>我的杀毒软件或 VirusTotal 报了警。</summary>

把你下载文件的 SHA-256 和发布页里的 <code>SHA256SUMS.txt</code> 对一下，并且只从这个仓库的发布页下载。
编辑器脚本偶尔会被一两个引擎误报。如果还是觉得不对，请提一个 issue，并附上扫描链接。
</details>

<details>
<summary>写着测试版，稳定吗？</summary>

以 <code>0.</code> 开头的版本都是测试版。能正常使用，但还会有变化。请先备份项目，遇到的问题也欢迎告诉我们。
</details>

<details>
<summary>怎么卸载？</summary>

在 VCC 里，点包旁边的 <b>−</b>。其他安装方式，删除 <code>Packages/com.avistry.toolkit</code> 即可。
你自己保存的文件会留着。
</details>

## 这是怎么做出来的

做模型的创作者试用了这个工具，告诉我们哪里好用，我们再据此修改。我们是有意使用 AI 的，
用它来做得更快，也用它检查安全问题和 bug，之后还有一位经验丰富的 C# 开发者阅读并检查代码。
我们自己的测试框架会驱动真实的 Unity 编辑器，跑自动化的功能测试、编译检查和截图。
这些都不是保证，所以如果你发现了问题，请[提一个 issue](https://github.com/avistry/avistry/issues)。

## 参与贡献

欢迎提交 Pull Request。新功能请先在 [Discord](https://discord.gg/PM8uC2UnT) 上和维护者讨论，欢迎加入。如何报告问题、提出功能建议或提交修复，请看[贡献指南](../../.github/CONTRIBUTING.md)。

| 文件夹 | 里面有什么 |
|---|---|
| [`Editor/`](../../Editor) | 工具本体：C# 和着色器。发布的只有这一部分。 |
| [`Website~/`](../../Website~) | 网站。 |
| [`.github/`](../../.github) | [贡献指南](../../.github/CONTRIBUTING.md)、[安全政策](../../.github/SECURITY.md)、各语言的 README、发布工作流。 |

## 致谢与许可证

由 Avistry contributors 制作，以 [MIT 许可证](../../LICENSE)发布。每个版本都列在[更新日志](../../CHANGELOG.md)里。
部分混合模式公式改编自 TextureCraft（MIT），详情见[第三方声明](../../Third%20Party%20Notices.md)。
Poiyomi Toon Shader、lilToon 和 VRChat SDK 不包含在本项目中，版权归各自作者所有。本项目与 VRChat Inc. 没有隶属关系。
