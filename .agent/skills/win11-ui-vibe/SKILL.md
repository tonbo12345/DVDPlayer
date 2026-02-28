---
name: win11-ui-vibe
description: Windows 11 のモダン UI デザイン（Mica、ダークモード、角丸）を WPF で実現するスキル
---

# @win11-ui-vibe - Windows 11 ネイティブ UI 構築

## 概要
Windows 11 のデザイン言語（Mica 素材、角丸、半透明コントロール）を WPF アプリケーションに適用し、モダンでプレミアムな DVD Player UI を構築する。

## Mica / Acrylic 背景の実装

### WindowChrome を使用したカスタムタイトルバー

```xml
<Window ...
        WindowStyle="None"
        AllowsTransparency="True"
        Background="Transparent">
    <WindowChrome.WindowChrome>
        <WindowChrome CaptionHeight="32"
                      ResizeBorderThickness="5"
                      GlassFrameThickness="-1"
                      CornerRadius="8" />
    </WindowChrome.WindowChrome>
</Window>
```

### ダークテーマのカラーパレット

```xml
<!-- Themes/DarkTheme.xaml -->
<ResourceDictionary>
    <!-- 背景色 -->
    <SolidColorBrush x:Key="WindowBackground" Color="#1E1E2E" />
    <SolidColorBrush x:Key="ControlBarBackground" Color="#181825" />
    <SolidColorBrush x:Key="ControlBarBackgroundHover" Color="#313244" />

    <!-- テキスト色 -->
    <SolidColorBrush x:Key="PrimaryText" Color="#CDD6F4" />
    <SolidColorBrush x:Key="SecondaryText" Color="#A6ADC8" />
    <SolidColorBrush x:Key="AccentText" Color="#89B4FA" />

    <!-- アクセント色 -->
    <SolidColorBrush x:Key="AccentColor" Color="#89B4FA" />
    <SolidColorBrush x:Key="AccentColorHover" Color="#B4D0FB" />
    <SolidColorBrush x:Key="AccentColorPressed" Color="#7BA2E0" />

    <!-- ボーダー -->
    <SolidColorBrush x:Key="BorderColor" Color="#45475A" />
    <CornerRadius x:Key="ControlCornerRadius">8</CornerRadius>
    <CornerRadius x:Key="ButtonCornerRadius">6</CornerRadius>

    <!-- フォント -->
    <FontFamily x:Key="UIFont">Segoe UI Variable, Segoe UI, Yu Gothic UI</FontFamily>
</ResourceDictionary>
```

## コントロールバーのデザイン

### XAML テンプレート

```xml
<!-- コントロールバー -->
<Border Background="{StaticResource ControlBarBackground}"
        CornerRadius="0,0,8,8"
        Padding="16,8">
    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="Auto" />   <!-- 再生コントロール -->
            <ColumnDefinition Width="*" />       <!-- シークバー -->
            <ColumnDefinition Width="Auto" />   <!-- 音量・設定 -->
        </Grid.ColumnDefinitions>

        <!-- 再生コントロール -->
        <StackPanel Grid.Column="0" Orientation="Horizontal" VerticalAlignment="Center">
            <Button x:Name="BtnPrevChapter" Style="{StaticResource MediaButton}"
                    Content="⏮" ToolTip="前のチャプター" />
            <Button x:Name="BtnPlayPause" Style="{StaticResource MediaButton}"
                    Content="▶" ToolTip="再生/一時停止"
                    FontSize="20" Width="48" Height="48" />
            <Button x:Name="BtnNextChapter" Style="{StaticResource MediaButton}"
                    Content="⏭" ToolTip="次のチャプター" />
        </StackPanel>

        <!-- シークバー + 時間表示 -->
        <Grid Grid.Column="1" Margin="16,0">
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto" />
                <RowDefinition Height="Auto" />
            </Grid.RowDefinitions>
            <Slider x:Name="SeekBar" Style="{StaticResource SeekBarStyle}"
                    Minimum="0" Maximum="100" />
            <StackPanel Grid.Row="1" Orientation="Horizontal"
                        HorizontalAlignment="Center">
                <TextBlock x:Name="TxtCurrentTime"
                           Text="00:00:00"
                           Foreground="{StaticResource SecondaryText}"
                           FontSize="11" />
                <TextBlock Text=" / "
                           Foreground="{StaticResource SecondaryText}"
                           FontSize="11" />
                <TextBlock x:Name="TxtTotalTime"
                           Text="00:00:00"
                           Foreground="{StaticResource SecondaryText}"
                           FontSize="11" />
            </StackPanel>
        </Grid>

        <!-- 音量・フルスクリーン -->
        <StackPanel Grid.Column="2" Orientation="Horizontal" VerticalAlignment="Center">
            <Button x:Name="BtnSubtitle" Style="{StaticResource MediaButton}"
                    Content="💬" ToolTip="字幕設定" />
            <Button x:Name="BtnVolume" Style="{StaticResource MediaButton}"
                    Content="🔊" ToolTip="音量" />
            <Slider x:Name="VolumeBar" Width="80"
                    Minimum="0" Maximum="100" Value="80"
                    Style="{StaticResource VolumeBarStyle}" />
            <Button x:Name="BtnFullScreen" Style="{StaticResource MediaButton}"
                    Content="⛶" ToolTip="フルスクリーン" />
        </StackPanel>
    </Grid>
</Border>
```

