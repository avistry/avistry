<h1 align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="../../Website~/media/lockup.svg">
    <img src="../../Website~/media/lockup-light.svg" height="64" alt="Avistry">
  </picture>
</h1>

<p align="center">
  <a href="../../README.md">English</a> |
  日本語 |
  <a href="README.ko.md">한국어</a> |
  <a href="README.zh-CN.md">简体中文</a>
</p>

<p align="center">
  VRChatアバターのテクスチャを、Unityの中で色替え・ペイント・修正できます。<br>
  パーツを選んで変更すると、アバターにそのまま反映されます。あとは新しいPNGとして保存するだけ。元のファイルは変わりません。
</p>

<p align="center">
  <a href="https://avistry.github.io/avistry/"><img alt="VCCに追加" height="36" src="https://img.shields.io/badge/Add%20to%20VCC-1f6feb?style=for-the-badge"></a>
  <a href="https://github.com/avistry/avistry/releases/latest"><img alt=".unitypackageをダウンロード" height="36" src="https://img.shields.io/badge/Download%20.unitypackage-2ea44f?style=for-the-badge&logo=unity&logoColor=white"></a>
  <a href="https://avistry.github.io/avistry/"><img alt="ウェブサイト" height="36" src="https://img.shields.io/badge/Website-30363d?style=for-the-badge"></a>
  <a href="https://discord.gg/sSeWgDJJjY"><img alt="Discord" height="36" src="https://img.shields.io/badge/Discord-5865F2?style=for-the-badge&logo=discord&logoColor=white"></a>
</p>

<p align="center">
  <img alt="Unity 2022.3" src="https://img.shields.io/badge/Unity-2022.3-555?style=flat-square">
  <img alt="エディタ専用" src="https://img.shields.io/badge/editor-only-555?style=flat-square">
  <a href="../../LICENSE"><img alt="MITライセンス" src="https://img.shields.io/badge/license-MIT-blue?style=flat-square"></a>
  <img alt="テレメトリーなし" src="https://img.shields.io/badge/telemetry-none-2ea44f?style=flat-square">
</p>

<p align="center">
  <img alt="元の見た目のアバターが、Avistryで作った3つの新しい見た目(クリムゾン、フロスト、ベノムグリーン)へワイプで切り替わる" src="../../.github/media/looks.webp" width="880">
  <br><sub>ひとつのアバターで、3つの見た目。すべてAvistryで作りました。元のファイルは変わっていません。</sub>
</p>

## 使い方

4ステップ、ウィンドウはひとつ。**Tools › Avistry › Textures** を開いてアバターを選ぶと、Sceneビューのアバターに、操作した変更がそのまま反映されます。

<table>
  <tr>
    <td width="50%" valign="top">
      <img alt="モデル上の前腕の袖をクリックすると選ばれ、テクスチャ上の同じピースが光る" src="../../.github/media/ja/how-1.webp" width="100%">
      <br><b>1. パーツを選ぶ。</b> モデル上でもテクスチャ上でもクリックできます。同じピースが両方で光ります。
    </td>
    <td width="50%" valign="top">
      <img alt="色のバーをドラッグすると、手袋が黒から青に変わる" src="../../.github/media/ja/how-2.webp" width="100%">
      <br><b>2. 色を選ぶ。</b> 色を変えても、陰影と模様はそのまま残ります。
    </td>
  </tr>
  <tr>
    <td width="50%" valign="top">
      <img alt="前腕に沿ってドラッグしたピンクのストロークが、選んだピースの中だけに付く" src="../../.github/media/ja/how-3.webp" width="100%">
      <br><b>3. 細部をペイント。</b> ブラシとグラデーションは、選んだピースの中だけに適用されます。
    </td>
    <td width="50%" valign="top">
      <img alt="「新しいPNGとして保存」を押すと、変わらない元のファイルの隣に、Glove_ats.png、マテリアルのコピー、編集レシピが追加される" src="../../.github/media/ja/how-4.webp" width="100%">
      <br><b>4. 新しいコピーとして保存。</b> 元のファイルの隣に、新しいPNGとマテリアルができます。元のファイルはそのままです。
    </td>
  </tr>
