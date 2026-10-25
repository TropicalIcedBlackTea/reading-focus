using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Path = System.Windows.Shapes.Path;

namespace ReadingFocus
{
    internal sealed partial class FocusWindow
    {
        private readonly Window window;
        private readonly FocusClock clock;
        private readonly Preferences prefs;
        private readonly DataStore store;
        private readonly AcademicDictionary dictionary;
        private readonly DispatcherTimer ticker;
        private readonly Queue<WordRecord> toastQueue = new Queue<WordRecord>();
        private WordRecord toastWord;
        private SessionRecord activeSession;
        private SessionRecord displayedSession;
        private readonly bool qa;
        private readonly string exportRoot;
        private bool compact, customOpen, syncing, showSummary, closing;
        private bool notesOpen, syncingNotes;
        private long notesSaveDue;
        private long previousRemaining, lastSave, toastUntil, redUntil, blockedAt;
        private DateTime accountWall;
        private uint clipboardSequence;
        private int clipboardRetries;
        private long clipboardRetryAt;
        private IntPtr hook, blockedHandle;
        private string blockedName;
        private Native.WinEvent foregroundEvent;
        private bool clipboardListening;
        private HwndSource source;
        private string exportFolder;
        private readonly Brush ink = B("#1C2420"), muted = B("#5F6A64"), faint = B("#98A19C"), accent = B("#2E5B49"), amber = B("#A86E1F"), alert = B("#C2413B");
        private readonly Brush cardLine = B("#E2E5E1"), cardDone = B("#D9C6A2");
        private readonly Effect raised = new DropShadowEffect { Color = Color.FromRgb(0x1C, 0x24, 0x20), BlurRadius = 4, ShadowDepth = 1, Direction = 270, Opacity = 0.14 };
        private static Brush B(string s) { Brush b = (Brush)new BrushConverter().ConvertFromString(s); b.Freeze(); return b; }
        private T F<T>(string name) where T : class { return window.FindName(name) as T; }