### メディアボタンスタイル

```xml
<Style x:Key="MediaButton" TargetType="Button">
    <Setter Property="Background" Value="Transparent" />
    <Setter Property="Foreground" Value="{StaticResource PrimaryText}" />
    <Setter Property="BorderThickness" Value="0" />
    <Setter Property="Width" Value="40" />
    <Setter Property="Height" Value="40" />
    <Setter Property="FontSize" Value="16" />
    <Setter Property="Cursor" Value="Hand" />
    <Setter Property="Template">
        <Setter.Value>
            <ControlTemplate TargetType="Button">
                <Border x:Name="ButtonBorder"
                        Background="{TemplateBinding Background}"
                        CornerRadius="{StaticResource ButtonCornerRadius}"
                        Padding="4">
                    <ContentPresenter HorizontalAlignment="Center"
                                      VerticalAlignment="Center" />
                </Border>
                <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                        <Setter TargetName="ButtonBorder"
                                Property="Background"
                                Value="{StaticResource ControlBarBackgroundHover}" />
                    </Trigger>
                    <Trigger Property="IsPressed" Value="True">
                        <Setter TargetName="ButtonBorder"
                                Property="Background"
                                Value="{StaticResource AccentColorPressed}" />
                    </Trigger>
                </ControlTemplate.Triggers>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>
```

## 字幕オーバーレイのデザイン

### 日本語字幕（上部）

```xml
<TextBlock x:Name="JapaneseSubtitle"
           VerticalAlignment="Top"
           HorizontalAlignment="Center"
           Margin="0,40,0,0"
           FontFamily="Yu Gothic UI, Meiryo"
           FontSize="22"
           FontWeight="Bold"
           Foreground="White"
           TextAlignment="Center"
           TextWrapping="Wrap"
           MaxWidth="800">
    <TextBlock.Effect>
        <DropShadowEffect Color="Black"
                          BlurRadius="4"
                          ShadowDepth="2"
                          Opacity="0.9" />
    </TextBlock.Effect>
</TextBlock>
```

### 英語字幕（下部）

```xml
<TextBlock x:Name="EnglishSubtitle"
           VerticalAlignment="Bottom"
           HorizontalAlignment="Center"
           Margin="0,0,0,80"
           FontFamily="Segoe UI, Arial"
           FontSize="24"
           FontWeight="SemiBold"
           Foreground="White"
           TextAlignment="Center"
           TextWrapping="Wrap"
           MaxWidth="800">
    <TextBlock.Effect>
        <DropShadowEffect Color="Black"
                          BlurRadius="4"
                          ShadowDepth="2"
                          Opacity="0.9" />
    </TextBlock.Effect>
</TextBlock>
```

## フルスクリーン切り替え

```csharp
private WindowState _previousState;
private WindowStyle _previousStyle;
private bool _isFullScreen = false;

private void ToggleFullScreen()
{
    if (_isFullScreen)
    {
        // ウィンドウモードに戻る
        WindowStyle = _previousStyle;
        WindowState = _previousState;
        ResizeMode = ResizeMode.CanResize;
    }
    else
    {
        // フルスクリーンに切り替え
        _previousState = WindowState;
        _previousStyle = WindowStyle;
        WindowStyle = WindowStyle.None;
        WindowState = WindowState.Maximized;
        ResizeMode = ResizeMode.NoResize;
    }
    _isFullScreen = !_isFullScreen;
}
```

## デザイン原則

1. **ダークファースト**: 映画鑑賞に適した暗い配色をデフォルトとする
2. **最小限のクロム**: コントロールバーは使用時のみ表示（マウスオーバーで表示）
3. **高い視認性**: 字幕には影付きの白文字を使用し、どんな映像の上でも読みやすくする
4. **Catppuccin Mocha**: Windows 11 のダークモードに合う色彩設計
5. **スムーズなアニメーション**: コントロールバーの表示/非表示にフェードアニメーションを使用