</table>

<p align="center"><sub>ウェブサイトの<a href="https://avistry.github.io/avistry/#tour">3分間のツアー</a>を見る(英語・日本語・韓国語・中国語のナレーション付き)。</sub></p>

## 選びたいところを、ぴったり選ぶ

アバターのテクスチャは、袖、ストラップ、翼など、たくさんの小さなピースが並んだ1枚の平らなシートです。Avistryは、各ピースがモデルのどこにあるかを知っています。テクスチャ上でピースを選ぶとアバター上で光り、アバターをクリックするとテクスチャ上で光ります。色、ペイント、レイヤーは、選んだところだけを変えます。選び方は6つあります。

<table>
  <tr>
    <td width="33%" valign="top">
      <img alt="クリック。モデル上でピースをひとつずつ選ぶ" src="../../.github/media/ja/sel-click.webp" width="100%">
      <br><b>クリック。</b> ピースをひとつずつ選びます。Shiftで追加、Ctrlで除外。
    </td>
    <td width="33%" valign="top">
      <img alt="矩形。腕の上にドラッグした四角が、中にあるピースをすべて選ぶ" src="../../.github/media/ja/sel-box.webp" width="100%">
      <br><b>矩形。</b> ピースをドラッグで囲みます。テクスチャ上でも、モデル上でも使えます。
    </td>
    <td width="33%" valign="top">
      <img alt="投げ縄。腕のまわりに描いた自由な形が、中にあるピースを選ぶ" src="../../.github/media/ja/sel-lasso.webp" width="100%">
      <br><b>投げ縄。</b> 選びたいピースのまわりに、好きな形を描きます。
    </td>
  </tr>
  <tr>
    <td width="33%" valign="top">
      <img alt="魔法の杖。1回クリックすると、同じ色のピースがすべて選ばれる" src="../../.github/media/ja/sel-wand.webp" width="100%">
      <br><b>魔法の杖。</b> 色をクリックすると、その色を持つピースがすべて選ばれます。
    </td>
    <td width="33%" valign="top">
      <img alt="パーツ単位。1回クリックすると、パーツのすべてのピースが選ばれる" src="../../.github/media/ja/sel-part.webp" width="100%">
      <br><b>パーツ単位。</b> ワンクリックで、パーツのすべてのピースを選びます。
    </td>
    <td width="33%" valign="top">
      <img alt="UVタイル単位。1つのタイル内のピースがすべて選ばれる" src="../../.github/media/ja/sel-tile.webp" width="100%">
      <br><b>UVタイル単位。</b> タイル内のピースをすべて選びます。複数のタイルにまたがるアバター向けです。
    </td>
  </tr>
</table>

## ほかにもできること

<table>
  <tr>
    <td width="50%" valign="top">
      <img alt="クイックマテリアル。マテリアルの編集用コピーで、Poiyomiの発光とグリッターの色が変わる" src="../../.github/media/ja/material.webp" width="100%">
      <br><b>マテリアル全体の色替え。</b> PoiyomiとlilToonでは、影・リム・マットキャップ・アウトライン・発光の色も、
      シェーダー自身の設定を通して一緒に変わります。
    </td>
    <td width="50%" valign="top">
      <img alt="片側に描いたフェイスペイントが反対側にも左右対称に入り、続いてテクスチャ上にドラッグしたグラデーションで髪が赤から紫に変わる" src="../../.github/media/ja/paint.webp" width="100%">
      <br><b>ペイント。</b> 筆圧対応の11種類のブラシに加えて、塗りつぶし、グラデーション、ぼかし、指先ツール。
      テクスチャ上にも3Dモデルに直接も描けて、ミラーもできます。
    </td>
  </tr>
  <tr>
    <td width="50%" valign="top">
      <img alt="カラーマッチ。角を選ぶと、翼の縁取りが角の色になる" src="../../.github/media/ja/match.webp" width="100%">
      <br><b>カラーマッチ。</b> 髪に衣装の色を、あるいはその逆を、元の陰影を残したまま移せます。
    </td>
    <td width="50%" valign="top">
      <img alt="一番上のレイヤーの合成モードが切り替わり、それに合わせて翼が変わる" src="../../.github/media/ja/blend.webp" width="100%">
      <br><b>レイヤーで作業。</b> 28種類の合成モード、マスク、クリッピング、画像レイヤー、色調補正レイヤー。
      どの操作も、非表示、並べ替え、元に戻すができます。
    </td>
  </tr>