        private const string Markup = @"
<Window xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
 Title='文献专注' Width='336' SizeToContent='Height' WindowStyle='None' AllowsTransparency='True'
 Background='Transparent' ResizeMode='NoResize' Topmost='True' FontFamily='Segoe UI, Microsoft YaHei UI'
 FontSize='12' Foreground='#1C2420' UseLayoutRounding='True' SnapsToDevicePixels='True'
 TextOptions.TextFormattingMode='Display' TextOptions.TextRenderingMode='Grayscale'>
 <Window.Resources>
  <FontFamily x:Key='Icons'>Segoe Fluent Icons, Segoe MDL2 Assets</FontFamily>
  <FontFamily x:Key='Digits'>Segoe UI</FontFamily>
  <SolidColorBrush x:Key='Ink' Color='#1C2420'/>
  <SolidColorBrush x:Key='Muted' Color='#5F6A64'/>
  <SolidColorBrush x:Key='Faint' Color='#98A19C'/>
  <SolidColorBrush x:Key='Line' Color='#E7E9E6'/>
  <SolidColorBrush x:Key='Fill' Color='#F3F4F2'/>
  <SolidColorBrush x:Key='Accent' Color='#2E5B49'/>
  <SolidColorBrush x:Key='AccentSoft' Color='#E5EEE9'/>
  <SolidColorBrush x:Key='Alert' Color='#C2413B'/>
  <Style x:Key='FocusRing'>
   <Setter Property='Control.Template'><Setter.Value><ControlTemplate>
    <Border Margin='-2' BorderBrush='{StaticResource Accent}' BorderThickness='1.5' CornerRadius='9'/>
   </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style TargetType='Button'>
   <Setter Property='Cursor' Value='Hand'/><Setter Property='Background' Value='Transparent'/><Setter Property='Foreground' Value='{StaticResource Ink}'/>
   <Setter Property='BorderBrush' Value='Transparent'/><Setter Property='BorderThickness' Value='1'/><Setter Property='Padding' Value='10,0'/>
   <Setter Property='HorizontalContentAlignment' Value='Center'/><Setter Property='VerticalContentAlignment' Value='Center'/>
   <Setter Property='FocusVisualStyle' Value='{StaticResource FocusRing}'/>
   <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>
    <Grid>
     <Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='7'/>
     <Border x:Name='Shade' Background='#1C2420' Opacity='0' CornerRadius='7'/>
     <ContentPresenter Margin='{TemplateBinding Padding}' HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='{TemplateBinding VerticalContentAlignment}'/>
    </Grid>
    <ControlTemplate.Triggers>
     <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Shade' Property='Opacity' Value='0.05'/></Trigger>
     <Trigger Property='IsPressed' Value='True'><Setter TargetName='Shade' Property='Opacity' Value='0.1'/></Trigger>
     <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.38'/></Trigger>
    </ControlTemplate.Triggers>
   </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style x:Key='Primary' TargetType='Button' BasedOn='{StaticResource {x:Type Button}}'>
   <Setter Property='Background' Value='{StaticResource Accent}'/><Setter Property='Foreground' Value='White'/><Setter Property='FontSize' Value='13'/><Setter Property='FontWeight' Value='SemiBold'/>
  </Style>
  <Style x:Key='Secondary' TargetType='Button' BasedOn='{StaticResource {x:Type Button}}'>
   <Setter Property='Background' Value='White'/><Setter Property='BorderBrush' Value='#DADDD9'/>
  </Style>
  <Style x:Key='Quiet' TargetType='Button' BasedOn='{StaticResource {x:Type Button}}'>
   <Setter Property='Foreground' Value='{StaticResource Muted}'/><Setter Property='Padding' Value='8,0'/>
  </Style>
  <Style x:Key='Icon' TargetType='Button' BasedOn='{StaticResource {x:Type Button}}'>
   <Setter Property='Width' Value='28'/><Setter Property='Height' Value='28'/><Setter Property='Padding' Value='0'/>
   <Setter Property='FontFamily' Value='{StaticResource Icons}'/><Setter Property='FontSize' Value='13'/><Setter Property='Foreground' Value='{StaticResource Muted}'/>
  </Style>
  <Style x:Key='Segment' TargetType='Button' BasedOn='{StaticResource {x:Type Button}}'>
   <Setter Property='Foreground' Value='{StaticResource Muted}'/><Setter Property='Padding' Value='0'/>
  </Style>
  <Style x:Key='Glyph' TargetType='TextBlock'>
   <Setter Property='FontFamily' Value='{StaticResource Icons}'/><Setter Property='FontSize' Value='12'/><Setter Property='VerticalAlignment' Value='Center'/>
  </Style>
  <Style TargetType='CheckBox'>
   <Setter Property='Width' Value='26'/><Setter Property='Height' Value='28'/><Setter Property='Cursor' Value='Hand'/>
   <Setter Property='FocusVisualStyle' Value='{StaticResource FocusRing}'/>
   <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='CheckBox'>
    <Grid Background='Transparent'>
     <Border x:Name='Box' Width='18' Height='18' CornerRadius='5' BorderThickness='1.5' BorderBrush='#C5CBC6' Background='White'/>
     <Path x:Name='Tick' Width='18' Height='18' Data='M 5.2,9.4 L 7.9,12 L 12.9,6.4' Stroke='White' StrokeThickness='1.8' StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round' Visibility='Collapsed'/>
    </Grid>
    <ControlTemplate.Triggers>
     <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Box' Property='BorderBrush' Value='{StaticResource Accent}'/></Trigger>
     <Trigger Property='IsChecked' Value='True'><Setter TargetName='Box' Property='Background' Value='{StaticResource Accent}'/><Setter TargetName='Box' Property='BorderBrush' Value='{StaticResource Accent}'/><Setter TargetName='Tick' Property='Visibility' Value='Visible'/></Trigger>
     <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger>
    </ControlTemplate.Triggers>
   </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style x:Key='Field' TargetType='TextBox'>
   <Setter Property='Background' Value='White'/><Setter Property='BorderBrush' Value='#DADDD9'/><Setter Property='BorderThickness' Value='1'/>
   <Setter Property='Foreground' Value='{StaticResource Ink}'/><Setter Property='CaretBrush' Value='{StaticResource Accent}'/><Setter Property='SelectionBrush' Value='{StaticResource Accent}'/>
   <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='TextBox'>
    <Border x:Name='Frame' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='8'>
     <ScrollViewer x:Name='PART_ContentHost' Focusable='False' Margin='{TemplateBinding Padding}' VerticalAlignment='{TemplateBinding VerticalContentAlignment}'/>
    </Border>
    <ControlTemplate.Triggers>
     <Trigger Property='IsReadOnly' Value='True'><Setter TargetName='Frame' Property='Background' Value='#F7F8F6'/></Trigger>
     <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Frame' Property='BorderBrush' Value='{StaticResource Accent}'/></Trigger>
    </ControlTemplate.Triggers>
   </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style TargetType='ScrollBar'>
   <Setter Property='Width' Value='8'/><Setter Property='MinWidth' Value='8'/>
   <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'>
    <Track x:Name='PART_Track' IsDirectionReversed='True'>
     <Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType='Thumb'><Border Background='#D3D8D4' CornerRadius='3' Margin='2,0'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
    </Track>
   </ControlTemplate></Setter.Value></Setter>
   <Style.Triggers><Trigger Property='Orientation' Value='Horizontal'><Setter Property='Visibility' Value='Collapsed'/></Trigger></Style.Triggers>
  </Style>
  <Style TargetType='ToolTip'>
   <Setter Property='Foreground' Value='White'/><Setter Property='FontSize' Value='11'/>
   <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ToolTip'>
    <Border Background='#1C2420' CornerRadius='5' Padding='8,4'><ContentPresenter/></Border>
   </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style TargetType='DataGridColumnHeader'>
   <Setter Property='Foreground' Value='{StaticResource Faint}'/><Setter Property='FontSize' Value='11'/>
   <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='DataGridColumnHeader'>
    <Border BorderBrush='{StaticResource Line}' BorderThickness='0,0,0,1' Padding='6,0,6,6'><ContentPresenter/></Border>
   </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style TargetType='DataGridCell'>
   <Setter Property='Foreground' Value='{StaticResource Ink}'/>
   <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='DataGridCell'>
    <ContentPresenter Margin='6,6' VerticalAlignment='Center'/>
   </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style TargetType='DataGridRow'>
   <Setter Property='Background' Value='Transparent'/>
   <Style.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter Property='Background' Value='#F5F7F5'/></Trigger></Style.Triggers>
  </Style>
 </Window.Resources>
 <Border x:Name='Card' Margin='10' CornerRadius='14' Background='#FDFDFC' BorderBrush='#E2E5E1' BorderThickness='1'>
  <Border.Effect><DropShadowEffect Color='#18231E' BlurRadius='18' ShadowDepth='3' Direction='270' Opacity='0.13'/></Border.Effect>
  <Border.RenderTransform><TranslateTransform x:Name='Bounce'/></Border.RenderTransform>
  <Grid>
   <ScrollViewer VerticalScrollBarVisibility='Auto' HorizontalScrollBarVisibility='Disabled'>
   <StackPanel Margin='16,12,16,12'>
    <StackPanel x:Name='FullPanel'>
     <Grid x:Name='Header' Height='28' Background='Transparent' Cursor='SizeAll'>
      <Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width='Auto'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions>
      <StackPanel Orientation='Horizontal' VerticalAlignment='Center'>
       <Image x:Name='AppMark' Width='18' Height='18' Margin='0,0,8,0' RenderOptions.BitmapScalingMode='HighQuality'/>
       <TextBlock Text='文献专注' FontSize='13' FontWeight='SemiBold' VerticalAlignment='Center'/>
      </StackPanel>
      <Button x:Name='SoundButton' Grid.Column='1' Style='{StaticResource Icon}' Content='&#xE767;' Margin='0,0,2,0' AutomationProperties.Name='提示音'/>
      <Button x:Name='CloseButton' Grid.Column='2' Style='{StaticResource Icon}' Content='&#xE8BB;' FontSize='10' ToolTip='退出' AutomationProperties.Name='退出'/>
     </Grid>
     <Border x:Name='StatsPanel' Background='{StaticResource Fill}' CornerRadius='10' Padding='0,10,0,9' Margin='0,12,0,0'>
      <Grid>
       <Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition/><ColumnDefinition/><ColumnDefinition/></Grid.ColumnDefinitions>
       <StackPanel><TextBlock x:Name='MinutesStat' Text='0' FontFamily='{StaticResource Digits}' FontSize='19' HorizontalAlignment='Center'/><TextBlock Text='今日分钟' FontSize='10.5' Foreground='{StaticResource Faint}' HorizontalAlignment='Center' Margin='0,1,0,0'/></StackPanel>
       <Border Grid.Column='1' Width='1' Background='{StaticResource Line}' HorizontalAlignment='Left' Margin='0,4'/>
       <StackPanel Grid.Column='1'><TextBlock x:Name='IntroStat' Text='0' FontFamily='{StaticResource Digits}' FontSize='19' HorizontalAlignment='Center'/><TextBlock Text='引言' FontSize='10.5' Foreground='{StaticResource Faint}' HorizontalAlignment='Center' Margin='0,1,0,0'/></StackPanel>
       <Border Grid.Column='2' Width='1' Background='{StaticResource Line}' HorizontalAlignment='Left' Margin='0,4'/>
       <StackPanel Grid.Column='2'><TextBlock x:Name='ModelStat' Text='0' FontFamily='{StaticResource Digits}' FontSize='19' HorizontalAlignment='Center'/><TextBlock Text='模型' FontSize='10.5' Foreground='{StaticResource Faint}' HorizontalAlignment='Center' Margin='0,1,0,0'/></StackPanel>
       <Border Grid.Column='3' Width='1' Background='{StaticResource Line}' HorizontalAlignment='Left' Margin='0,4'/>
       <StackPanel Grid.Column='3'><TextBlock x:Name='FindingsStat' Text='0' FontFamily='{StaticResource Digits}' FontSize='19' HorizontalAlignment='Center'/><TextBlock Text='发现' FontSize='10.5' Foreground='{StaticResource Faint}' HorizontalAlignment='Center' Margin='0,1,0,0'/></StackPanel>
      </Grid>
     </Border>
     <Grid Width='148' Height='148' Margin='0,18,0,16'>
      <Ellipse Stroke='{StaticResource Line}' StrokeThickness='3'/>
      <Path x:Name='ProgressArc' Stroke='{StaticResource Accent}' StrokeThickness='3' StrokeStartLineCap='Round' StrokeEndLineCap='Round'/>
      <StackPanel VerticalAlignment='Center'>
       <TextBlock x:Name='StateLabel' Text='准备就绪' Foreground='{StaticResource Faint}' FontSize='11' HorizontalAlignment='Center'/>
       <TextBlock x:Name='TimeLabel' Text='25:00' FontFamily='{StaticResource Digits}' FontSize='40' FontWeight='Light' HorizontalAlignment='Center' TextOptions.TextFormattingMode='Ideal'/>
       <TextBlock x:Name='DialCaption' Foreground='{StaticResource Faint}' FontSize='11' HorizontalAlignment='Center' Visibility='Collapsed'/>
      </StackPanel>
     </Grid>
     <Border x:Name='PresetRow' Background='{StaticResource Fill}' CornerRadius='9' Padding='3'>
      <Grid Height='28'>
       <Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition/><ColumnDefinition/><ColumnDefinition/></Grid.ColumnDefinitions>
       <Button x:Name='Preset25' Style='{StaticResource Segment}' Content='25 分钟'/>
       <Button x:Name='Preset45' Grid.Column='1' Style='{StaticResource Segment}' Content='45 分钟'/>
       <Button x:Name='Preset60' Grid.Column='2' Style='{StaticResource Segment}' Content='60 分钟'/>
       <Button x:Name='CustomButton' Grid.Column='3' Style='{StaticResource Segment}' Content='自定义'/>
      </Grid>
     </Border>
     <Grid x:Name='CustomRow' Visibility='Collapsed' Height='32' Margin='0,8,0,0'>
      <Grid.ColumnDefinitions><ColumnDefinition Width='72'/><ColumnDefinition/><ColumnDefinition Width='64'/></Grid.ColumnDefinitions>
      <TextBox x:Name='MinutesBox' Style='{StaticResource Field}' Text='25' MaxLength='3' HorizontalContentAlignment='Center' VerticalContentAlignment='Center' AutomationProperties.Name='自定义分钟数'/>
      <TextBlock x:Name='CustomHint' Grid.Column='1' Text='分钟（1–180）' Foreground='{StaticResource Muted}' VerticalAlignment='Center' Margin='10,0,0,0'/>
      <Button x:Name='ApplyButton' Grid.Column='2' Style='{StaticResource Secondary}' Content='确定'/>
     </Grid>
     <Grid Height='38' Margin='0,10,0,0'>
      <Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width='96'/></Grid.ColumnDefinitions>
      <Button x:Name='ToggleButton' Style='{StaticResource Primary}' Content='开始专注'/>
      <Button x:Name='StopButton' Grid.Column='1' Style='{StaticResource Secondary}' Content='结束本轮' Margin='8,0,0,0' AutomationProperties.Name='结束本轮'/>
     </Grid>
     <Border Height='1' Background='{StaticResource Line}' Margin='0,14,0,4'/>
    </StackPanel>
    <StackPanel x:Name='MiniPanel' Visibility='Collapsed'>
     <Grid x:Name='MiniHeader' Height='40' Background='Transparent' Cursor='SizeAll'>
      <Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition/><ColumnDefinition Width='Auto'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions>
      <TextBlock x:Name='MiniTime' Text='25:00' FontFamily='{StaticResource Digits}' FontSize='28' FontWeight='Light' VerticalAlignment='Center' TextOptions.TextFormattingMode='Ideal'/>
      <TextBlock x:Name='MiniNotice' Grid.Column='1' Foreground='{StaticResource Faint}' FontSize='11' VerticalAlignment='Center' Margin='10,4,8,0' TextTrimming='CharacterEllipsis'/>
      <Button x:Name='MiniNotes' Grid.Column='2' Style='{StaticResource Icon}' Content='&#xE70B;' Margin='0,0,4,0' AutomationProperties.Name='笔记'/>
      <Button x:Name='MiniToggle' Grid.Column='3' Style='{StaticResource Icon}' Content='&#xE769;' FontSize='12' Background='{StaticResource AccentSoft}' Foreground='{StaticResource Accent}' AutomationProperties.Name='暂停或继续'/>
     </Grid>
     <Grid Height='3' Margin='0,6,0,4'>
      <Border Background='{StaticResource Line}' CornerRadius='1.5'/>
      <Border Background='{StaticResource Accent}' CornerRadius='1.5' RenderTransformOrigin='0,0.5'>
       <Border.RenderTransform><ScaleTransform x:Name='MiniProgress'/></Border.RenderTransform>
      </Border>
     </Grid>
    </StackPanel>
    <Grid x:Name='PaperRow' Height='32'>
     <Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width='Auto'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions>
     <TextBlock x:Name='PaperLabel' Text='第 1 篇' Foreground='{StaticResource Muted}' VerticalAlignment='Center'/>
     <StackPanel Grid.Column='1' Orientation='Horizontal' Margin='0,0,6,0'>
      <CheckBox x:Name='Section0' ToolTip='引言' AutomationProperties.Name='引言'/>
      <CheckBox x:Name='Section1' ToolTip='模型' AutomationProperties.Name='模型'/>
      <CheckBox x:Name='Section2' ToolTip='发现' AutomationProperties.Name='发现'/>
     </StackPanel>
     <Button x:Name='NextPaper' Grid.Column='2' Style='{StaticResource Quiet}' Height='28' Padding='8,0,4,0'>
      <StackPanel Orientation='Horizontal'><TextBlock Text='下一篇'/><TextBlock Style='{StaticResource Glyph}' Text='&#xE76C;' FontSize='9' Margin='5,1,0,0'/></StackPanel>
     </Button>
    </Grid>
    <Grid x:Name='FullFooter' Height='28' Margin='0,2,0,0'>
     <Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width='Auto'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions>
     <TextBlock x:Name='FooterLabel' Foreground='{StaticResource Alert}' FontSize='11' VerticalAlignment='Center' TextTrimming='CharacterEllipsis'/>
     <Button x:Name='NotesButton' Grid.Column='1' Style='{StaticResource Quiet}' Height='28'>
      <StackPanel Orientation='Horizontal'><TextBlock Style='{StaticResource Glyph}' Text='&#xE70B;'/><TextBlock x:Name='NotesLabel' Text='笔记' Margin='6,0,0,0'/></StackPanel>
     </Button>
     <Button x:Name='WordsButton' Grid.Column='2' Style='{StaticResource Quiet}' Height='28' Padding='8,0,4,0'>
      <StackPanel Orientation='Horizontal'><TextBlock Style='{StaticResource Glyph}' Text='&#xE82D;'/><TextBlock Text='词表' Margin='6,0,0,0'/><TextBlock x:Name='WordsCount' Text='0' Foreground='{StaticResource Faint}' Margin='5,0,0,0'/></StackPanel>
     </Button>
    </Grid>
    <StackPanel x:Name='NotesPanel' Visibility='Collapsed' Margin='0,10,0,0'>
     <Grid Margin='0,0,0,6'>
      <TextBlock x:Name='NotesHeader' Text='笔记' FontWeight='SemiBold'/>
      <TextBlock x:Name='NotesSaveState' Foreground='{StaticResource Alert}' FontSize='11' HorizontalAlignment='Right'/>
     </Grid>
     <TextBox x:Name='NotesBox' Style='{StaticResource Field}' AcceptsReturn='True' AcceptsTab='True' TextWrapping='Wrap' Height='132' VerticalScrollBarVisibility='Auto' HorizontalScrollBarVisibility='Disabled' Padding='5,4' FontSize='12.5' AutomationProperties.Name='阅读笔记' SpellCheck.IsEnabled='False'/>
    </StackPanel>
    <Border x:Name='ToastPanel' Visibility='Collapsed' Background='#F2F6F3' CornerRadius='8' Padding='12,9,12,10' Margin='0,10,0,0'>
     <Grid>
      <Grid.ColumnDefinitions><ColumnDefinition Width='3'/><ColumnDefinition/></Grid.ColumnDefinitions>
      <Border Background='{StaticResource Accent}' CornerRadius='1.5'/>
      <StackPanel Grid.Column='1' Margin='10,0,0,0'>
       <TextBlock x:Name='ToastWord' FontFamily='{StaticResource Digits}' FontSize='14.5' FontWeight='SemiBold'/>
       <TextBlock x:Name='ToastMeaning' FontSize='12.5' Foreground='{StaticResource Muted}' TextWrapping='Wrap' MaxHeight='54' TextTrimming='CharacterEllipsis' Margin='0,2,0,0'/>
      </StackPanel>
     </Grid>
    </Border>
    <StackPanel x:Name='SummaryPanel' Visibility='Collapsed' Margin='0,14,0,0'>
     <Grid Margin='0,0,0,6'>
      <StackPanel Orientation='Horizontal'>
       <TextBlock Text='本轮词表' FontWeight='SemiBold'/>
       <TextBlock x:Name='SummaryCount' Foreground='{StaticResource Faint}' Margin='6,0,0,0'/>
      </StackPanel>
      <TextBlock x:Name='SummaryDate' Foreground='{StaticResource Faint}' FontSize='11' HorizontalAlignment='Right' VerticalAlignment='Center'/>
     </Grid>
     <TextBlock x:Name='SummaryEmpty' Text='计时中复制英文单词，即可加入词表。' Foreground='{StaticResource Faint}' TextWrapping='Wrap' Margin='0,2,0,2'/>
     <DataGrid x:Name='WordsGrid' AutoGenerateColumns='False' CanUserAddRows='False' CanUserDeleteRows='False' CanUserResizeColumns='False' CanUserResizeRows='False' CanUserReorderColumns='False' CanUserSortColumns='False'
      IsReadOnly='True' HeadersVisibility='Column' GridLinesVisibility='Horizontal' HorizontalGridLinesBrush='{StaticResource Line}' Background='Transparent' RowBackground='Transparent' AlternatingRowBackground='Transparent'
      BorderThickness='0' MaxHeight='196' SelectionMode='Single' HorizontalScrollBarVisibility='Disabled' VerticalScrollBarVisibility='Auto'>
      <DataGrid.Columns>
       <DataGridTextColumn Header='单词' Binding='{Binding Word}' Width='104'>
        <DataGridTextColumn.ElementStyle><Style TargetType='TextBlock'><Setter Property='FontWeight' Value='SemiBold'/><Setter Property='TextTrimming' Value='CharacterEllipsis'/><Setter Property='ToolTip' Value='{Binding Word}'/></Style></DataGridTextColumn.ElementStyle>
       </DataGridTextColumn>
       <DataGridTemplateColumn Header='释义' Width='166'>
        <DataGridTemplateColumn.CellTemplate><DataTemplate>
         <TextBlock TextWrapping='Wrap'>
          <TextBlock.Style><Style TargetType='TextBlock'>
           <Setter Property='Text' Value='{Binding Meaning}'/><Setter Property='Foreground' Value='#3D4842'/>
           <Style.Triggers><DataTrigger Binding='{Binding Status}' Value='missing'><Setter Property='Text' Value='词典未收录'/><Setter Property='Foreground' Value='#A7AEAA'/></DataTrigger></Style.Triggers>
          </Style></TextBlock.Style>
         </TextBlock>
        </DataTemplate></DataGridTemplateColumn.CellTemplate>
       </DataGridTemplateColumn>
      </DataGrid.Columns>
     </DataGrid>
     <Grid Height='30' Margin='0,10,0,0'>
      <Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width='8'/><ColumnDefinition/></Grid.ColumnDefinitions>
      <Button x:Name='CopyWords' Style='{StaticResource Secondary}'>
       <StackPanel Orientation='Horizontal'><TextBlock Style='{StaticResource Glyph}' Text='&#xE8C8;'/><TextBlock Text='复制单词' Margin='6,0,0,0'/></StackPanel>
      </Button>
      <Button x:Name='ExportButton' Grid.Column='2' Style='{StaticResource Secondary}'>
       <StackPanel Orientation='Horizontal'><TextBlock Style='{StaticResource Glyph}' Text='&#xE838;'/><TextBlock Text='打开导出文件' Margin='6,0,0,0'/></StackPanel>
      </Button>
     </Grid>
     <TextBlock x:Name='ExportStatus' Visibility='Collapsed' Foreground='{StaticResource Muted}' FontSize='11' TextWrapping='Wrap' Margin='0,8,0,0'/>
    </StackPanel>
   </StackPanel>
   </ScrollViewer>
   <Canvas x:Name='Confetti' ClipToBounds='True' IsHitTestVisible='False'/>
   <Border x:Name='AlertFrame' BorderBrush='{StaticResource Alert}' BorderThickness='2' CornerRadius='14' Margin='-1' Opacity='0' IsHitTestVisible='False'/>
  </Grid>
 </Border>
</Window>";

