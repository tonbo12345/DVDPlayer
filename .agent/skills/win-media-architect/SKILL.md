---
name: win-media-architect
description: Windows 11 DVD Player の技術選定とアーキテクチャ設計を支援するスキル
---

# @win-media-architect - 技術選定・アーキテクチャ設計

## 概要
Windows 11 上で DVD を再生するアプリケーションを構築するための、最適なライブラリ選定とアーキテクチャパターンを提供する。

## 技術スタック

### フレームワーク
- **WPF (.NET 8)** - Windows デスクトップ UI フレームワーク
- WinUI 3 は LibVLCSharp との統合が複雑なため、WPF を選択

### メディア再生エンジン
- **LibVLCSharp.WPF** (NuGet: `LibVLCSharp.WPF` v3.x)
  - VLC の libvlc エンジンの .NET ラッパー
  - DVD 再生、字幕トラック管理、チャプター操作をサポート
- **VideoLAN.LibVLC.Windows** (NuGet: v3.x)
  - Windows 用のネイティブ LibVLC バイナリ
  - libdvdcss を含む（CSS 暗号化 DVD の再生に必要）

### なぜ LibVLCSharp なのか
- Windows Media Foundation は DVD コーデックを標準で含まない
- DirectShow は廃止予定で API が古い
- LibVLC は DVD 再生に最も信頼性の高いオープンソースライブラリ

## NuGet パッケージのインストール

```xml
<!-- DVDPlayer.csproj に追加 -->
<ItemGroup>
  <PackageReference Include="LibVLCSharp.WPF" Version="3.*" />
  <PackageReference Include="VideoLAN.LibVLC.Windows" Version="3.*" />
</ItemGroup>
```

または CLI:
```powershell
dotnet add package LibVLCSharp.WPF
dotnet add package VideoLAN.LibVLC.Windows
```

## LibVLC 初期化パターン

```csharp
using LibVLCSharp.Shared;

// アプリケーション起動時に1回だけ呼び出す
Core.Initialize();

// LibVLC インスタンスの作成（アプリ全体で1つ）
var libVLC = new LibVLC(
    "--dvdnav",              // DVD ナビゲーション有効化
    "--no-video-title-show"  // タイトル表示を無効化
);

// MediaPlayer の作成
var mediaPlayer = new MediaPlayer(libVLC);
```

## DVD メディアのオープンパターン

```csharp
// DVD ドライブからの再生
// "dvd:///D:" の形式で指定（D: はドライブレター）
var media = new Media(libVLC, new Uri("dvd:///D:"));

// オプション設定
media.AddOption(":dvd-angle=1");
media.AddOption(":sub-track=0");       // 字幕トラック
media.AddOption(":audio-track=0");     // 音声トラック

mediaPlayer.Media = media;
mediaPlayer.Play();
```

## VideoView の WPF 統合

```xml
<!-- XAML -->
<Window xmlns:vlc="clr-namespace:LibVLCSharp.WPF;assembly=LibVLCSharp.WPF">
    <Grid>
        <vlc:VideoView x:Name="VideoView" MediaPlayer="{Binding MediaPlayer}">
            <!-- VideoView 内にWPFコンテンツを配置すると airspace 問題を回避 -->
            <Grid>
                <!-- ここに字幕オーバーレイやコントロールを配置 -->
            </Grid>
        </vlc:VideoView>
    </Grid>
</Window>
```

> **重要**: WPF には airspace 制限があり、VideoView の上に直接 WPF コントロールを
> 重ねることはできません。LibVLCSharp.WPF の VideoView は内部で透明ウィンドウを
> 使用するハックを提供しており、VideoView の子要素として配置することで解決します。

## プロジェクト構成

```
DVDPlayer/
├── DVDPlayer.csproj
├── App.xaml / App.xaml.cs
├── MainWindow.xaml / MainWindow.xaml.cs
├── Models/
│   └── SubtitleEntry.cs
├── Services/
│   ├── DvdManager.cs
│   ├── SrtParser.cs
│   └── SubtitleSyncService.cs
├── Themes/
│   └── DarkTheme.xaml
└── .agent/skills/
    ├── win-media-architect/SKILL.md
    ├── dvd-logic-implementer/SKILL.md
    └── win11-ui-vibe/SKILL.md
```