</table>

ほかにもあります。

- **補助マップのベイク。** アンビエントオクルージョン、テクスチャから作る法線マップ、アニメ調の顔向けのフェイスシャドウマップ。
- **マテリアルのチェック。** ピンク表示のシェーダー、空のスロット、足りないテクスチャ、おかしなインポート設定を、わかりやすい言葉で説明します。
  安全に直せるものは、ワンクリックで修正できます。
- **使いながら学ぶ。** SimpleモードとAdvancedモード、情報アイコン、65語の用語集、Unity初心者向けのスキルツリー。
- **あなたの言語で。** ツール全体が英語、日本語、韓国語、簡体字中国語に対応していて、初期設定ではUnityエディタの言語に合わせて切り替わります。

全機能は[機能ページ](https://avistry.github.io/avistry/features.html)にまとめています。

<!-- site-pages:start -->
## ウェブサイトのページ一覧

ページごとに画像があります。クリックするとそのページが開きます。

<table>
  <tr>
    <td width="50%" valign="top">
      <a href="https://avistry.github.io/avistry/features.html?lang=ja"><img alt="機能: ピースを選び、レイヤーに描き、色替えし、マテリアルを調整して、マップをベイク。" src="../../Website~/media/og-features-ja.jpg" width="100%"></a>
      <br><a href="https://avistry.github.io/avistry/features.html?lang=ja"><b>機能</b></a> · <sub>ピースを選び、レイヤーに描き、色替えし、マテリアルを調整して、マップをベイク。</sub>
    </td>
    <td width="50%" valign="top">
      <a href="https://avistry.github.io/avistry/gallery.html?lang=ja"><img alt="ギャラリー: 回して見て、クローズアップを元の姿と比べ、3 種類の光で確かめましょう。" src="../../Website~/media/og-gallery-ja.jpg" width="100%"></a>
      <br><a href="https://avistry.github.io/avistry/gallery.html?lang=ja"><b>ギャラリー</b></a> · <sub>回して見て、クローズアップを元の姿と比べ、3 種類の光で確かめましょう。</sub>
    </td>
  </tr>
  <tr>
    <td width="50%" valign="top">
      <a href="https://avistry.github.io/avistry/learn.html?lang=ja"><img alt="学ぶ: Unity でのペイントからアバターの動作確認まで。スキルツリーから手順を開き、作業中に用語を調べられます。" src="../../Website~/media/og-learn-ja.jpg" width="100%"></a>
      <br><a href="https://avistry.github.io/avistry/learn.html?lang=ja"><b>学ぶ</b></a> · <sub>Unity でのペイントからアバターの動作確認まで。スキルツリーから手順を開き、作業中に用語を調べられます。</sub>
    </td>
    <td width="50%" valign="top">
      <a href="https://avistry.github.io/avistry/skills.html?lang=ja"><img alt="スキルツリー: 初めての一筆からアバターのトグルの動作確認まで、64 のスキルを実践。" src="../../Website~/media/og-skills-ja.jpg" width="100%"></a>
      <br><a href="https://avistry.github.io/avistry/skills.html?lang=ja"><b>スキルツリー</b></a> · <sub>初めての一筆からアバターのトグルの動作確認まで、64 のスキルを実践。</sub>
    </td>
  </tr>
  <tr>
    <td width="50%" valign="top">
      <a href="https://avistry.github.io/avistry/glossary.html?lang=ja"><img alt="用語集: Unity、シェーダー、VRChat の用語を、実際の操作と結び付けて解説。" src="../../Website~/media/og-glossary-ja.jpg" width="100%"></a>
      <br><a href="https://avistry.github.io/avistry/glossary.html?lang=ja"><b>用語集</b></a> · <sub>Unity、シェーダー、VRChat の用語を、実際の操作と結び付けて解説。</sub>
    </td>
    <td width="50%" valign="top">
      <a href="https://avistry.github.io/avistry/about.html?lang=ja"><img alt="概要: できること・できないこと、ダウンロードの確かめ方、そして協力する方法。" src="../../Website~/media/og-about-ja.jpg" width="100%"></a>
      <br><a href="https://avistry.github.io/avistry/about.html?lang=ja"><b>概要</b></a> · <sub>できること・できないこと、ダウンロードの確かめ方、そして協力する方法。</sub>
    </td>
  </tr>
  <tr>
    <td width="50%" valign="top">
      <a href="https://avistry.github.io/avistry/download.html?lang=ja"><img alt="ダウンロード: VCC がいちばん簡単で、Avistry を最新に保ってくれます。" src="../../Website~/media/og-download-ja.jpg" width="100%"></a>
      <br><a href="https://avistry.github.io/avistry/download.html?lang=ja"><b>ダウンロード</b></a> · <sub>VCC がいちばん簡単で、Avistry を最新に保ってくれます。</sub>
    </td>
    <td width="50%" valign="top">
      <a href="https://avistry.github.io/avistry/?lang=ja"><img alt="ホーム: パーツの色を変え、レイヤーに描いて、新しい PNG として保存。元のテクスチャはそのまま残ります。" src="../../Website~/media/og-ja.jpg" width="100%"></a>
      <br><a href="https://avistry.github.io/avistry/?lang=ja"><b>ホーム</b></a> · <sub>パーツの色を変え、レイヤーに描いて、新しい PNG として保存。元のテクスチャはそのまま残ります。</sub>
    </td>
  </tr>
</table>
<!-- site-pages:end -->

## なぜAvistryなのか

アバターを手に入れたら、Avistryで自分のものにしましょう。Avistryは、丁寧に作ったVRChatアバター用ツールキットです。どのツールも、クリエイターの実際の困りごとをわかりやすい言葉で解決し、公開する前に完成した状態になっていることを大切にしています。

- **設計から安全。** 元のファイルを上書きせず、Unityエディタの中だけで動き、データは一切送りません。これは設定ではなく、コードに組み込んだルールです。
- **量より質。** ほぼ動くツールが10個あるより、きちんと動くツールが1つあるほうがいい。散らかる機能は、お断りしています。
- **コミュニティと育てる。** アイデアはDiscordで話し合い、コントリビューションはAIエージェントを使って作られることも多く、メンテナーがすべての変更をUnityで確認してテストします。ここでAIは道具であり、作者ではありません。

<p align="center">
  <img alt="元のHair.matとHair.pngは変わらず、Avistryが新しいPNG、マテリアルのコピー、編集レシピを追加する" src="../../.github/media/ja/files.webp" width="640">
  <br><sub>保存では必ず新しいファイルを作ります。元のテクスチャとマテリアルはそのままです。</sub>
</p>

<!-- release-banner:start -->
<p align="center">
  <a href="https://github.com/avistry/avistry/releases/tag/v0.3.0"><img alt="Avistry 0.3.0「Surface Studio」。イヤーカップに桜の花を描いた茶色のヘッドホン。" src="../../Website~/media/release/0.3.0/banner-ja.jpg" width="880"></a>
  <br><sub>最新版 0.3.0 · Surface Studio · <a href="https://github.com/avistry/avistry/releases/tag/v0.3.0">リリースノートを読む</a></sub>
</p>
<!-- release-banner:end -->

## インストール

VRChatが使っているUnity 2022.3が必要です。方法を1つ選んで、アップデートも同じ方法で行ってください。

**VRChat Creator Companion(おすすめ)。** [プロジェクトのページ](https://avistry.github.io/avistry/)を開いて
**Add to VCC** を押します。あるいはVCCの *Settings › Packages › Add Repository* に
`https://avistry.github.io/avistry/index.json` を貼り付けます。そのあと *Manage Project* でAvistryの横の **+** を押します。
アップデートもそこに表示されます。

**.unitypackage。** [最新リリース](https://github.com/avistry/avistry/releases/latest)からダウンロードして、
Projectウィンドウにドラッグします。ツールは Assets の下ではなく `Packages/com.avistry.toolkit` に入ります。

**Git URL。** *Window › Package Manager* で **+** を押し、*Add package from git URL…* を選んで
`https://github.com/avistry/avistry.git` を貼り付けます。

ほかのパッケージは必要ありません。PoiyomiかlilToonが入っていれば、色替えタブにそれ用の項目が追加されます。
最初期のテストビルドを使ったことがある人は、先に `Assets/AvatarTextureStudio` を削除してください(VCCなら自動で取り除かれます)。

## クイックスタート

1. **Tools › Avistry › Textures** を選び、アバターのパーツを選んで、テクスチャを選択します。
2. 手早く色を変えるなら **色替え** を、ピースを選んで塗るなら **範囲** のあとに **ペイント** を使います。アバターには操作に合わせてすぐ反映されます。
3. **保存** タブで **新しいPNGとして保存** を押します。

保存するまでは何も書き込まれません。Ctrl+Zですべての編集を元に戻せて、未保存のままウィンドウを閉じようとすると確認が出ます。

## 安全性と信頼性

- コードはすべてこのリポジトリにあり、[MITライセンス](../../LICENSE)で公開しています。エディタ専用なので、アップロードには何も入らず、VRChat内で動くこともありません。
- 各ファイルの SHA-256 は `SHA256SUMS.txt` に記載されています。PowerShell で
  `Get-FileHash <file> -Algorithm SHA256` を実行し、値を照合してください。VirusTotal の検査結果がある場合は、リリースノートから確認できます。
- テレメトリーはありません。ネットワーク通信は、任意のアップデート確認(ヘルプ › 更新を自動的に確認する)だけです。
  オンにするまで動かず、オンにしても1日1回、GitHubの公開APIで最新バージョンを調べるだけで、あなたの情報は何も送りません。
- 新しいツールを試す前には、プロジェクトのバックアップを取ってください。このツールも例外ではありません。

<details>
<summary><b>AIヘルパーと使う(任意)</b></summary>

Avistryは、パソコン上のAIアプリ(Claude Code、Cursor、VS Codeなど)と **MCP for Unity** を通して連携できます。
MCP for Unityは別のチームが作った無料のパッケージです。Avistryは、これがなくても全機能が使えます。

1. *Window › Package Manager* で、git URLから `https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity` を追加します。
2. *Window › MCP for Unity* を開いて、AIアプリをつなぎます。このパソコン上でサーバーが動きます。
3. Avistryで **ヘルプ › AIヘルパーを接続する…** を開き、**見るだけ** か **見て編集** を選びます。最初は **オフ** です。

AIの操作は1回ごとに「AI: …」という名前のCtrl+Z 1段階になります。保存と、テクスチャの画像の送信は、あなたが **許可** を押すまで実行されません。
私たちのツールが、ファイルを上書きしたり、削除したり、コードを実行したりすることはありません。

注意点: MCP for Unityには独自のツールがあり、その1つはUnity内で任意のコードを実行できます。AIアプリの承認確認はオンのままにしてください。
パーツ、マテリアル、テクスチャの名前は、お使いのAIサービスに送られます。MCP for Unityは既定で利用データを収集します。
オフにする方法はそのドキュメントにあります。
</details>

## FAQ

<details>
<summary>元のテクスチャは変更されますか?</summary>

いいえ。保存すると、元のファイルの隣に新しいPNG(<code>_ats.png</code>)、新しいマテリアル、保存した編集内容ができます。
</details>

<details>
<summary>PoiyomiやlilToonは必要ですか?</summary>

いいえ。ペイント、レイヤー、ピース選択、保存は、どのマテリアルでも使えます。シェーダーに合わせた色替えとフェイスシャドウマップには、
PoiyomiかlilToonが必要です。
</details>

<details>
<summary>アバターと一緒にアップロードされますか?</summary>

いいえ。プロジェクトの一部になるのは、保存したPNGとマテリアルだけです。
</details>

<details>
<summary>ウイルス対策ソフトやVirusTotalに引っかかりました。</summary>

ダウンロードしたファイルのSHA-256を、リリースの <code>SHA256SUMS.txt</code> と見比べてください。ダウンロードは、このリポジトリのリリースからだけにしてください。
エディタスクリプトは、1つか2つのエンジンに誤検知されることがあります。それでもおかしいと感じたら、スキャン結果のリンクを添えてIssueを立ててください。
</details>

<details>
<summary>テストビルドと出ますが、安定していますか?</summary>

<code>0.</code> で始まるバージョンはテストビルドです。動作はしますが、まだ変わる部分があります。
先にバックアップを取って、困ったことがあれば教えてください。
</details>

<details>
<summary>削除するにはどうすればいいですか?</summary>

VCCなら、パッケージの横の <b>−</b> を押します。それ以外の方法で入れた場合は、<code>Packages/com.avistry.toolkit</code> を削除してください。
保存したファイルは残ります。
</details>

## 作り方について

Unityでアバターを作っているクリエイターにツールを試してもらい、うまくいった点を聞いて、それをもとに直してきました。
AIは意図して使っています。開発を速く進めるためと、セキュリティ上の問題やバグを確認するためです。そのうえで、経験のあるC#開発者がコードを読んで確認しています。
独自のテストハーネスが、実際のUnityエディタを動かして、自動の機能テスト、コンパイルチェック、スクリーンショットを行います。
どれも保証ではないので、問題を見つけたら[Issueを立てて](https://github.com/avistry/avistry/issues)ください。

## 開発に参加する

プルリクエストを歓迎します。新しい機能は、まず[Discord](https://discord.gg/sSeWgDJJjY)でメンテナーと話し合います。ぜひ参加してください。バグの報告、機能の提案、修正の送り方は[コントリビューションガイド](../../.github/CONTRIBUTING.md)にあります。

| フォルダ | 中身 |
|---|---|
| [`Editor/`](../../Editor) | ツール本体(C#とシェーダー)。配布されるのはこれだけです。 |
| [`Website~/`](../../Website~) | ウェブサイト。 |
| [`.github/`](../../.github) | [コントリビューションガイド](../../.github/CONTRIBUTING.md)、[セキュリティポリシー](../../.github/SECURITY.md)、翻訳版README、リリースのワークフロー。 |

## クレジットとライセンス

作者はAvistry contributorsで、[MITライセンス](../../LICENSE)で公開しています。全バージョンは[変更履歴](../../CHANGELOG.md)にあります。
一部の合成モードの計算式は、TextureCraft(MIT)から改変して使っています。詳しくは
[Third Party Notices](../../Third%20Party%20Notices.md)を見てください。Poiyomi Toon Shader、lilToon、VRChat SDKは同梱しておらず、
それぞれの作者のものです。このプロジェクトはVRChat Inc.とは関係ありません。