        public FocusWindow(bool isQa, string dataFolder)
        {
            qa = isQa;
            prefs = qa ? new Preferences() : Preferences.Load();
            store = new DataStore(dataFolder);
            dictionary = AcademicDictionary.Shared;
            store.Data.Resolve(dictionary);
            displayedSession = store.Data.Sessions.LastOrDefault();
            showSummary = displayedSession != null;
            notesOpen = displayedSession != null && !string.IsNullOrWhiteSpace(displayedSession.Notes);
            exportRoot = System.IO.Path.Combine(qa ? dataFolder : AppDomain.CurrentDomain.BaseDirectory, "词表");
            clock = new FocusClock(prefs.Minutes * 60000L, delegate { return Native.Now; });
            window = (Window)XamlReader.Parse(Markup);
            if (qa) window.Title = "文献专注 · 功能检查";
            LoadIcon();
            F<Button>("ToggleButton").Click += delegate { Toggle(); };
            F<Button>("MiniToggle").Click += delegate { Toggle(); };
            F<Button>("StopButton").Click += delegate { TryEndSession(); };
            F<Button>("CloseButton").Click += delegate { window.Close(); };
            F<Button>("NotesButton").Click += delegate { ToggleNotes(); };
            F<Button>("MiniNotes").Click += delegate { ToggleNotes(); };
            F<TextBox>("NotesBox").TextChanged += delegate { NotesChanged(); };
            F<Button>("Preset25").Click += delegate { SetMinutes(25); };
            F<Button>("Preset45").Click += delegate { SetMinutes(45); };
            F<Button>("Preset60").Click += delegate { SetMinutes(60); };
            F<Button>("CustomButton").Click += delegate
            {
                customOpen = !customOpen;
                F<TextBox>("MinutesBox").Text = prefs.Minutes.ToString();
                F<TextBlock>("CustomHint").Text = "分钟（1–180）";
                F<TextBlock>("CustomHint").Foreground = muted;
                Resize();
                if (customOpen) { F<TextBox>("MinutesBox").Focus(); F<TextBox>("MinutesBox").SelectAll(); }
            };
            F<Button>("ApplyButton").Click += delegate { ApplyCustom(); };
            F<TextBox>("MinutesBox").PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Enter) { ApplyCustom(); e.Handled = true; }
                if (e.Key == Key.Escape) { customOpen = false; Resize(); e.Handled = true; }
            };
            F<Button>("NextPaper").Click += delegate { NextPaper(); };
            for (int i = 0; i < 3; i++)
            {
                int part = i;
                CheckBox box = F<CheckBox>("Section" + i);
                box.Checked += delegate { Mark(part, true); };
                box.Unchecked += delegate { Mark(part, false); };
            }
            F<Button>("WordsButton").Click += delegate { if (clock.State == TimerState.Running) return; showSummary = !showSummary; SetCompact(false); RefreshSummary(); RefreshNotes(); };
            F<Button>("CopyWords").Click += delegate
            {
                if (displayedSession == null) return;
                try { Clipboard.SetText(string.Join(Environment.NewLine, displayedSession.Words.Select(w => w.Word))); ShowExportStatus("已复制 " + displayedSession.Words.Count + " 个单词"); }
                catch { ShowExportStatus("剪贴板被占用，请稍后再试"); }
            };
            F<Button>("ExportButton").Click += delegate
            {
                if (displayedSession == null) return;
                Export(displayedSession);
                if (!string.IsNullOrEmpty(exportFolder))
                    try { Process.Start(new ProcessStartInfo(exportFolder) { UseShellExecute = true }); } catch { }
            };
            F<Button>("SoundButton").Click += delegate { prefs.Sound = !prefs.Sound; Save(); Paint(); };
            F<Grid>("Header").MouseLeftButtonDown += Drag;
            F<Grid>("MiniHeader").MouseLeftButtonDown += Drag;
            window.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (Keyboard.FocusedElement is TextBox || Keyboard.FocusedElement is CheckBox || Keyboard.Modifiers != ModifierKeys.None) return;
                if (e.Key == Key.Space) { Toggle(); e.Handled = true; }
                if (e.Key == Key.Escape && activeSession != null && clock.State == TimerState.Running) { Toggle(); e.Handled = true; }
            };
            window.SourceInitialized += delegate { InstallListeners(); };
            window.Loaded += delegate
            {
                Resize();
                Rect area = WorkArea();
                window.MaxHeight = area.Height;
                window.Left = double.IsNaN(prefs.Left) ? area.Right - window.Width - 12 : prefs.Left;
                window.Top = double.IsNaN(prefs.Top) ? area.Top + 12 : prefs.Top;
                KeepVisible(false);
                Paint();
            };
            window.SizeChanged += delegate { if (window.IsLoaded) KeepVisible(false); };
            window.Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e)
            {
                if (clock.State == TimerState.Running) { e.Cancel = true; return; }
                if (activeSession != null) EndSession("退出", true);
                closing = true;
                Save();
                ticker.Stop();
                if (hook != IntPtr.Zero) Native.UnhookWinEvent(hook);
                IntPtr h = new WindowInteropHelper(window).Handle;
                if (clipboardListening) Native.RemoveClipboardFormatListener(h);
                if (source != null) source.RemoveHook(WindowMessage);
            };
            ticker = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            ticker.Tick += delegate { Tick(); };
            ticker.Start();
            Resize();
            RefreshSummary();
            RefreshNotes();
            Paint();
            if (displayedSession != null && displayedSession.Status == "意外退出") Export(displayedSession);
        }
        public void Show() { window.Show(); }
        private void LoadIcon()
        {
            try
            {
                using (Stream stream = typeof(FocusWindow).Assembly.GetManifestResourceStream("FocusTimer.ico"))
                {
                    var decoder = new System.Windows.Media.Imaging.IconBitmapDecoder(stream,
                        System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
                        System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                    var frames = decoder.Frames.OrderBy(frame => frame.PixelWidth).ToList();
                    var large = frames.Last();
                    large.Freeze();
                    window.Icon = large;
                    var mark = frames.FirstOrDefault(frame => frame.PixelWidth >= 48) ?? large;
                    mark.Freeze();
                    F<Image>("AppMark").Source = mark;
                }
            }
            catch { }
        }
        private void Toggle()
        {
            Settle();
            bool wasRunning = clock.State == TimerState.Running;
            if (activeSession == null)
            {
                activeSession = new SessionRecord { StartedAt = DateTime.Now, Notes = store.Data.DraftNotes ?? "" };
                store.Data.DraftNotes = "";
                store.Data.Sessions.Add(activeSession);
                displayedSession = activeSession;
                showSummary = false;
                clock.Reset();
                toastQueue.Clear();
                toastUntil = 0;
                notesOpen = false;
            }
            clock.Toggle();
            previousRemaining = clock.Remaining;
            if (clock.State == TimerState.Running) { accountWall = DateTime.Now; Sweep(); }
            clipboardSequence = Native.GetClipboardSequenceNumber();
            clipboardRetries = 0;
            redUntil = 0;
            customOpen = false;
            SetCompact(!wasRunning);
            Save();
            RefreshSummary();
            RefreshNotes();
            Paint();
        }
        private void TryEndSession()
        {
            if (activeSession != null && clock.State == TimerState.Paused) EndSession("提前结束", false);
        }
        private void ToggleNotes()
        {
            notesOpen = !notesOpen;
            RefreshNotes();
            if (notesOpen && !F<TextBox>("NotesBox").IsReadOnly)
            {
                TextBox box = F<TextBox>("NotesBox");
                box.Focus(); box.CaretIndex = box.Text.Length; box.ScrollToEnd();
            }
            KeepVisible(false);
        }
        private void NotesChanged()
        {
            if (syncingNotes || F<TextBox>("NotesBox").IsReadOnly) return;
            string text = F<TextBox>("NotesBox").Text;
            if (activeSession != null) activeSession.Notes = text;
            else store.Data.DraftNotes = text;
            notesSaveDue = Native.Now + 450;
        }
        private void RefreshNotes()
        {
            bool readOnly = activeSession == null && showSummary && displayedSession != null;
            TextBox box = F<TextBox>("NotesBox");
            box.IsReadOnly = readOnly;
            string text = activeSession != null ? activeSession.Notes : readOnly ? displayedSession.Notes : store.Data.DraftNotes;
            text = text ?? "";
            if (box.Text != text)
            {
                syncingNotes = true;
                box.Text = text;
                syncingNotes = false;
            }
            F<StackPanel>("NotesPanel").Visibility = notesOpen ? Visibility.Visible : Visibility.Collapsed;
            F<TextBlock>("NotesHeader").Text = readOnly ? "本轮笔记" : activeSession != null ? "笔记" : "下一轮笔记";
            F<TextBlock>("NotesSaveState").Text = store.Error != null ? "保存失败" : "";
            F<TextBlock>("NotesLabel").Text = notesOpen ? "收起" : "笔记";
            Button mini = F<Button>("MiniNotes");
            mini.Foreground = notesOpen ? accent : muted;
            mini.ToolTip = notesOpen ? "收起笔记" : "笔记";
        }
        private void Account()
        {
            if (activeSession == null || clock.State != TimerState.Running) return;
            long remaining = clock.Remaining;
            long elapsed = Math.Max(0, previousRemaining - remaining);
            if (elapsed > 0)
            {
                activeSession.Milliseconds += elapsed;
                accountWall = accountWall.AddMilliseconds(elapsed);
                store.Data.AddTime(accountWall, elapsed);
            }
            previousRemaining = remaining;
        }
        private void Settle()
        {
            Account();
            if (clock.PollCompletion())
            {
                EndSession("已完成", false);
                if (prefs.Sound) try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
            }
        }
        private void EndSession(string status, bool exiting)
        {
            if (activeSession == null) return;
            Account();
            SessionRecord session = activeSession;
            session.EndedAt = DateTime.Now;
            session.Status = status;
            activeSession = null;
            displayedSession = session;
            if (clock.State != TimerState.Complete) clock.Reset();
            toastQueue.Clear(); toastUntil = 0; redUntil = 0; clipboardRetries = 0;
            F<Border>("ToastPanel").Visibility = Visibility.Collapsed;
            showSummary = true;
            notesOpen = !string.IsNullOrWhiteSpace(session.Notes);
            RefreshNotes();
            if (!exiting) { SetCompact(false); RefreshSummary(); }
            Save(); Export(session); Paint();
        }
        private void Tick()
        {
            Settle();
            if (clock.State == TimerState.Running)
            {
                Protect(Native.GetForegroundWindow());
                if (clipboardRetries > 0 && Native.Now >= clipboardRetryAt) ReadClipboard();
            }
            if (Native.Now > toastUntil)
            {
                F<Border>("ToastPanel").Visibility = Visibility.Collapsed;
                if (clock.State == TimerState.Running && toastQueue.Count > 0)
                {
                    WordRecord item = toastWord = toastQueue.Dequeue();
                    bool found = item.Status == "ok";
                    F<TextBlock>("ToastWord").Text = item.Word;
                    F<TextBlock>("ToastMeaning").Text = found ? item.Meaning : "词典未收录";
                    F<TextBlock>("ToastMeaning").Foreground = found ? muted : faint;
                    toastUntil = Native.Now + 10000;
                    F<Border>("ToastPanel").Visibility = Visibility.Visible;
                }
            }
            if ((notesSaveDue > 0 && Native.Now >= notesSaveDue) || (activeSession != null && Native.Now - lastSave >= 1500)) Save();
            Paint();
        }
        private void Mark(int section, bool value)
        {
            if (syncing || activeSession == null) return;
            if (store.Data.Mark(section, value, DateTime.Now))
            {
                if (value) Celebrate();
                Save(); Paint();
            }
        }
        private void NextPaper() { store.Data.NextPaper(); Save(); Paint(); }
        private void Celebrate()
        {
            Canvas canvas = F<Canvas>("Confetti");
            if (canvas.Children.Count > 140) canvas.Children.Clear();
            Random random = new Random(Guid.NewGuid().GetHashCode());
            string[] colors = { "#2E5B49", "#7FA38F", "#D8B26A", "#A9BCD3", "#E0A497" };
            for (int i = 0; i < 38; i++)
            {
                Rectangle piece = new Rectangle { Width = random.Next(3, 7), Height = random.Next(6, 12), Fill = B(colors[i % colors.Length]), RadiusX = 1, RadiusY = 1 };
                double x = canvas.ActualWidth / 2, y = compact ? 65 : Math.Max(30, canvas.ActualHeight - 70);
                Canvas.SetLeft(piece, x); Canvas.SetTop(piece, y);
                TransformGroup transforms = new TransformGroup();
                RotateTransform rotation = new RotateTransform(random.Next(180));
                TranslateTransform move = new TranslateTransform();
                transforms.Children.Add(rotation); transforms.Children.Add(move); piece.RenderTransform = transforms;
                canvas.Children.Add(piece);
                Duration duration = TimeSpan.FromMilliseconds(1100 + random.Next(500));
                move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, random.Next(-165, 166), duration) { DecelerationRatio = 0.5 });
                DoubleAnimationUsingKeyFrames vertical = new DoubleAnimationUsingKeyFrames();
                vertical.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
                vertical.KeyFrames.Add(new EasingDoubleKeyFrame(-random.Next(35, 110), KeyTime.FromPercent(0.35), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
                vertical.KeyFrames.Add(new EasingDoubleKeyFrame(random.Next(30, 130), KeyTime.FromPercent(1), new QuadraticEase { EasingMode = EasingMode.EaseIn }));
                vertical.Duration = duration;
                move.BeginAnimation(TranslateTransform.YProperty, vertical);
                rotation.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, random.Next(180, 720), duration));
                DoubleAnimation opacity = new DoubleAnimation(1, 0, duration) { BeginTime = TimeSpan.FromMilliseconds(150) };
                opacity.Completed += delegate { canvas.Children.Remove(piece); };
                piece.BeginAnimation(UIElement.OpacityProperty, opacity);
            }
        }
        private void InstallListeners()
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            source = HwndSource.FromHwnd(handle);
            source.AddHook(WindowMessage);
            clipboardListening = Native.AddClipboardFormatListener(handle);
            foregroundEvent = delegate(IntPtr h, uint ev, IntPtr hwnd, int obj, int child, uint tid, uint ms)
            {
                if (!closing) window.Dispatcher.BeginInvoke(new Action(delegate { Protect(hwnd); }));
            };
            hook = Native.SetWinEventHook(3, 3, IntPtr.Zero, foregroundEvent, 0, 0, 0);
        }
        private IntPtr WindowMessage(IntPtr h, int message, IntPtr w, IntPtr l, ref bool handled)
        {
            if (message == 0x031D)
            {
                uint seq = Native.GetClipboardSequenceNumber();
                if (seq != clipboardSequence)
                {
                    clipboardSequence = seq;
                    clipboardRetries = 0;
                    if (clock.State == TimerState.Running) { clipboardRetries = 4; ReadClipboard(); }
                }
            }
            return IntPtr.Zero;
        }
        private void ReadClipboard()
        {
            if (activeSession == null || clock.State != TimerState.Running || clock.Remaining <= 0 || Native.GetClipboardSequenceNumber() != clipboardSequence) { clipboardRetries = 0; return; }
            IntPtr foreground = Native.GetForegroundWindow();
            string owner = Native.ProcessName(Native.GetClipboardOwner());
            if (!WindowPolicy.IsReadingApp(Native.ProcessName(foreground)) || (!string.IsNullOrEmpty(owner) && !WindowPolicy.IsReadingApp(owner))) { clipboardRetries = 0; return; }
            try
            {
                string text = Clipboard.ContainsText(TextDataFormat.UnicodeText) ? Clipboard.GetText(TextDataFormat.UnicodeText) : null;
                clipboardRetries = 0;
                Collect(text);
            }
            catch (ExternalException) { clipboardRetries--; clipboardRetryAt = Native.Now + 150; }
        }
        private void Collect(string text)
        {
            if (activeSession == null) return;
            DictionaryHit hit = WordReader.Read(text, dictionary);
            if (hit == null) return;
            WordRecord item = store.Data.AddWord(activeSession, hit.Word, hit.Meaning, DateTime.Now) ?? activeSession.Words.Find(w => w.Word == hit.Word);
            if (item == toastWord && Native.Now < toastUntil) toastUntil = Native.Now + 10000;
            else if (!toastQueue.Contains(item)) toastQueue.Enqueue(item);
            Save(); RefreshSummary(); Paint();
        }
        // Starting or resuming clears the desktop down to reading apps in one
        // pass; Protect handles anything brought forward afterwards.
        private void Sweep()
        {
            foreach (IntPtr handle in Native.AppWindows())
            {
                if (Native.IsOwn(handle) || WindowPolicy.IsSystemSurface(handle)) continue;
                string process = Native.ProcessName(handle);
                if (!WindowPolicy.ShouldBlock(process)) continue;
                // QA only targets our disposable test window; never touches the user's apps.
                if (qa && process != "focusguardprobe") continue;
                Native.ShowWindowAsync(handle, 7);
            }
        }
        private void Protect(IntPtr handle)
        {
            if (clock.State != TimerState.Running || clock.Remaining <= 0 || handle == IntPtr.Zero || handle != Native.GetForegroundWindow() || Native.IsOwn(handle) || !Native.IsWindowVisible(handle) || Native.IsIconic(handle)) return;
            string process = Native.ProcessName(handle);
            if (!WindowPolicy.ShouldBlock(process) || WindowPolicy.IsSystemSurface(handle)) return;
            // QA only targets our disposable test window; never touches the user's apps.
            if (qa && process != "focusguardprobe") return;
            if (blockedHandle == handle && Native.Now - blockedAt < 700) return;
            if (blockedHandle != handle) blockedName = Native.AppName(handle);
            blockedHandle = handle; blockedAt = Native.Now;
            Native.ShowWindowAsync(handle, 6);
            redUntil = Native.Now + 2000;
            DoubleAnimationUsingKeyFrames bounce = new DoubleAnimationUsingKeyFrames();
            double[] values = { 0, -6, 0, -3, 0 };
            for (int i = 0; i < values.Length; i++) bounce.KeyFrames.Add(new LinearDoubleKeyFrame(values[i], KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(i * 110))));
            F<TranslateTransform>("Bounce").BeginAnimation(TranslateTransform.YProperty, bounce);
        }
        private void SetMinutes(int minutes)
        {
            if (activeSession != null) return;
            prefs.Minutes = minutes; clock.SetDuration(minutes * 60000L);
            customOpen = false; Resize(); Save(); Paint();
        }
        private void ApplyCustom()
        {
            int minutes;
            if (!int.TryParse(F<TextBox>("MinutesBox").Text.Trim(), out minutes) || minutes < 1 || minutes > 180)
            { F<TextBlock>("CustomHint").Text = "请输入 1–180"; F<TextBlock>("CustomHint").Foreground = alert; return; }
            SetMinutes(minutes);
        }
        private void SetCompact(bool value) { compact = value; customOpen = false; Resize(); KeepVisible(true); }
        private void Resize()
        {
            double right = window.Left + window.Width;
            Rect area = WorkArea();
            bool atRight = !double.IsNaN(window.Left) && Math.Abs(area.Right - right) < 45;
            Visibility full = compact ? Visibility.Collapsed : Visibility.Visible;
            F<StackPanel>("FullPanel").Visibility = full;
            F<Grid>("FullFooter").Visibility = full;
            F<StackPanel>("MiniPanel").Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
            F<Grid>("CustomRow").Visibility = customOpen ? Visibility.Visible : Visibility.Collapsed;
            F<StackPanel>("SummaryPanel").Visibility = !compact && showSummary && displayedSession != null ? Visibility.Visible : Visibility.Collapsed;
            window.Width = compact ? 300 : 336;
            if (atRight) window.Left = right - window.Width;
        }
        private void Drag(object sender, MouseButtonEventArgs e)
        {
            DependencyObject item = e.OriginalSource as DependencyObject;
            while (item != null)
            {
                if (item is Button || item is CheckBox) return;
                item = VisualTreeHelper.GetParent(item);
            }
            try { window.DragMove(); } catch (InvalidOperationException) { }
            KeepVisible(true); Save(); e.Handled = true;
        }
        private Rect WorkArea()
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return SystemParameters.WorkArea;
            System.Drawing.Rectangle r = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
            HwndSource hwnd = HwndSource.FromHwnd(handle);
            if (hwnd == null || hwnd.CompositionTarget == null) return SystemParameters.WorkArea;
            Matrix m = hwnd.CompositionTarget.TransformFromDevice;
            return new Rect(m.Transform(new Point(r.Left, r.Top)), m.Transform(new Point(r.Right, r.Bottom)));
        }
        private void KeepVisible(bool snap)
        {
            if (!window.IsLoaded) return;
            Rect area = WorkArea();
            double h = window.ActualHeight > 0 ? window.ActualHeight : 550;
            double x = double.IsNaN(window.Left) ? area.Right - window.Width - 12 : window.Left;
            double y = double.IsNaN(window.Top) ? area.Top + 12 : window.Top;
            x = Math.Max(area.Left, Math.Min(x, area.Right - window.Width));
            y = Math.Max(area.Top, Math.Min(y, area.Bottom - h));
            if (snap)
            {
                if (Math.Abs(x - area.Left) < 22) x = area.Left;
                if (Math.Abs(area.Right - x - window.Width) < 22) x = area.Right - window.Width;
                if (Math.Abs(y - area.Top) < 22) y = area.Top;
                if (Math.Abs(area.Bottom - y - h) < 22) y = area.Bottom - h;
            }
            window.Left = x; window.Top = y;
        }
        private void Save()
        {
            bool saved = store.Save(); lastSave = Native.Now;
            if (saved) notesSaveDue = 0;
            else if (notesSaveDue > 0) notesSaveDue = Native.Now + 3000;
            F<TextBlock>("NotesSaveState").Text = saved ? "" : "保存失败";
            if (qa) return;
            prefs.Left = window.Left; prefs.Top = window.Top; prefs.Compact = false; prefs.Save();
        }
        private void Export(SessionRecord session)
        {
            try { exportFolder = WordExport.Export(session, exportRoot); }
            catch
            {
                try { exportFolder = WordExport.Export(session, System.IO.Path.Combine(Preferences.Folder, "词表")); }
                catch { exportFolder = null; ShowExportStatus("词表导出失败，可稍后再试"); return; }
            }
            F<Button>("ExportButton").ToolTip = exportFolder;
        }
        private void ShowExportStatus(string text)
        {
            TextBlock status = F<TextBlock>("ExportStatus");
            status.Text = text;
            status.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }
        private void RefreshSummary()
        {
            if (displayedSession != null)
            {
                DataGrid grid = F<DataGrid>("WordsGrid");
                int count = displayedSession.Words.Count;
                grid.ItemsSource = null;
                grid.ItemsSource = displayedSession.Words;
                grid.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
                F<TextBlock>("SummaryEmpty").Visibility = count > 0 ? Visibility.Collapsed : Visibility.Visible;
                F<TextBlock>("SummaryCount").Text = count > 0 ? count.ToString() : "";
                F<TextBlock>("SummaryDate").Text = displayedSession == activeSession ? "" : displayedSession.StartedAt.ToString("MM-dd HH:mm");
                F<Button>("CopyWords").IsEnabled = count > 0;
                ShowExportStatus("");
            }
            Resize();
        }
        private void Paint()
        {
            bool running = clock.State == TimerState.Running;
            bool paused = clock.State == TimerState.Paused;
            bool done = clock.State == TimerState.Complete;
            bool ended = showSummary && displayedSession != null && activeSession == null;
            long remaining = clock.Remaining;
            string time = FocusClock.Display(remaining);
            F<TextBlock>("TimeLabel").Text = time; F<TextBlock>("MiniTime").Text = time;
            F<TextBlock>("TimeLabel").Foreground = done ? amber : ink;
            F<TextBlock>("StateLabel").Text = running ? "专注中" : paused ? "已暂停" : done ? "已完成" : ended ? "本轮结束" : "准备就绪";
            TextBlock caption = F<TextBlock>("DialCaption");
            caption.Text = running ? DateTime.Now.AddMilliseconds(remaining).ToString("HH:mm") + " 结束" : "";
            caption.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
            F<Button>("ToggleButton").Content = running ? "暂停" : paused ? "继续专注" : done ? "再读一轮" : "开始专注";
            Button miniToggle = F<Button>("MiniToggle");
            miniToggle.Content = running ? "\uE769" : "\uE768";
            miniToggle.ToolTip = running ? "暂停" : "继续";
            F<Button>("StopButton").IsEnabled = activeSession != null && paused;
            F<Button>("CloseButton").IsEnabled = !running;
            F<Button>("WordsButton").IsEnabled = !running;
            F<Border>("PresetRow").IsEnabled = activeSession == null;
            F<Border>("StatsPanel").Visibility = running ? Visibility.Collapsed : Visibility.Visible;
            Button sound = F<Button>("SoundButton");
            sound.Content = prefs.Sound ? "\uE767" : "\uE74F";
            sound.ToolTip = prefs.Sound ? "提示音：开" : "提示音：关";
            F<TextBlock>("FooterLabel").Text = store.Error ?? "";
            F<TextBlock>("FooterLabel").ToolTip = store.Error;
            int wordCount = displayedSession == null ? 0 : displayedSession.Words.Count;
            F<TextBlock>("WordsCount").Text = wordCount.ToString();
            bool alerting = Native.Now < redUntil;
            TextBlock notice = F<TextBlock>("MiniNotice");
            notice.Text = alerting ? "已最小化 " + blockedName : wordCount > 0 ? wordCount + " 词" : "";
            notice.Foreground = alerting ? alert : faint;
            DateTime now = DateTime.Now;
            F<TextBlock>("MinutesStat").Text = Math.Floor(store.Data.TodayTime(now) / 60000.0).ToString("0");
            F<TextBlock>("IntroStat").Text = store.Data.TodayCount(0, now).ToString();
            F<TextBlock>("ModelStat").Text = store.Data.TodayCount(1, now).ToString();
            F<TextBlock>("FindingsStat").Text = store.Data.TodayCount(2, now).ToString();
            F<TextBlock>("PaperLabel").Text = "第 " + store.Data.CurrentPaper.Number + " 篇";
            syncing = true;
            for (int i = 0; i < 3; i++)
            {
                CheckBox box = F<CheckBox>("Section" + i);
                box.IsChecked = !string.IsNullOrEmpty(store.Data.CurrentPaper.MarkDates[i]);
                box.IsEnabled = activeSession != null;
            }
            syncing = false;
            int[] minutes = { 25, 45, 60 };
            bool custom = Array.IndexOf(minutes, prefs.Minutes) < 0;
            for (int i = 0; i < 4; i++)
            {
                Button b = F<Button>(i < 3 ? "Preset" + minutes[i] : "CustomButton");
                bool selected = i < 3 ? prefs.Minutes == minutes[i] : custom;
                b.Background = selected ? Brushes.White : Brushes.Transparent;
                b.Foreground = selected ? ink : muted;
                b.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
                b.Effect = selected ? raised : null;
            }
            F<Button>("CustomButton").Content = custom ? prefs.Minutes + " 分钟" : "自定义";
            double fraction = Math.Max(0, Math.Min(1, (double)remaining / clock.Duration));
            F<ScaleTransform>("MiniProgress").ScaleX = fraction;
            Path arc = F<Path>("ProgressArc");
            arc.Stroke = done ? amber : accent;
            const double center = 74, radius = 72.5;
            if (fraction <= 0) arc.Data = Geometry.Empty;
            else if (fraction >= 0.9999) arc.Data = new EllipseGeometry(new Point(center, center), radius, radius);
            else
            {
                double angle = fraction * 2 * Math.PI - Math.PI / 2;
                PathFigure figure = new PathFigure { StartPoint = new Point(center, center - radius) };
                figure.Segments.Add(new ArcSegment(new Point(center + radius * Math.Cos(angle), center + radius * Math.Sin(angle)), new Size(radius, radius), 0, fraction > 0.5, SweepDirection.Clockwise, true));
                PathGeometry geometry = new PathGeometry(); geometry.Figures.Add(figure); arc.Data = geometry;
            }
            F<Border>("AlertFrame").Opacity = alerting && (Native.Now / 180) % 2 == 0 ? 1 : 0;
            F<Border>("Card").BorderBrush = done ? cardDone : cardLine;
        }

        [STAThread]
        public static int Main(string[] args)
        {
#if TESTING
            if (args.Length > 0 && args[0] == "--self-test") return Checks.Run(args.Length > 1 ? args[1] : AppDomain.CurrentDomain.BaseDirectory);
            if (args.Length > 1 && args[0] == "--flow-test") return FlowTest(args[1]);
            if (args.Length > 2 && args[0] == "--sweep-test") return SweepTest(args[1], args[2]);
            bool qa = args.Length > 1 && args[0] == "--qa";
            string dataFolder = qa ? System.IO.Path.GetFullPath(args[1]) : Preferences.Folder;
#else
            bool qa = false;
            string dataFolder = Preferences.Folder;
#endif
            bool first;
            using (Mutex mutex = new Mutex(true, "Local\\ReadingFocusTimer.EB8B38D1" + (qa ? ".QA" : ""), out first))
            {
                if (!first)
                {
                    foreach (Process p in Process.GetProcessesByName(Process.GetCurrentProcess().ProcessName))
                    {
                        if (p.Id == Process.GetCurrentProcess().Id || p.MainWindowHandle == IntPtr.Zero) continue;
                        Native.ShowWindowAsync(p.MainWindowHandle, 9); Native.SetForegroundWindow(p.MainWindowHandle); break;
                    }
                    return 0;
                }
                try
                {
                    Application app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                    FocusWindow widget = new FocusWindow(qa, dataFolder); widget.Show(); return app.Run();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("计时器暂时无法启动。\n\n" + ex.Message, "文献专注", MessageBoxButton.OK, MessageBoxImage.Information); return 1;
                }
                finally { mutex.ReleaseMutex(); }
            }
        }
    }
}
