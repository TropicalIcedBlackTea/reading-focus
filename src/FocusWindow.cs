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
        private readonly DispatcherTimer ticker;
        private readonly TranslationQueue translations = new TranslationQueue();
        private Func<string, TranslationResult> translateWord = Translator.Translate;
        private readonly Queue<WordRecord> toastQueue = new Queue<WordRecord>();
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
        private Native.WinEvent foregroundEvent;
        private bool clipboardListening;
        private HwndSource source;
        private string exportFolder;
        private readonly Brush forest = B("#315C48"), amber = B("#AD742D"), subtle = B("#718274");
        private static Brush B(string s) { return (Brush)new BrushConverter().ConvertFromString(s); }
        private T F<T>(string name) where T : class { return window.FindName(name) as T; }

        private const string Markup = @"
<Window xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
 Title='文献专注 · 悬浮倒计时器' Width='354' SizeToContent='Height' WindowStyle='None' AllowsTransparency='True'
 Background='Transparent' ResizeMode='NoResize' Topmost='True' FontFamily='Microsoft YaHei UI, Segoe UI'
 FontSize='12' Foreground='#293F33' UseLayoutRounding='True' SnapsToDevicePixels='True'
 TextOptions.TextFormattingMode='Display' TextOptions.TextRenderingMode='Grayscale'>
 <Window.Resources>
  <Style TargetType='Button'>
   <Setter Property='Cursor' Value='Hand'/><Setter Property='Background' Value='#EEF2EC'/><Setter Property='Foreground' Value='#53675B'/>
   <Setter Property='BorderBrush' Value='Transparent'/><Setter Property='BorderThickness' Value='1'/><Setter Property='Padding' Value='6,0'/>
   <Setter Property='HorizontalContentAlignment' Value='Center'/><Setter Property='VerticalContentAlignment' Value='Center'/>
   <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>
    <Border x:Name='B' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='8' Padding='{TemplateBinding Padding}'>
     <ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='{TemplateBinding VerticalContentAlignment}'/>
    </Border>
    <ControlTemplate.Triggers>
     <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='B' Property='Opacity' Value='0.75'/></Trigger>
     <Trigger Property='IsPressed' Value='True'><Setter TargetName='B' Property='Opacity' Value='0.5'/></Trigger>
     <Trigger Property='IsEnabled' Value='False'><Setter TargetName='B' Property='Opacity' Value='0.35'/></Trigger>
     <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='B' Property='BorderBrush' Value='#95AE9A'/></Trigger>
    </ControlTemplate.Triggers>
   </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style x:Key='Chrome' TargetType='Button' BasedOn='{StaticResource {x:Type Button}}'>
   <Setter Property='Width' Value='26'/><Setter Property='Height' Value='26'/><Setter Property='Padding' Value='0'/><Setter Property='Background' Value='Transparent'/>
  </Style>
  <Style TargetType='CheckBox'>
   <Setter Property='Width' Value='29'/><Setter Property='Height' Value='28'/><Setter Property='Cursor' Value='Hand'/>
   <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='CheckBox'>
    <Grid Width='21' Height='21'>
     <Border x:Name='Box' CornerRadius='5' BorderThickness='1.4' BorderBrush='#A7BAA1' Background='#F2F6EE'/>
     <Path x:Name='Tick' Data='M 5,10 L 9,14 L 16,6' Stroke='White' StrokeThickness='2' Visibility='Collapsed'/>
    </Grid>
    <ControlTemplate.Triggers>
     <Trigger Property='IsChecked' Value='True'><Setter TargetName='Box' Property='Background' Value='#5D825C'/><Setter TargetName='Box' Property='BorderBrush' Value='#5D825C'/><Setter TargetName='Tick' Property='Visibility' Value='Visible'/></Trigger>
     <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Box' Property='BorderBrush' Value='#315C48'/></Trigger>
     <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Box' Property='BorderBrush' Value='#315C48'/><Setter TargetName='Box' Property='BorderThickness' Value='2'/></Trigger>
     <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger>
    </ControlTemplate.Triggers>
   </ControlTemplate></Setter.Value></Setter>
  </Style>
 </Window.Resources>
 <Border x:Name='Card' Margin='10' CornerRadius='18' Background='#FAFCF8' BorderBrush='#DDE6D9' BorderThickness='1.5'>
  <Border.Effect><DropShadowEffect Color='#263E2B' BlurRadius='15' ShadowDepth='2' Opacity='0.16'/></Border.Effect>
  <Border.RenderTransform><TranslateTransform x:Name='Bounce'/></Border.RenderTransform>
  <Grid>
   <ScrollViewer VerticalScrollBarVisibility='Auto' HorizontalScrollBarVisibility='Disabled'>
   <StackPanel Margin='16,13,16,14'>
    <StackPanel x:Name='FullPanel'>
     <Grid x:Name='Header' Height='26' Background='Transparent' Cursor='SizeAll' ToolTip='拖动移动窗口'>
      <Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width='26'/></Grid.ColumnDefinitions>
      <StackPanel Orientation='Horizontal' VerticalAlignment='Center'>
       <Border Width='22' Height='22' CornerRadius='6' Background='#DFEADC' Margin='0,0,9,0'><TextBlock Text='读' FontSize='12' FontWeight='SemiBold' HorizontalAlignment='Center' VerticalAlignment='Center'/></Border>
       <TextBlock Text='文献专注' FontSize='14' FontWeight='SemiBold' VerticalAlignment='Center'/>
      </StackPanel>
      <Button x:Name='CloseButton' Grid.Column='1' Style='{StaticResource Chrome}' Content='×' FontSize='20' ToolTip='退出并保存本轮' AutomationProperties.Name='退出计时器'/>
     </Grid>
     <Border x:Name='StatsPanel' Background='#EFF4EB' CornerRadius='11' Padding='8,9' Margin='0,12,0,1'>
      <Grid>
       <Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition/><ColumnDefinition/><ColumnDefinition/></Grid.ColumnDefinitions>
       <StackPanel><TextBlock x:Name='MinutesStat' Text='0.0' FontFamily='Segoe UI' FontSize='24' HorizontalAlignment='Center'/><TextBlock Text='今日分钟' FontSize='10' Foreground='#718274' HorizontalAlignment='Center'/></StackPanel>
       <StackPanel Grid.Column='1'><TextBlock x:Name='IntroStat' Text='0' FontFamily='Segoe UI' FontSize='24' HorizontalAlignment='Center'/><TextBlock Text='引言' FontSize='10' Foreground='#718274' HorizontalAlignment='Center'/></StackPanel>
       <StackPanel Grid.Column='2'><TextBlock x:Name='ModelStat' Text='0' FontFamily='Segoe UI' FontSize='24' HorizontalAlignment='Center'/><TextBlock Text='模型' FontSize='10' Foreground='#718274' HorizontalAlignment='Center'/></StackPanel>
       <StackPanel Grid.Column='3'><TextBlock x:Name='FindingsStat' Text='0' FontFamily='Segoe UI' FontSize='24' HorizontalAlignment='Center'/><TextBlock Text='发现' FontSize='10' Foreground='#718274' HorizontalAlignment='Center'/></StackPanel>
      </Grid>
     </Border>
     <Grid Width='154' Height='154' Margin='0,10,0,10'>
      <Ellipse Stroke='#E4EBE0' StrokeThickness='4'/><Path x:Name='ProgressArc' Stroke='#7E9D7D' StrokeThickness='4' StrokeStartLineCap='Round' StrokeEndLineCap='Round'/>
      <StackPanel VerticalAlignment='Center'>
       <TextBlock x:Name='StateLabel' Text='准备阅读' Foreground='#718274' FontSize='11' HorizontalAlignment='Center'/>
       <TextBlock x:Name='TimeLabel' Text='25:00' FontFamily='Segoe UI' FontSize='37' FontWeight='Light' HorizontalAlignment='Center'/>
       <TextBlock x:Name='DialCaption' Text='留一段时间，读懂一篇' Foreground='#718274' FontSize='10.5' HorizontalAlignment='Center' Margin='0,4,0,0'/>
      </StackPanel>
     </Grid>
     <Grid Height='38'>
      <Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width='94'/></Grid.ColumnDefinitions>
      <Button x:Name='ToggleButton' Content='开始专注' Background='#315C48' Foreground='White' FontSize='13' FontWeight='SemiBold'/>
      <Button x:Name='StopButton' Grid.Column='1' Content='结束本轮' Margin='8,0,0,0' FontSize='11' ToolTip='暂停后结束并汇总' AutomationProperties.Name='结束本轮'/>
     </Grid>
     <Grid x:Name='PresetRow' Height='28' Margin='0,12,0,0'>
      <Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition/><ColumnDefinition/><ColumnDefinition/></Grid.ColumnDefinitions>
      <Button x:Name='Preset25' Content='25 分' Margin='0,0,5,0'/><Button x:Name='Preset45' Content='45 分' Grid.Column='1' Margin='0,0,5,0'/>
      <Button x:Name='Preset60' Content='60 分' Grid.Column='2' Margin='0,0,5,0'/><Button x:Name='CustomButton' Content='自定义' Grid.Column='3'/>
     </Grid>
     <Grid x:Name='CustomRow' Visibility='Collapsed' Height='30' Margin='0,9,0,0'>
      <Grid.ColumnDefinitions><ColumnDefinition Width='65'/><ColumnDefinition/><ColumnDefinition Width='48'/></Grid.ColumnDefinitions>
      <TextBox x:Name='MinutesBox' Text='25' MaxLength='3' VerticalContentAlignment='Center' HorizontalContentAlignment='Center' Background='White' BorderBrush='#BDCFBA' AutomationProperties.Name='自定义分钟数'/>
      <TextBlock x:Name='CustomHint' Grid.Column='1' Text='分钟 · 1–180' FontSize='11' Foreground='#718274' VerticalAlignment='Center' Margin='9,0,0,0'/>
      <Button x:Name='ApplyButton' Grid.Column='2' Content='确定'/>
     </Grid>
     <Grid Height='31' Margin='0,12,0,0'>
      <Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width='90'/><ColumnDefinition Width='65'/></Grid.ColumnDefinitions>
      <TextBlock x:Name='PaperLabel' Text='第 1 篇' VerticalAlignment='Center' Foreground='#718274'/>
      <StackPanel Grid.Column='1' Orientation='Horizontal'>
       <CheckBox x:Name='Section0' AutomationProperties.Name='第一节'/><CheckBox x:Name='Section1' AutomationProperties.Name='第二节'/><CheckBox x:Name='Section2' AutomationProperties.Name='第三节'/>
      </StackPanel>
      <Button x:Name='NextPaper' Grid.Column='2' Content='下一篇 →' Height='27' FontSize='11' ToolTip='保留已读记录，开始记录下一篇'/>
     </Grid>
     <Grid Height='24' Margin='0,10,0,0'>
      <Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width='48'/><ColumnDefinition Width='61'/><ColumnDefinition Width='67'/></Grid.ColumnDefinitions>
      <TextBlock x:Name='FooterLabel' Text='今日统计自动保存' FontSize='10.5' Foreground='#718274' VerticalAlignment='Center'/>
      <Button x:Name='NotesButton' Grid.Column='1' Content='笔记' FontSize='11' Background='Transparent'/>
      <Button x:Name='WordsButton' Grid.Column='2' Content='词表 0' FontSize='11' Background='Transparent'/>
      <Button x:Name='SoundButton' Grid.Column='3' Content='提示音 开' FontSize='11' Background='Transparent'/>
     </Grid>
    </StackPanel>
    <StackPanel x:Name='MiniPanel' Visibility='Collapsed'>
     <Grid x:Name='MiniHeader' Height='43' Background='Transparent' Cursor='SizeAll'>
      <Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width='57'/><ColumnDefinition Width='32'/></Grid.ColumnDefinitions>
      <TextBlock x:Name='MiniTime' Text='25:00' FontFamily='Segoe UI' FontSize='30' FontWeight='Light' VerticalAlignment='Center'/>
      <Button x:Name='MiniNotes' Grid.Column='1' Width='50' Height='29' Content='笔记' FontSize='11' Padding='0' ToolTip='展开或收起笔记'/>
      <Button x:Name='MiniToggle' Grid.Column='2' Width='29' Height='29' Content='Ⅱ' Background='#DFEADC' Padding='0' AutomationProperties.Name='暂停或继续'/>
     </Grid>
     <Grid Height='29' Margin='0,4,0,0'>
      <Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width='90'/><ColumnDefinition Width='65'/></Grid.ColumnDefinitions>
      <TextBlock x:Name='MiniPaper' Text='第 1 篇' VerticalAlignment='Center' Foreground='#718274' FontSize='11'/>
      <StackPanel Grid.Column='1' Orientation='Horizontal'>
       <CheckBox x:Name='MiniSection0' AutomationProperties.Name='第一节'/><CheckBox x:Name='MiniSection1' AutomationProperties.Name='第二节'/><CheckBox x:Name='MiniSection2' AutomationProperties.Name='第三节'/>
      </StackPanel>
      <Button x:Name='MiniNext' Grid.Column='2' Content='下一篇 →' Height='26' FontSize='11' Padding='3,0'/>
     </Grid>
     <TextBlock x:Name='MiniState' Text='专注保护中 · 复制单词自动收集' Foreground='#718274' FontSize='10.5' Margin='0,8,0,0' TextWrapping='Wrap'/>
    </StackPanel>
    <Border x:Name='NotesPanel' Visibility='Collapsed' Background='#EEF3E9' CornerRadius='10' Padding='10,9' Margin='0,10,0,0'>
     <StackPanel>
      <Grid Margin='0,0,0,7'><Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width='67'/></Grid.ColumnDefinitions>
       <TextBlock x:Name='NotesHeader' Text='随手笔记' FontWeight='SemiBold' FontSize='12'/>
       <TextBlock x:Name='NotesSaveState' Grid.Column='1' Text='自动保存' Foreground='#718274' FontSize='10' TextAlignment='Right' VerticalAlignment='Center'/>
      </Grid>
      <TextBox x:Name='NotesBox' AcceptsReturn='True' AcceptsTab='True' TextWrapping='Wrap' Height='126' VerticalScrollBarVisibility='Auto' HorizontalScrollBarVisibility='Disabled' Padding='8' Background='#FEFFFC' BorderBrush='#CDDAC6' BorderThickness='1' FontSize='12' Foreground='#293F33' AutomationProperties.Name='本轮阅读笔记' SpellCheck.IsEnabled='False'/>
     </StackPanel>
    </Border>
    <Border x:Name='ToastPanel' Visibility='Collapsed' Background='#EAF1E4' CornerRadius='10' Padding='11,8' Margin='0,10,0,0'>
     <StackPanel><TextBlock x:Name='ToastWord' FontFamily='Segoe UI' FontWeight='SemiBold' FontSize='15'/><TextBlock x:Name='ToastMeaning' FontSize='13' TextWrapping='Wrap' MaxHeight='58' Margin='0,3,0,0'/><TextBlock Text='已收录 · 10 秒后收起' Foreground='#718274' FontSize='10' Margin='0,4,0,0'/></StackPanel>
    </Border>
    <StackPanel x:Name='SummaryPanel' Visibility='Collapsed' Margin='0,12,0,0'>
     <Border Height='1' Background='#E1E8DD' Margin='0,0,0,10'/>
     <TextBlock x:Name='SummaryTitle' Text='本轮单词' FontWeight='SemiBold' FontSize='12'/>
     <TextBlock x:Name='SummaryStatus' Foreground='#718274' FontSize='10.5' Margin='0,4,0,7' TextWrapping='Wrap'/>
     <DataGrid x:Name='WordsGrid' AutoGenerateColumns='False' CanUserAddRows='False' CanUserDeleteRows='False' IsReadOnly='True' HeadersVisibility='Column' GridLinesVisibility='Horizontal' MaxHeight='157' MinHeight='48' Background='#FAFCF8' BorderThickness='0' RowBackground='#FAFCF8' AlternatingRowBackground='#F0F4ED' HorizontalGridLinesBrush='#E5EBE0' FontSize='11' SelectionMode='Single'>
      <DataGrid.Columns>
       <DataGridTextColumn Header='单词' Binding='{Binding Word}' Width='104'><DataGridTextColumn.ElementStyle><Style TargetType='TextBlock'><Setter Property='TextTrimming' Value='CharacterEllipsis'/><Setter Property='ToolTip' Value='{Binding Word}'/><Setter Property='Margin' Value='3,3'/></Style></DataGridTextColumn.ElementStyle></DataGridTextColumn>
       <DataGridTemplateColumn Header='中文释义' Width='172'><DataGridTemplateColumn.CellTemplate><DataTemplate><TextBlock Text='{Binding Meaning}' TextWrapping='Wrap' Margin='3,3'/></DataTemplate></DataGridTemplateColumn.CellTemplate></DataGridTemplateColumn>
      </DataGrid.Columns>
     </DataGrid>
     <Grid Height='28' Margin='0,8,0,0'><Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition/><ColumnDefinition/></Grid.ColumnDefinitions>
      <Button x:Name='RetryButton' Content='重新翻译' FontSize='11' Margin='0,0,5,0'/>
      <Button x:Name='CopyWords' Grid.Column='1' Content='复制纯单词' FontSize='11' Margin='0,0,5,0'/>
      <Button x:Name='ExportButton' Grid.Column='2' Content='导出词表 ↗' FontSize='11' Background='#DFEADC'/>
     </Grid>
     <TextBlock x:Name='ExportStatus' Text='TXT 纯单词 · CSV / TSV 中英词表' Foreground='#718274' FontSize='10' Margin='0,7,0,0' TextWrapping='Wrap'/>
    </StackPanel>
   </StackPanel>
   </ScrollViewer>
   <Canvas x:Name='Confetti' ClipToBounds='True' IsHitTestVisible='False'/>
  </Grid>
 </Border>
