using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace StudyDesk
{
    public class TaskEntry
    {
        public string Id { get; set; }
        public string Date { get; set; }
        public string Text { get; set; }
        public bool Done { get; set; }
    }

    public class AppData
    {
        public List<TaskEntry> Tasks { get; set; }
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double Opacity { get; set; }
        public bool Topmost { get; set; }
        public bool AutoStart { get; set; }
        public bool Initialized { get; set; }

        public AppData()
        {
            Tasks = new List<TaskEntry>();
            Width = 355;
            Height = 510;
            Opacity = 0.96;
            AutoStart = true;
        }
    }

    public class Widget : Window
    {
        const string AppName = "StudyDesk Daily Widget";
        readonly string dataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StudyDesk", "tasks.json");
        readonly JavaScriptSerializer serializer = new JavaScriptSerializer();
        readonly SolidColorBrush ink = Brush("#20263B");
        readonly SolidColorBrush muted = Brush("#788196");
        readonly SolidColorBrush accent = Brush("#6459E8");
        readonly SolidColorBrush pale = Brush("#F3F2FF");
        AppData data;
        DateTime selectedDate = DateTime.Today;
        DateTime lastToday = DateTime.Today;
        TextBlock dateLabel, summaryLabel, headingLabel;
        StackPanel taskList;
        TextBox taskInput;
        Slider opacitySlider;
        CheckBox autoStartCheck, topmostCheck;
        Border settingsPanel;
        Forms.NotifyIcon tray;
        bool exiting;
        bool buildingSettings;
        DispatcherTimer geometryTimer;

        public Widget()
        {
            data = Load();
            Title = "今日学习与待办";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            MinWidth = 300;
            MinHeight = 380;
            Width = Math.Max(MinWidth, data.Width);
            Height = Math.Max(MinHeight, data.Height);
            Opacity = Clamp(data.Opacity, 0.45, 1);
            Topmost = data.Topmost;
            WindowStartupLocation = WindowStartupLocation.Manual;
            double desiredLeft = data.Initialized ? data.Left : SystemParameters.WorkArea.Right - Width - 32;
            double desiredTop = data.Initialized ? data.Top : SystemParameters.WorkArea.Top + 32;
            Left = Clamp(desiredLeft, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100);
            Top = Clamp(desiredTop, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 100);
            BuildUi();
            BuildTray();
            RenderTasks();
            SetAutoStart(data.AutoStart, false);
            geometryTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            geometryTimer.Tick += (s, e) => { geometryTimer.Stop(); SaveGeometry(); };
            Closed += (s, e) => { if (tray != null) tray.Dispose(); };
            Closing += (s, e) => { if (!exiting) { e.Cancel = true; Hide(); tray.ShowBalloonTip(1500, "今日任务", "小组件仍在托盘中运行。", Forms.ToolTipIcon.Info); } };
            LocationChanged += (s, e) => QueueGeometrySave();
            SizeChanged += (s, e) => QueueGeometrySave();
            DispatcherTimer timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromMinutes(1);
            timer.Tick += (s, e) => {
                if (DateTime.Today != lastToday)
                {
                    bool followingToday = selectedDate.Date == lastToday.Date;
                    lastToday = DateTime.Today;
                    if (followingToday) selectedDate = lastToday;
                    RenderTasks();
                }
            };
            timer.Start();
        }

        static SolidColorBrush Brush(string hex) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        static double Clamp(double value, double min, double max) { return Math.Max(min, Math.Min(max, value)); }

        AppData Load()
        {
            try
            {
                if (File.Exists(dataPath))
                {
                    AppData loaded = serializer.Deserialize<AppData>(File.ReadAllText(dataPath));
                    if (loaded != null) { if (loaded.Tasks == null) loaded.Tasks = new List<TaskEntry>(); return loaded; }
                }
            }
            catch (Exception ex) { MessageBox.Show("任务数据暂时无法读取：\n" + ex.Message, "今日任务"); }
            return new AppData();
        }

        void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dataPath));
                string temp = dataPath + ".tmp";
                File.WriteAllText(temp, serializer.Serialize(data));
                if (File.Exists(dataPath)) File.Replace(temp, dataPath, null);
                else File.Move(temp, dataPath);
            }
            catch (Exception ex) { MessageBox.Show("保存任务失败：\n" + ex.Message, "今日任务"); }
        }

        void SaveGeometry()
        {
            if (!IsLoaded || WindowState != WindowState.Normal) return;
            data.Left = Left; data.Top = Top; data.Width = Width; data.Height = Height;
            data.Initialized = true;
            Save();
        }

        void QueueGeometrySave()
        {
            if (geometryTimer == null) return;
            geometryTimer.Stop(); geometryTimer.Start();
        }

        public void Reveal()
        {
            Show(); WindowState = WindowState.Normal; Activate();
        }

        TextBlock Text(string content, int size, Brush color, FontWeight weight)
        {
            return new TextBlock { Text = content, FontSize = size, Foreground = color, FontWeight = weight,
                FontFamily = new FontFamily("Microsoft YaHei UI"), VerticalAlignment = VerticalAlignment.Center };
        }

        Button Button(string content, int size, Brush foreground, Brush background)
        {
            Button b = new Button { Content = content, FontFamily = new FontFamily("Microsoft YaHei UI"), FontSize = size,
                Foreground = foreground, Background = background, BorderThickness = new Thickness(0), Cursor = Cursors.Hand,
                Padding = new Thickness(8, 4, 8, 4), MinWidth = 28, MinHeight = 27 };
            b.Template = RoundButtonTemplate();
            return b;
        }

        ControlTemplate RoundButtonTemplate()
        {
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            presenter.SetBinding(ContentPresenter.MarginProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            border.AppendChild(presenter);
            template.VisualTree = border;
            return template;
        }

        void BuildUi()
        {
            Border card = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(18), Margin = new Thickness(10),
                BorderBrush = Brush("#E7E8F0"), BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 20, ShadowDepth = 4, Opacity = 0.16 } };
            Content = card;
            Grid layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(82) });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(61) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(60) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            card.Child = layout;

            Border header = new Border { Background = Brush("#252844"), CornerRadius = new CornerRadius(17, 17, 0, 0) };
            Grid.SetRow(header, 0); layout.Children.Add(header);
            Grid headerGrid = new Grid { Margin = new Thickness(20, 13, 14, 12) };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Child = headerGrid;
            StackPanel titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            titleStack.Children.Add(Text("STUDY DESK", 10, Brush("#AAA5FF"), FontWeights.Bold));
            titleStack.Children.Add(Text("今日学习与待办", 19, Brushes.White, FontWeights.SemiBold));
            headerGrid.Children.Add(titleStack);
            StackPanel controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
            Grid.SetColumn(controls, 1); headerGrid.Children.Add(controls);
            Button settings = Button("⚙", 17, Brushes.White, Brush("#3C4064"));
            settings.ToolTip = "设置"; settings.Click += (s, e) => { settingsPanel.Visibility = settingsPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; };
            controls.Children.Add(settings);
            Button hide = Button("×", 20, Brushes.White, Brush("#3C4064"));
            hide.Margin = new Thickness(6, 0, 0, 0); hide.ToolTip = "隐藏到托盘"; hide.Click += (s, e) => Hide();
            controls.Children.Add(hide);
            header.MouseLeftButtonDown += (s, e) => { if (e.OriginalSource is TextBlock || e.OriginalSource == header || e.OriginalSource == headerGrid) DragMove(); };

            Grid dateRow = new Grid { Margin = new Thickness(20, 12, 20, 7) };
            dateRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            dateRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            dateRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetRow(dateRow, 1); layout.Children.Add(dateRow);
            Button prev = Button("‹", 24, accent, pale); prev.Click += (s, e) => { selectedDate = selectedDate.AddDays(-1); RenderTasks(); };
            dateRow.Children.Add(prev);
            dateLabel = Text("", 15, ink, FontWeights.SemiBold); dateLabel.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(dateLabel, 1); dateRow.Children.Add(dateLabel);
            Button next = Button("›", 24, accent, pale); next.Click += (s, e) => { selectedDate = selectedDate.AddDays(1); RenderTasks(); };
            Grid.SetColumn(next, 2); dateRow.Children.Add(next);

            StackPanel stats = new StackPanel { Margin = new Thickness(20, 3, 20, 8) };
            Grid.SetRow(stats, 2); layout.Children.Add(stats);
            Grid statLine = new Grid(); statLine.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            statLine.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headingLabel = Text("今天的计划", 13, ink, FontWeights.SemiBold); statLine.Children.Add(headingLabel);
            summaryLabel = Text("", 12, muted, FontWeights.Normal); Grid.SetColumn(summaryLabel, 1); statLine.Children.Add(summaryLabel);
            stats.Children.Add(statLine);
            Border progressBase = new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = Brush("#ECECF4"), Margin = new Thickness(0, 10, 0, 0) };
            Grid progressGrid = new Grid(); progressBase.Child = progressGrid;
            Border progressFill = new Border { Background = accent, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left };
            progressGrid.Children.Add(progressFill);
            progressBase.SizeChanged += (s, e) => UpdateProgress(progressFill, progressBase.ActualWidth);
            stats.Children.Add(progressBase);
            progressBase.Tag = progressFill;
            stats.Tag = progressBase;

            ScrollViewer scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(14, 0, 12, 0) };
            taskList = new StackPanel { Margin = new Thickness(6, 0, 6, 6) };
            scroll.Content = taskList; Grid.SetRow(scroll, 3); layout.Children.Add(scroll);

            Grid addRow = new Grid { Margin = new Thickness(20, 8, 20, 12) };
            addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetRow(addRow, 4); layout.Children.Add(addRow);
            taskInput = new TextBox { FontFamily = new FontFamily("Microsoft YaHei UI"), FontSize = 13, Foreground = ink,
                BorderBrush = Brush("#DFE1EB"), BorderThickness = new Thickness(1), Background = Brush("#FAFAFD"),
                Padding = new Thickness(11, 8, 9, 8), VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "输入任务后按回车" };
            taskInput.KeyDown += (s, e) => { if (e.Key == Key.Enter) AddTask(); };
            addRow.Children.Add(taskInput);
            Button add = Button("添加", 13, Brushes.White, accent); add.Padding = new Thickness(14, 6, 14, 6);
            add.MinWidth = 58;
            add.Margin = new Thickness(8, 0, 0, 0); add.Click += (s, e) => AddTask();
            Grid.SetColumn(add, 1); addRow.Children.Add(add);

            Grid bottom = new Grid { Margin = new Thickness(20, 0, 12, 10) };
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetRow(bottom, 5); layout.Children.Add(bottom);
            Button today = Button("回到今天", 11, accent, pale); today.Click += (s, e) => { selectedDate = DateTime.Today; RenderTasks(); };
            today.MinWidth = 76;
            today.HorizontalAlignment = HorizontalAlignment.Left; bottom.Children.Add(today);
            Thumb grip = new Thumb { Width = 19, Height = 19, Cursor = Cursors.SizeNWSE, Background = Brushes.Transparent, ToolTip = "拖动调整大小" };
            grip.Template = GripTemplate();
            grip.DragDelta += (s, e) => { Width = Math.Max(MinWidth, Width + e.HorizontalChange); Height = Math.Max(MinHeight, Height + e.VerticalChange); };
            Grid.SetColumn(grip, 1); bottom.Children.Add(grip);

            settingsPanel = BuildSettings();
            settingsPanel.Visibility = Visibility.Collapsed;
            Grid.SetRow(settingsPanel, 3); Grid.SetRowSpan(settingsPanel, 2);
            Panel.SetZIndex(settingsPanel, 5); layout.Children.Add(settingsPanel);
        }

        ControlTemplate GripTemplate()
        {
            ControlTemplate template = new ControlTemplate(typeof(Thumb));
            FrameworkElementFactory text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetValue(TextBlock.TextProperty, "◢");
            text.SetValue(TextBlock.FontSizeProperty, 17.0);
            text.SetValue(TextBlock.ForegroundProperty, Brush("#A4A8B8"));
            template.VisualTree = text; return template;
        }

        Border BuildSettings()
        {
            Border panel = new Border { Background = Brushes.White, BorderBrush = Brush("#E8E7F1"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12), Margin = new Thickness(18, 2, 18, 8), Padding = new Thickness(17),
                Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Opacity = 0.12 } };
            StackPanel stack = new StackPanel(); panel.Child = stack;
            stack.Children.Add(Text("小组件设置", 15, ink, FontWeights.SemiBold));
            TextBlock transparency = Text("透明度", 12, muted, FontWeights.Normal); transparency.Margin = new Thickness(0, 16, 0, 0);
            stack.Children.Add(transparency);
            opacitySlider = new Slider { Minimum = 45, Maximum = 100, Value = Opacity * 100, Margin = new Thickness(0, 5, 0, 8),
                Foreground = accent, ToolTip = "调整窗口透明度" };
            opacitySlider.ValueChanged += (s, e) => { if (buildingSettings) return; Opacity = opacitySlider.Value / 100; data.Opacity = Opacity; Save(); };
            stack.Children.Add(opacitySlider);
            topmostCheck = new CheckBox { Content = "置于其他窗口前方", IsChecked = data.Topmost, FontSize = 12, Foreground = ink,
                FontFamily = new FontFamily("Microsoft YaHei UI"), Margin = new Thickness(0, 9, 0, 9) };
            topmostCheck.Checked += (s, e) => { Topmost = true; data.Topmost = true; Save(); };
            topmostCheck.Unchecked += (s, e) => { Topmost = false; data.Topmost = false; Save(); };
            stack.Children.Add(topmostCheck);
            autoStartCheck = new CheckBox { Content = "登录 Windows 后自动启动", IsChecked = data.AutoStart, FontSize = 12, Foreground = ink,
                FontFamily = new FontFamily("Microsoft YaHei UI"), Margin = new Thickness(0, 2, 0, 9) };
            autoStartCheck.Checked += (s, e) => ChangeAutoStart(true);
            autoStartCheck.Unchecked += (s, e) => ChangeAutoStart(false);
            stack.Children.Add(autoStartCheck);
            TextBlock hint = Text("拖动顶部移动 · 右下角调整大小", 11, muted, FontWeights.Normal);
            hint.Margin = new Thickness(0, 8, 0, 0); stack.Children.Add(hint);
            return panel;
        }

        void UpdateProgress(Border fill, double width)
        {
            int total = data.Tasks.Count(t => t.Date == selectedDate.ToString("yyyy-MM-dd"));
            int done = data.Tasks.Count(t => t.Date == selectedDate.ToString("yyyy-MM-dd") && t.Done);
            fill.Width = total == 0 ? 0 : width * done / total;
        }

        void RenderTasks()
        {
            if (taskList == null) return;
            string day = selectedDate.ToString("yyyy-MM-dd");
            string[] weekdays = { "周日", "周一", "周二", "周三", "周四", "周五", "周六" };
            dateLabel.Text = selectedDate.ToString("yyyy 年 M 月 d 日") + "  " + weekdays[(int)selectedDate.DayOfWeek];
            headingLabel.Text = selectedDate.Date == DateTime.Today ? "今天的计划" : "当天的计划";
            List<TaskEntry> entries = data.Tasks.Where(t => t.Date == day).ToList();
            int done = entries.Count(t => t.Done);
            summaryLabel.Text = done + " / " + entries.Count + " 已完成";
            taskList.Children.Clear();
            if (entries.Count == 0)
            {
                StackPanel empty = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 55, 0, 0) };
                TextBlock icon = Text("✦", 36, Brush("#C9C4FC"), FontWeights.Normal); icon.HorizontalAlignment = HorizontalAlignment.Center;
                TextBlock message = Text("还没有安排任务", 13, muted, FontWeights.Normal); message.Margin = new Thickness(0, 10, 0, 0);
                empty.Children.Add(icon); empty.Children.Add(message); taskList.Children.Add(empty);
            }
            foreach (TaskEntry entry in entries)
            {
                Border row = new Border { Background = entry.Done ? Brush("#F9F9FC") : Brush("#F7F6FF"),
                    CornerRadius = new CornerRadius(9), Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(9, 9, 8, 9) };
                Grid line = new Grid();
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                CheckBox check = new CheckBox { IsChecked = entry.Done, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 11, 0),
                    ToolTip = "标记完成" };
                check.Checked += (s, e) => { entry.Done = true; Save(); RenderTasks(); };
                check.Unchecked += (s, e) => { entry.Done = false; Save(); RenderTasks(); };
                line.Children.Add(check);
                TextBlock name = Text(entry.Text, 13, entry.Done ? muted : ink, FontWeights.Normal);
                name.TextWrapping = TextWrapping.Wrap;
                if (entry.Done) name.TextDecorations = TextDecorations.Strikethrough;
                Grid.SetColumn(name, 1); line.Children.Add(name);
                Button remove = Button("×", 16, muted, Brushes.Transparent); remove.ToolTip = "删除任务";
                remove.Click += (s, e) => { data.Tasks.Remove(entry); Save(); RenderTasks(); };
                Grid.SetColumn(remove, 2); line.Children.Add(remove);
                row.Child = line; taskList.Children.Add(row);
            }
            StackPanel statStack = (StackPanel)((Grid)((Border)Content).Child).Children[2];
            Border bar = (Border)statStack.Tag;
            UpdateProgress((Border)bar.Tag, bar.ActualWidth);
        }

        void AddTask()
        {
            string value = taskInput.Text.Trim();
            if (value.Length == 0) return;
            data.Tasks.Add(new TaskEntry { Id = Guid.NewGuid().ToString("N"), Date = selectedDate.ToString("yyyy-MM-dd"), Text = value, Done = false });
            taskInput.Clear(); Save(); RenderTasks(); taskInput.Focus();
        }

        void BuildTray()
        {
            tray = new Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Text = "今日学习与待办", Visible = true };
            Forms.ContextMenuStrip menu = new Forms.ContextMenuStrip();
            menu.Items.Add("显示小组件", null, (s, e) => Reveal());
            menu.Items.Add("回到今天", null, (s, e) => { selectedDate = DateTime.Today; RenderTasks(); Reveal(); });
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("退出", null, (s, e) => { exiting = true; SaveGeometry(); Close(); Forms.Application.Exit(); });
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += (s, e) => Reveal();
        }

        void ChangeAutoStart(bool enabled)
        {
            if (buildingSettings) return;
            if (SetAutoStart(enabled, true)) { data.AutoStart = enabled; Save(); }
            else { buildingSettings = true; autoStartCheck.IsChecked = data.AutoStart; buildingSettings = false; }
        }

        bool SetAutoStart(bool enabled, bool showError)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    if (key == null) throw new InvalidOperationException("无法打开开机启动设置。");
                    if (enabled) key.SetValue(AppName, "\"" + System.Reflection.Assembly.GetExecutingAssembly().Location + "\"");
                    else key.DeleteValue(AppName, false);
                }
                return true;
            }
            catch (Exception ex) { if (showError) MessageBox.Show("无法设置开机自启：\n" + ex.Message, "今日任务"); return false; }
        }
    }

    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            bool first;
            using (Mutex mutex = new Mutex(true, @"Local\StudyDeskDailyWidget", out first))
            {
                using (EventWaitHandle revealEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\StudyDeskReveal"))
                {
                    if (!first) { revealEvent.Set(); return; }
                    System.Windows.Application app = new System.Windows.Application();
                    app.ShutdownMode = ShutdownMode.OnMainWindowClose;
                    Widget widget = new Widget();
                    Thread listener = new Thread(() => {
                        while (!widget.Dispatcher.HasShutdownStarted)
                        {
                            if (revealEvent.WaitOne(500)) widget.Dispatcher.BeginInvoke(new Action(widget.Reveal));
                        }
                    });
                    listener.IsBackground = true;
                    listener.Start();
                    app.Run(widget);
                }
            }
        }
    }
}