</Window>";

        public FocusWindow(bool isQa, string dataFolder)
        {
            qa = isQa;
            prefs = qa ? new Preferences() : Preferences.Load();
            store = new DataStore(dataFolder);
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
                F<TextBlock>("CustomHint").Text = "分钟 · 1–180";
                F<TextBlock>("CustomHint").Foreground = subtle;
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
            F<Button>("MiniNext").Click += delegate { NextPaper(); };
            for (int i = 0; i < 3; i++)
            {
                int part = i;
                foreach (string prefix in new[] { "Section", "MiniSection" })
                {
                    CheckBox box = F<CheckBox>(prefix + i);
                    box.Checked += delegate { Mark(part, true); };
                    box.Unchecked += delegate { Mark(part, false); };
                }
            }
            F<Button>("WordsButton").Click += delegate { if (clock.State == TimerState.Running) return; showSummary = !showSummary; SetCompact(false); RefreshSummary(); RefreshNotes(); };
            F<Button>("RetryButton").Click += delegate { RetryFailed(); };
            F<Button>("CopyWords").Click += delegate
            {
                if (displayedSession == null) return;
                try { Clipboard.SetText(string.Join(Environment.NewLine, displayedSession.Words.Select(w => w.Word))); F<TextBlock>("ExportStatus").Text = "已复制纯单词，可粘贴到扇贝支持的添加入口。"; }
                catch { F<TextBlock>("ExportStatus").Text = "剪贴板正忙，请稍后再试。"; }
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
                    var bitmap = decoder.Frames.OrderByDescending(frame => frame.PixelWidth).First();
                    bitmap.Freeze();
                    window.Icon = bitmap;
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
            if (clock.State == TimerState.Running) accountWall = DateTime.Now;
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
            F<TextBlock>("NotesSaveState").Text = "保存中…";
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
            F<Border>("NotesPanel").Visibility = notesOpen ? Visibility.Visible : Visibility.Collapsed;
            F<TextBlock>("NotesHeader").Text = readOnly ? "本轮笔记" : activeSession != null ? "随手笔记" : "下一轮笔记";
            F<TextBlock>("NotesSaveState").Text = notesSaveDue > 0 ? "保存中…" : store.Error != null ? "保存失败" : readOnly ? "已保存" : "自动保存";
            F<Button>("NotesButton").Content = notesOpen ? "收起" : "笔记";
            F<Button>("MiniNotes").Content = notesOpen ? "收起" : "笔记";
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
                    WordRecord item = toastQueue.Dequeue();
                    F<TextBlock>("ToastWord").Text = item.Word;
                    F<TextBlock>("ToastMeaning").Text = item.Meaning;
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
            string[] colors = { "#6D9872", "#D7AA55", "#B8C9E1", "#E6A0A0", "#B0C58C" };
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
                string word = Clipboard.ContainsText(TextDataFormat.UnicodeText) ? ReadingData.NormalizeWord(Clipboard.GetText(TextDataFormat.UnicodeText)) : null;
                clipboardRetries = 0;
                if (word == null) return;
                WordRecord item = store.Data.AddWord(activeSession, word, DateTime.Now);
                if (item != null) QueueTranslation(activeSession, item);
                Save(); RefreshSummary(); Paint();
            }
            catch (ExternalException) { clipboardRetries--; clipboardRetryAt = Native.Now + 150; }
        }
        private void QueueTranslation(SessionRecord session, WordRecord word)
        {
            string cached = store.Data.Cached(word.Word);
            word.Status = "pending"; word.Meaning = "翻译中…";
            if (cached != null) { ApplyTranslation(session, word, new TranslationResult { Success = true, Text = cached }); return; }
            translations.Add(delegate
            {
                TranslationResult result = translateWord(word.Word);
                if (!closing) window.Dispatcher.BeginInvoke(new Action(delegate { if (!closing) ApplyTranslation(session, word, result); }));
            });
        }
        private void ApplyTranslation(SessionRecord session, WordRecord word, TranslationResult result)
        {
            word.Status = result.Success ? "ok" : "failed";
            word.Meaning = result.Text;
            if (result.Success && activeSession == session) toastQueue.Enqueue(word);
            Save(); RefreshSummary();
            if (session != activeSession) Export(session);
            Paint();
        }
        private void RetryFailed()
        {
            if (displayedSession == null) return;
            foreach (WordRecord word in displayedSession.Words.Where(w => w.Status == "failed").ToArray()) QueueTranslation(displayedSession, word);
            Save(); RefreshSummary();
        }
        private void Protect(IntPtr handle)
        {
            if (clock.State != TimerState.Running || clock.Remaining <= 0 || handle == IntPtr.Zero || handle != Native.GetForegroundWindow() || Native.IsOwn(handle) || !Native.IsWindowVisible(handle) || Native.IsIconic(handle)) return;
            string process = Native.ProcessName(handle);
            if (!WindowPolicy.ShouldBlock(process) || WindowPolicy.IsSystemSurface(handle)) return;
            // QA only targets our disposable test window; never touches the user's apps.
            if (qa && process != "focusguardprobe") return;
            if (blockedHandle == handle && Native.Now - blockedAt < 700) return;
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
            { F<TextBlock>("CustomHint").Text = "请输入 1–180"; F<TextBlock>("CustomHint").Foreground = amber; return; }
            SetMinutes(minutes);
        }
        private void SetCompact(bool value) { compact = value; customOpen = false; Resize(); KeepVisible(true); }
        private void Resize()
        {
            double right = window.Left + window.Width;
            Rect area = WorkArea();
            bool atRight = !double.IsNaN(window.Left) && Math.Abs(area.Right - right) < 45;
            F<StackPanel>("FullPanel").Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            F<StackPanel>("MiniPanel").Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
            F<Grid>("CustomRow").Visibility = customOpen ? Visibility.Visible : Visibility.Collapsed;
            F<StackPanel>("SummaryPanel").Visibility = !compact && showSummary && displayedSession != null ? Visibility.Visible : Visibility.Collapsed;
            window.Width = compact ? 310 : 354;
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
            F<TextBlock>("NotesSaveState").Text = saved ? "已保存" : "保存失败";
            if (qa) return;
            prefs.Left = window.Left; prefs.Top = window.Top; prefs.Compact = false; prefs.Save();
        }
        private void Export(SessionRecord session)
        {
            try { exportFolder = WordExport.Export(session, exportRoot); }
            catch
            {
                try { exportFolder = WordExport.Export(session, System.IO.Path.Combine(Preferences.Folder, "词表")); }
                catch { exportFolder = null; F<TextBlock>("ExportStatus").Text = "词表导出失败，单词仍保存在阅读记录中，可重试。"; return; }
            }
            F<TextBlock>("ExportStatus").Text = "已保存 TXT / CSV / TSV · 点击导出可打开";
            F<TextBlock>("ExportStatus").ToolTip = exportFolder;
        }
        private void RefreshSummary()
        {
            if (displayedSession != null)
            {
                F<DataGrid>("WordsGrid").ItemsSource = null;
                F<DataGrid>("WordsGrid").ItemsSource = displayedSession.Words;
                F<TextBlock>("SummaryTitle").Text = displayedSession == activeSession ? "本轮单词" : "本轮单词 · " + displayedSession.StartedAt.ToString("MM-dd HH:mm");
                int ok = displayedSession.Words.Count(w => w.Status == "ok");
                int pending = displayedSession.Words.Count(w => w.Status == "pending");
                int failed = displayedSession.Words.Count(w => w.Status == "failed");
                F<TextBlock>("SummaryStatus").Text = displayedSession.Words.Count == 0 ? "本轮尚未收集单词。计时中复制英文单词即可收集。" : "共 " + displayedSession.Words.Count + " 词 · 已翻译 " + ok + " · 进行中 " + pending + " · 待重试 " + failed;
                Button retry = F<Button>("RetryButton");
                retry.IsEnabled = failed > 0;
                retry.Visibility = displayedSession.Words.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
                retry.Content = failed > 0 ? "重新翻译" : pending > 0 ? "翻译中…" : "翻译已完成";
                retry.ToolTip = failed > 0 ? "重新翻译尚未成功的单词，已成功的释义会保留" : pending > 0 ? "单词正在后台翻译，请稍候" : "所有单词都已翻译成功";
                F<Button>("CopyWords").IsEnabled = displayedSession.Words.Count > 0;
            }
            Resize();
        }
        private void Paint()
        {
            bool running = clock.State == TimerState.Running;
            bool paused = clock.State == TimerState.Paused;
            bool done = clock.State == TimerState.Complete;
            long remaining = clock.Remaining;
            string time = FocusClock.Display(remaining);
            F<TextBlock>("TimeLabel").Text = time; F<TextBlock>("MiniTime").Text = time;
            F<TextBlock>("TimeLabel").Foreground = done ? amber : forest;
            F<TextBlock>("StateLabel").Text = running ? "正在阅读" : paused ? "已暂停" : done ? "本轮完成" : showSummary && displayedSession != null ? "本轮结束" : "准备阅读";
            F<TextBlock>("DialCaption").Text = running ? "预计 " + DateTime.Now.AddMilliseconds(remaining).ToString("HH:mm") + " 结束" : paused ? "专注保护与收词已暂停" : done ? "起身活动，让眼睛休息" : showSummary && displayedSession != null ? "已保存词表与今日统计" : "留一段时间，读懂一篇";
            F<Button>("ToggleButton").Content = running ? "暂停一下" : paused ? "继续专注" : done ? "再读一轮" : "开始专注";
            F<Button>("MiniToggle").Content = running ? "Ⅱ" : "▶";
            F<Button>("StopButton").IsEnabled = activeSession != null && paused;
            F<Button>("CloseButton").IsEnabled = !running;
            F<Button>("WordsButton").IsEnabled = !running;
            F<Grid>("PresetRow").IsEnabled = activeSession == null;
            F<Border>("StatsPanel").Visibility = running ? Visibility.Collapsed : Visibility.Visible;
            F<Button>("SoundButton").Content = prefs.Sound ? "提示音 开" : "提示音 关";
            F<TextBlock>("FooterLabel").Text = store.Error ?? (running ? "保护中 · 暂停可解除" : "今日统计自动保存");
            F<TextBlock>("FooterLabel").ToolTip = store.Error;
            int wordCount = displayedSession == null ? 0 : displayedSession.Words.Count;
            F<Button>("WordsButton").Content = "词表 " + wordCount;
            F<TextBlock>("MiniState").Text = Native.Now < redUntil ? "回到文献，让注意力继续。" : running ? "专注保护中 · 已收集 " + wordCount + " 词" : paused ? "已暂停 · 专注保护已解除" : "准备阅读";
            F<TextBlock>("MiniState").ToolTip = "运行时，从 Zotero、浏览器或文件管理器复制单个英文词即可收集。英文 → 中文，在线翻译由 MyMemory 提供。";
            DateTime now = DateTime.Now;
            F<TextBlock>("MinutesStat").Text = (store.Data.TodayTime(now) / 60000.0).ToString("0.0");
            F<TextBlock>("IntroStat").Text = store.Data.TodayCount(0, now).ToString();
            F<TextBlock>("ModelStat").Text = store.Data.TodayCount(1, now).ToString();
            F<TextBlock>("FindingsStat").Text = store.Data.TodayCount(2, now).ToString();
            string paper = "第 " + store.Data.CurrentPaper.Number + " 篇";
            F<TextBlock>("PaperLabel").Text = paper; F<TextBlock>("MiniPaper").Text = paper;
            syncing = true;
            for (int i = 0; i < 3; i++) foreach (string prefix in new[] { "Section", "MiniSection" })
            {
                CheckBox box = F<CheckBox>(prefix + i);
                box.IsChecked = !string.IsNullOrEmpty(store.Data.CurrentPaper.MarkDates[i]);
                box.IsEnabled = activeSession != null;
            }
            syncing = false;
            int[] minutes = { 25, 45, 60 };
            for (int i = 0; i < 3; i++)
            {
                Button b = F<Button>("Preset" + minutes[i]); bool selected = prefs.Minutes == minutes[i];
                b.Background = B(selected ? "#DFEADC" : "#EEF2EC"); b.BorderBrush = B(selected ? "#BBCFB3" : "#EEF2EC");
            }
            F<Button>("CustomButton").Content = Array.IndexOf(minutes, prefs.Minutes) < 0 ? prefs.Minutes + " 分" : "自定义";
            double fraction = Math.Max(0, Math.Min(1, (double)remaining / clock.Duration));
            Path arc = F<Path>("ProgressArc");
            arc.Stroke = done ? amber : B("#7E9D7D");
            if (fraction <= 0) arc.Data = Geometry.Empty;
            else if (fraction >= 0.9999) arc.Data = new EllipseGeometry(new Point(77, 77), 75, 75);
            else
            {
                double angle = fraction * 2 * Math.PI - Math.PI / 2;
                PathFigure figure = new PathFigure { StartPoint = new Point(77, 2) };
                figure.Segments.Add(new ArcSegment(new Point(77 + 75 * Math.Cos(angle), 77 + 75 * Math.Sin(angle)), new Size(75, 75), 0, fraction > 0.5, SweepDirection.Clockwise, true));
                PathGeometry geometry = new PathGeometry(); geometry.Figures.Add(figure); arc.Data = geometry;
            }
            F<Border>("Card").BorderBrush = Native.Now < redUntil && (Native.Now / 180) % 2 == 0 ? B("#D7524E") : done ? B("#CFB48F") : B("#DDE6D9");
        }

        [STAThread]
        public static int Main(string[] args)
        {
#if TESTING
            if (args.Length > 0 && args[0] == "--self-test") return Checks.Run(args.Length > 1 ? args[1] : AppDomain.CurrentDomain.BaseDirectory);
            if (args.Length > 1 && args[0] == "--flow-test") return FlowTest(args[1]);
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
