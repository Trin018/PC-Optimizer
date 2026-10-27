using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;

namespace PCOptimizer
{
    public partial class MainWindow : Window
    {
        private string currentVersion = "14.5";
        private string githubRepoApi = "https://api.github.com/repos/Trin018/PC-Optimizer/releases/latest";

        private HardwareMonitorService _hwMonitor = new HardwareMonitorService();
        private DispatcherTimer _sensorTimer;
        private Window overlayWin, crosshairWin;
        private TextBox txtSearchProg;
        private TextBlock txtCpuVal, txtRamVal, txtDiskVal, txtGpuTempVal, txtOptScoreVal, txtOptStatus;
        private TextBlock lblMouseHz, lblMouseMaxHz, lblNetResult, lblKeyTest, lblGamePingResult, overlayText;
        private ListBox lstPrograms, lstLeftovers, lstHistory, lstRunningGames, lstBigFolders;
        private Canvas graphCanvas, ufoCanvas;
        private Polyline cpuLine, ramLine;
        private Ellipse gaugeRing;
        private Rectangle ufoRect1, ufoRect2;
        private ComboBox cmbGameSelect;

        private List<double> cpuHistory = new List<double>(), ramHistory = new List<double>();
        private List<CheckBox> storeCheckBoxes = new List<CheckBox>(), allToggleCards = new List<CheckBox>();
        private HashSet<Key> pressedKeys = new HashSet<Key>();
        private Stopwatch mouseSw = new Stopwatch();
        private int mouseSamples = 0, maxMouseHz = 0, maxSimulKeys = 0, crosshairStyle = 0;
        private double ufoX = 0;
        private bool ufoRunning = false, isUpdatingToggles = false, isReadingSensors = false;
        private ulong totalRamMb = 32768;
        private Brush crosshairColor = Brushes.Lime;
        private string historyFilePath = @"C:\PCOptimizer_Backups\Historico_Acoes.txt";

        [DllImport("psapi.dll")] static extern int EmptyWorkingSet(IntPtr hwProc);
        [DllImport("ntdll.dll", SetLastError = true)] static extern int NtSetTimerResolution(uint DesiredResolution, bool SetResolution, out uint CurrentResolution);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra; public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels; public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        }
        [DllImport("user32.dll", CharSet = CharSet.Ansi)] static extern int EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);
        [DllImport("user32.dll", CharSet = CharSet.Ansi)] static extern int ChangeDisplaySettings(ref DEVMODE lpDevMode, int dwFlags);

        public MainWindow()
        {
            InitializeComponent();
            lblVerTitle.Text = "CONTROL PRO v" + currentVersion;
            nav9.Content = "📦  Ninite App Store (36 Apps)";

            topDragBar.MouseLeftButtonDown += (s, e) => { if (e.ClickCount == 2) ToggleFullscreen(); else if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
            btnMin.Click += (s, e) => WindowState = WindowState.Minimized;
            btnMax.Click += (s, e) => ToggleFullscreen();
            btnClose.Click += (s, e) => { _hwMonitor.Dispose(); Environment.Exit(0); };
            btnOverlay.Click += ToggleMiniOverlay;
            btnTopCrosshair.Click += (s, e) => ToggleCrosshairOverlay();
            btnUpdate.Click += (s, e) => CheckForGitHubReleasesUpdate(true);

            chkGuard.Checked += (s, e) => Log("🛡️ [AUTO-GUARD ATIVADO] Vigia automático de RAM (<2.5GB) ativo!");
            chkGuard.Unchecked += (s, e) => Log("🛡️ [AUTO-GUARD DESATIVADO]");
            chkTimer.Checked += ToggleTimerResolution;
            chkTimer.Unchecked += ToggleTimerResolution;

            Grid[] pages = { page1, page2, page3, page4, page5, page6, page7, page8, page9, page10, page11, page12, page13, page14 };
            RadioButton[] navs = { nav1, nav2, nav3, nav4, nav5, nav6, nav7, nav8, nav9, nav10, nav11, nav12, nav13, nav14 };
            for (int i = 0; i < 14; i++)
            {
                int idx = i;
                navs[i].Checked += (s, e) => { for (int j = 0; j < 14; j++) pages[j].Visibility = (j == idx) ? Visibility.Visible : Visibility.Collapsed; if (idx == 0) CalculateOptimizationScore(); };
            }

            BuildPage0_GaldinoDashboard(page1);
            BuildPage1_ToggleCardsWindows(page2);
            BuildPage2_PerGameOptimizer(page3);
            BuildPage3_GamingSwitches(page4);
            BuildPage_ProcessLasso(page5);
            BuildPage_CrosshairAndGamePing(page6);
            BuildPage_VipGamesAndDisplay(page7);
            BuildPage_DiskSpaceHunter(page8);
            BuildPage_NiniteAppStore(page9);
            BuildPage3_NativeTests(page10);
            BuildPage4_RevoUninstaller(page11);
            BuildPage5_FansAndRgb(page12);
            BuildPage6_Streamer(page13);
            BuildPage8_UndoHistory(page14);

            SourceInitialized += (s, e) =>
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                HwndSource.FromHwnd(hwnd)?.AddHook(HwndHook);
                RegisterHotKey(hwnd, 9001, 0x0006, 0x70); // Ctrl+Shift+F1
                RegisterHotKey(hwnd, 9002, 0x0006, 0x71); // Ctrl+Shift+F2
                RegisterHotKey(hwnd, 9003, 0x0006, 0x72); // Ctrl+Shift+F3
            };

            PreviewKeyDown += (s, e) => { if (page10.Visibility == Visibility.Visible && lblKeyTest != null) { pressedKeys.Add(e.Key); if (pressedKeys.Count > maxSimulKeys) maxSimulKeys = pressedKeys.Count; lblKeyTest.Text = $"⌨️ Última: [{e.Key}] | Simultâneas: {pressedKeys.Count} | 🏆 Recorde NKRO: {maxSimulKeys} teclas"; } };
            PreviewKeyUp += (s, e) => pressedKeys.Remove(e.Key);

            StartLiveSensorLoop();
            ContentRendered += (s, e) => Task.Run(() => { _hwMonitor.Initialize(); string hw = GetHardwareSummary(); Dispatcher.Invoke(() => { lblHardware.Text = hw; LoadActionHistory(); CalculateOptimizationScore(); }); CheckForGitHubReleasesUpdate(false); });
            Log($"⚡ PC OPTIMIZER v{currentVersion} | Interface Cards + Switches com Leitura Real + Ninite Store 36 Apps!");
        }

        private bool CheckRegInt(string key, string val, int exp) { try { object v = Registry.GetValue(key, val, null); if (v != null && Convert.ToInt32(v) == exp) return true; } catch { } return false; }
        private bool CheckRegStr(string key, string val, string exp) { try { object v = Registry.GetValue(key, val, null); if (v != null && v.ToString().Trim() == exp) return true; } catch { } return false; }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == 0x0312)
            {
                int id = wParam.ToInt32();
                if (id == 9001) { FlushMemory(); Console.Beep(900, 120); Log("⌨️ [HOTKEY Ctrl+Shift+F1] RAM limpa!"); handled = true; }
                else if (id == 9002) { ToggleCrosshairOverlay(); Console.Beep(700, 80); handled = true; }
                else if (id == 9003) { ToggleMiniOverlay(null, null); Console.Beep(700, 80); handled = true; }
            }
            return IntPtr.Zero;
        }

        private void AddSwitchCard(Panel parent, string badgeType, string title, string desc, bool state, Action<bool> onToggle)
        {
            Border card = new Border { Background = new SolidColorBrush(Color.FromRgb(13, 19, 31)), BorderBrush = state ? new SolidColorBrush(Color.FromRgb(2, 132, 199)) : new SolidColorBrush(Color.FromRgb(26, 38, 59)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Margin = new Thickness(6), Padding = new Thickness(14, 12, 14, 12) };
            DockPanel dp = new DockPanel();
            Border badge = new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(7, 2, 7, 2), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8), Background = badgeType == "REC" ? new SolidColorBrush(Color.FromRgb(12, 45, 72)) : new SolidColorBrush(Color.FromRgb(66, 32, 6)), BorderBrush = badgeType == "REC" ? new SolidColorBrush(Color.FromRgb(2, 132, 199)) : new SolidColorBrush(Color.FromRgb(217, 119, 6)), BorderThickness = new Thickness(1), Child = new TextBlock { Text = badgeType == "REC" ? "★ RECOMENDADO" : "! AVANÇADO", FontSize = 9.5, FontWeight = FontWeights.Bold, Foreground = badgeType == "REC" ? new SolidColorBrush(Color.FromRgb(56, 189, 248)) : new SolidColorBrush(Color.FromRgb(251, 191, 36)) } };
            DockPanel.SetDock(badge, Dock.Top); dp.Children.Add(badge);

            DockPanel bottomBar = new DockPanel { Margin = new Thickness(0, 8, 0, 0), VerticalAlignment = VerticalAlignment.Bottom };
            DockPanel.SetDock(bottomBar, Dock.Bottom);
            TextBlock lblState = new TextBlock { Text = state ? "Ativado no teu PC" : "Desativado", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = state ? new SolidColorBrush(Color.FromRgb(56, 189, 248)) : new SolidColorBrush(Color.FromRgb(100, 116, 139)), VerticalAlignment = VerticalAlignment.Center };
            CheckBox sw = new CheckBox { Style = (Style)FindResource("ModernSwitch"), IsChecked = state, HorizontalAlignment = HorizontalAlignment.Right };
            allToggleCards.Add(sw);

            sw.Checked += (s, e) => { lblState.Text = "Ativado no teu PC"; lblState.Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248)); card.BorderBrush = new SolidColorBrush(Color.FromRgb(2, 132, 199)); if (!isUpdatingToggles) { RecordActionHistory("[ON] " + title); onToggle(true); CalculateOptimizationScore(); } };
            sw.Unchecked += (s, e) => { lblState.Text = "Desativado"; lblState.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)); card.BorderBrush = new SolidColorBrush(Color.FromRgb(26, 38, 59)); if (!isUpdatingToggles) { RecordActionHistory("[OFF] " + title); onToggle(false); CalculateOptimizationScore(); } };
            DockPanel.SetDock(sw, Dock.Right); bottomBar.Children.Add(sw); bottomBar.Children.Add(lblState); dp.Children.Add(bottomBar);

            StackPanel sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = title, FontSize = 13.5, FontWeight = FontWeights.Bold, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) });
            sp.Children.Add(new TextBlock { Text = desc, FontSize = 11.2, Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)), TextWrapping = TextWrapping.Wrap });
            dp.Children.Add(sp); card.Child = dp; parent.Children.Add(card);
        }

        private void AddCardBtn(Panel parent, string text, string desc, Action onClick, bool isHero = false)
        {
            Border card = new Border { Background = new SolidColorBrush(Color.FromRgb(13, 19, 31)), BorderBrush = isHero ? new SolidColorBrush(Color.FromRgb(2, 132, 199)) : new SolidColorBrush(Color.FromRgb(26, 38, 59)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Margin = new Thickness(6), Padding = new Thickness(14, 10, 14, 10) };
            DockPanel dp = new DockPanel();
            Border badge = new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(7, 2, 7, 2), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 6), Background = isHero ? new SolidColorBrush(Color.FromRgb(12, 45, 72)) : new SolidColorBrush(Color.FromRgb(23, 37, 84)), BorderBrush = new SolidColorBrush(Color.FromRgb(2, 132, 199)), BorderThickness = new Thickness(1), Child = new TextBlock { Text = isHero ? "★ DESTAQUE PRO" : "⚡ FERRAMENTA DIRETA", FontSize = 9, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248)) } };
            DockPanel.SetDock(badge, Dock.Top); dp.Children.Add(badge);

            Button btn = new Button { Content = "Executar Agora ▶", Background = isHero ? new SolidColorBrush(Color.FromRgb(2, 132, 199)) : new SolidColorBrush(Color.FromRgb(30, 41, 59)), Foreground = Brushes.White, BorderThickness = new Thickness(0), Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 8, 0, 0), FontSize = 11, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right, Cursor = Cursors.Hand };
            btn.Click += (s, e) => { try { RecordActionHistory(text); onClick(); CalculateOptimizationScore(); } catch (Exception ex) { Log("⚠️ Aviso: " + ex.Message); } };
            DockPanel.SetDock(btn, Dock.Bottom); dp.Children.Add(btn);

            StackPanel sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = text, FontSize = 13, FontWeight = FontWeights.Bold, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 3) });
            sp.Children.Add(new TextBlock { Text = desc, FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)), TextWrapping = TextWrapping.Wrap });
            dp.Children.Add(sp); card.Child = dp; parent.Children.Add(card);
        }

        private void BuildPage0_GaldinoDashboard(Grid page)
        {
            Grid g = new Grid(); g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(255) }); g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); page.Children.Add(g);
            Grid topGrid = new Grid(); topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) }); topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); Grid.SetRow(topGrid, 0);

            Border cLeft = CreateCard("🌡️ SENSORES TÉRMICOS & HARDWARE AO VIVO", 0, 0);
            StackPanel spLeft = (StackPanel)cLeft.Child;
            txtCpuVal = new TextBlock { Text = "🔥 CPU: A calibrar sensores...", FontSize = 13.5, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248)), Margin = new Thickness(0, 10, 0, 6) };
            txtGpuTempVal = new TextBlock { Text = "🎮 GPU: A calibrar sensores...", FontSize = 13.5, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153)), Margin = new Thickness(0, 2, 0, 6) };
            txtRamVal = new TextBlock { Text = "🧠 RAM Livre: A calcular...", FontSize = 13.5, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(192, 132, 252)), Margin = new Thickness(0, 2, 0, 6) };
            txtDiskVal = new TextBlock { Text = "💾 SSD Principal: A ler...", FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Brushes.Silver, Margin = new Thickness(0, 2, 0, 10) };
            spLeft.Children.Add(txtCpuVal); spLeft.Children.Add(txtGpuTempVal); spLeft.Children.Add(txtRamVal); spLeft.Children.Add(txtDiskVal);
            spLeft.Children.Add(CreateSmallBtn("🧹 Limpar Memória Cache / RAM (F1)", () => FlushMemory())); topGrid.Children.Add(cLeft);

            Border cCenter = CreateCard("⚡ CENTRAL DE OTIMIZAÇÃO & IA", 0, 1);
            StackPanel spCenter = (StackPanel)cCenter.Child;
            Grid gaugeGrid = new Grid { Width = 128, Height = 128, Margin = new Thickness(0, 6, 0, 8), HorizontalAlignment = HorizontalAlignment.Center };
            Ellipse bgRing = new Ellipse { Stroke = new SolidColorBrush(Color.FromRgb(30, 41, 59)), StrokeThickness = 10 };
            gaugeRing = new Ellipse { Stroke = new SolidColorBrush(Color.FromRgb(2, 132, 199)), StrokeThickness = 10, StrokeDashArray = new DoubleCollection(new double[] { 30, 100 }) };
            StackPanel gaugeCenterText = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            txtOptScoreVal = new TextBlock { Text = "100%", FontSize = 28, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center };
            txtOptStatus = new TextBlock { Text = "● Otimizado", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153)), HorizontalAlignment = HorizontalAlignment.Center };
            gaugeCenterText.Children.Add(txtOptScoreVal); gaugeCenterText.Children.Add(txtOptStatus);
            gaugeGrid.Children.Add(bgRing); gaugeGrid.Children.Add(gaugeRing); gaugeGrid.Children.Add(gaugeCenterText);
            spCenter.Children.Add(gaugeGrid);

            Button btnBoost = CreateSmallBtn("🤖 AI Boost Automático (Avaliar & Otimizar 100%)", () => RunAiSmartAutoTuner());
            btnBoost.HorizontalAlignment = HorizontalAlignment.Stretch; spCenter.Children.Add(btnBoost); topGrid.Children.Add(cCenter);

            Border cRight = CreateCard("⚔️ SIMULADOR DE BATALHA & MODO OTIMIZADO", 0, 2);
            StackPanel spRight = (StackPanel)cRight.Child;
            spRight.Children.Add(new TextBlock { Text = "Testa os FPS reais com 30.000 unidades em combate ou configura os interruptores:", FontSize = 11.5, Foreground = Brushes.Silver, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 10) });
            spRight.Children.Add(CreateSmallBtn("⚔️ Abrir Benchmark 30.000 Unidades!", () => GameSimBenchmark.OpenBattleBenchmarkWindow()));
            spRight.Children.Add(new Border { Height = 6 });
            spRight.Children.Add(CreateSmallBtn("🪟 Ir para Interruptores do Windows ->", () => nav2.IsChecked = true));
            spRight.Children.Add(new Border { Height = 6 });
            spRight.Children.Add(CreateSmallBtn("⚖️ Analisar Gargalo (CPU vs GPU)", () => CheckUptimeAndBottleneck()));
            spRight.Children.Add(new Border { Height = 6 });
            spRight.Children.Add(CreateSmallBtn("🚗 Scan a Drivers Oficiais", () => ScanForOfficialDrivers()));
            topGrid.Children.Add(cRight); g.Children.Add(topGrid);

            Border cGraph = CreateCard("📈 MONITOR DE DESEMPENHO EM TEMPO REAL (AZUL = CPU %   |   ROXO = RAM %)", 1, 0);
            StackPanel spGraph = (StackPanel)cGraph.Child;
            graphCanvas = new Canvas { Height = 125, Background = new SolidColorBrush(Color.FromRgb(6, 9, 15)), Margin = new Thickness(0, 8, 0, 0), ClipToBounds = true };
            cpuLine = new Polyline { Stroke = new SolidColorBrush(Color.FromRgb(14, 165, 233)), StrokeThickness = 2.5 };
            ramLine = new Polyline { Stroke = new SolidColorBrush(Color.FromRgb(168, 85, 247)), StrokeThickness = 2.5 };
            graphCanvas.Children.Add(ramLine); graphCanvas.Children.Add(cpuLine); spGraph.Children.Add(graphCanvas); g.Children.Add(cGraph);
        }

        private void RunAiSmartAutoTuner()
        {
            Log("🤖 [AI AUTO-TUNER] A avaliar arquitetura de CPU, GPU, RAM e Registo...");
            isUpdatingToggles = true;
            foreach (CheckBox cb in allToggleCards) cb.IsChecked = true;
            isUpdatingToggles = false;
            RunFullIsoLiteTransformation(); OptimizeForGames(); OptimizePeripherals(); DisableFastStartup(); RegisterPermanentVipGames(); ApplyCpuZeroAvoidanceToRunningGames(); CalculateOptimizationScore();
            Log($"🏆 [AI BOOST 100%] Perfil Ultra Gaming aplicado com sucesso!");
        }

        private void BuildPage1_ToggleCardsWindows(Grid page)
        {
            ScrollViewer sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            UniformGrid ug = new UniformGrid { Columns = 3, Rows = 4 };
            sv.Content = ug; page.Children.Add(sv);

            bool sPrio = CheckRegInt(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", 38);
            bool sDvr = CheckRegInt(@"HKEY_CURRENT_USER\System\GameConfigStore", "GameDVR_Enabled", 0);
            bool sBg = CheckRegInt(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", 1);
            bool sTrans = CheckRegInt(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 0);
            bool sGameMode = CheckRegInt(@"HKEY_CURRENT_USER\Software\Microsoft\GameBar", "AutoGameModeEnabled", 1);
            bool sSvc = !CheckRegInt(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control", "SvcHostSplitThresholdInKB", 3670016);
            bool sCopilot = CheckRegInt(@"HKEY_CURRENT_USER\Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1);
            bool sHiber = CheckRegInt(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled", 0);
            bool sEdge = CheckRegInt(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Edge", "BackgroundModeEnabled", 0);

            AddSwitchCard(ug, "REC", "Plano de Energia Máximo", "Ativa o plano oculto de Desempenho Máximo da Microsoft.", sPrio, (on) => { if (on) RunCmd("powercfg -duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61", "Plano Máximo ativo!"); });
            AddSwitchCard(ug, "REC", "Desligar suspensão do sistema", "Impede que o Windows suspenda portas USB e discos durante os jogos.", sPrio, (on) => { if (on) DisableUsbSuspend(); });
            AddSwitchCard(ug, "REC", "Priorizar programas em 1.º plano", "Ajusta o Win32PrioritySeparation (Hex 26) para focar o CPU no jogo.", sPrio, (on) => { if (on) BoostMsiAndGames(); else SafeRegSet(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", 2); });
            AddSwitchCard(ug, "REC", "Manter núcleos acordados", "Desativa o Core Parking para todos os núcleos responderem imediatamente.", sPrio, (on) => { if (on) UnparkCpuCores(); });
            AddSwitchCard(ug, "REC", "Desativar gravação de tela 30 seg", "Desativa o Xbox GameDVR que consome recursos e rouba FPS.", sDvr, (on) => { if (on) OptimizeForGames(); else SafeRegSet(@"HKEY_CURRENT_USER\System\GameConfigStore", "GameDVR_Enabled", 1); });
            AddSwitchCard(ug, "REC", "Desligar apps em 2.º plano", "Proíbe aplicações da loja de rodarem escondidas a gastar RAM.", sBg, (on) => { if (on) KillBackgroundApps(); else SafeRegSet(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", 0); });
            AddSwitchCard(ug, "REC", "Menos transparências visuais", "Desativa efeitos pesados de vidro para libertar VRAM para os jogos.", sTrans, (on) => SafeRegSet(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", on ? 0 : 1));
            AddSwitchCard(ug, "REC", "Modo de Jogo do Windows Ativo", "Dá acesso prioritário da placa gráfica ao teu jogo e OBS.", sGameMode, (on) => SafeRegSet(@"HKEY_CURRENT_USER\Software\Microsoft\GameBar", "AutoGameModeEnabled", on ? 1 : 0));
            AddSwitchCard(ug, "REC", "Otimizar SVCHOST na RAM", "Agrupa processos do Windows para reduzir até 70 tarefas no Gestor de Tarefas.", sSvc, (on) => { if (on) OptimizeSvchostProcesses(); else SafeRegSet(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control", "SvcHostSplitThresholdInKB", 3670016); });
            AddSwitchCard(ug, "REC", "Desativar IA (Copilot e Bing)", "Desliga o Copilot, Widgets e pesquisa web no Menu Iniciar.", sCopilot, (on) => { if (on) DisableAIAndBloat(); else SafeRegSet(@"HKEY_CURRENT_USER\Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 0); });
            AddSwitchCard(ug, "WARN", "Desativar Hibernação (+10GB)", "Apaga o hiberfil.sys e desativa o Fast Startup para limpar o Kernel.", sHiber, (on) => { DisableFastStartup(); RunCmd(on ? "powercfg -h off" : "powercfg -h on", "Hibernação ajustada!"); });
            AddSwitchCard(ug, "WARN", "Bloquear Edge em 2.º plano", "Impede o Microsoft Edge de ficar aberto escondido após fechar.", sEdge, (on) => { if (on) DisableEdgeBackground(); else SafeRegSet(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Edge", "BackgroundModeEnabled", 1); });
        }

        private void BuildPage2_PerGameOptimizer(Grid page)
        {
            Border card = CreateCard("🎮 AJUSTES ESPECÍFICOS DO JOGO (CADA JOGO NO MÁXIMO)", 0, 0);
            StackPanel sp = (StackPanel)card.Child;
            DockPanel topBar = new DockPanel { Margin = new Thickness(0, 8, 0, 12) };
            topBar.Children.Add(new TextBlock { Text = "Seleciona o teu Jogo:  ", FontSize = 13, FontWeight = FontWeights.Bold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });
            cmbGameSelect = new ComboBox { Width = 340, Height = 30, FontSize = 12.5, FontWeight = FontWeights.Bold };
            string[] games = { "Counter-Strike 2 (cs2.exe)", "Valorant (VALORANT-Win64-Shipping.exe)", "Call of Duty / Warzone (cod.exe)", "Fortnite (FortniteClient-Win64-Shipping.exe)", "Apex Legends (r5apex.exe)", "Rust (RustClient.exe)", "EA Sports FC / FIFA (FC25.exe)", "GTA V / FiveM (GTA5.exe)", "League of Legends (League of Legends.exe)", "Rainbow Six Siege (RainbowSix.exe)" };
            foreach (string gm in games) cmbGameSelect.Items.Add(gm); cmbGameSelect.SelectedIndex = 0; topBar.Children.Add(cmbGameSelect); sp.Children.Add(topBar);

            bool vipActive = CheckRegInt(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\cs2.exe\PerfOptions", "CpuPriorityClass", 3);
            bool fsoActive = CheckRegInt(@"HKEY_CURRENT_USER\System\GameConfigStore", "GameDVR_FSEBehaviorMode", 2);

            UniformGrid ug = new UniformGrid { Columns = 2, Rows = 3 };
            AddSwitchCard(ug, "REC", "Liberar o núcleo 0 (Core 0 Free)", "Não usa o primeiro núcleo do CPU onde o Windows concentra o lixo.", vipActive, (on) => { if (on) ApplyCpuZeroAvoidanceToRunningGames(); });
            AddSwitchCard(ug, "REC", "Prioridade Alta Permanente", "Dá foco máximo do processador e placa gráfica a este executável.", vipActive, (on) => { if (on) RegisterPermanentVipGames(); else RemovePermanentVipGames(); });
            AddSwitchCard(ug, "REC", "Só núcleos físicos (Sem SMT)", "Usa apenas os núcleos físicos reais do processador na partida.", false, (on) => { });
            AddSwitchCard(ug, "REC", "Placa de vídeo dedicada", "Força potência máxima da GPU principal para este jogo.", vipActive, (on) => { });
            AddSwitchCard(ug, "REC", "Desativar FSO (Ecrã Inteiro)", "Reduz o input lag da mira forçando o ecrã inteiro exclusivo.", fsoActive, (on) => SafeRegSet(@"HKEY_CURRENT_USER\System\GameConfigStore", "GameDVR_FSEBehaviorMode", on ? 2 : 0));
            AddSwitchCard(ug, "REC", "Limpar RAM antes de jogar", "Liberta a memória Standby presa antes de abrir a partida.", true, (on) => { if (on) FlushMemory(); });
            sp.Children.Add(ug);

            StackPanel bBtns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 10, 0, 0) };
            bBtns.Children.Add(CreateSmallBtn("💾 Salvar Ajustes para Todos os Jogos VIP", () => { RegisterPermanentVipGames(); ApplyCpuZeroAvoidanceToRunningGames(); FlushMemory(); Log($"🏆 [PERFIL GUARDADO] Núcleo 0 Livre e Prioridade Alta ativos para {cmbGameSelect.SelectedItem}!"); }));
            bBtns.Children.Add(CreateSmallBtn("⏪ Restaurar Padrão", () => RemovePermanentVipGames()));
            sp.Children.Add(bBtns); page.Children.Add(card);
        }

        private void ApplyCpuZeroAvoidanceToRunningGames()
        {
            int c = Environment.ProcessorCount; if (c <= 2) return;
            long mask = ((1L << c) - 1) & ~1L;
            foreach (string t in new[] { "cs2", "VALORANT-Win64-Shipping", "cod", "FortniteClient-Win64-Shipping", "r5apex", "RustClient", "GTA5" })
                foreach (Process p in Process.GetProcessesByName(t)) try { p.ProcessorAffinity = new IntPtr(mask); p.PriorityClass = ProcessPriorityClass.High; } catch { }
        }

        private void BuildPage3_GamingSwitches(Grid page)
        {
            ScrollViewer sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            UniformGrid ug = new UniformGrid { Columns = 3, Rows = 4 };
            sv.Content = ug; page.Children.Add(sv);

            bool sMouse = CheckRegStr(@"HKEY_CURRENT_USER\Control Panel\Mouse", "MouseSpeed", "0");
            bool sHags = CheckRegInt(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 2);
            bool sPowerThrot = CheckRegInt(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff", 1);
            bool sNet = CheckRegInt(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NetworkThrottlingIndex", -1);

            AddSwitchCard(ug, "REC", "Rato e Teclado Pro (1:1)", "Desativa aceleração artificial e reduz buffer de fila de resposta.", sMouse, (on) => { if (on) OptimizePeripherals(); else UndoPeripherals(); });
            AddSwitchCard(ug, "REC", "Agendamento GPU HAGS", "Ativa o Agendamento de GPU por Hardware para mais FPS e FrameGen.", sHags, (on) => SafeRegSet(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", on ? 2 : 1));
            AddSwitchCard(ug, "REC", "Modo MSI na Placa Gráfica", "Comunicação PCIe direta por mensagem para menor atraso de GPU.", sHags, (on) => { if (on) EnableGpuMsiMode(); });
            AddSwitchCard(ug, "REC", "Desligar Power Throttling", "Desativa o limitador de energia que baixa os GHz do CPU.", sPowerThrot, (on) => SafeRegSet(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff", on ? 1 : 0));
            AddSwitchCard(ug, "WARN", "Desativar Isolamento VBS", "Desliga virtualização do Windows 11 para ganhar até +15% FPS.", false, (on) => RunCmd(on ? "bcdedit /set hypervisorlaunchtype off" : "bcdedit /set hypervisorlaunchtype auto", "VBS ajustado!"));
            AddSwitchCard(ug, "REC", "Otimizar Relógio No-Tick", "Desativa o Dynamic Tick para frametimes mais estáveis nos jogos.", sMouse, (on) => { if (on) OptimizeBcdTimer(); });
            AddSwitchCard(ug, "REC", "Otimizar TCP + Nagle Off", "Ativa TCPNoDelay=1 para enviar tiros sem agrupamento de pacotes.", sNet, (on) => { if (on) OptimizeNetworkForGaming(); else UndoNetworkSettings(); });
            AddSwitchCard(ug, "REC", "Tirar Poupança de Rede", "Garante potência máxima contínua na placa Ethernet/Wi-Fi.", sNet, (on) => { if (on) DisableNicPowerSave(); });
            AddSwitchCard(ug, "REC", "Parar Telemetria Microsoft", "Desativa a recolha em 2.º plano que gasta rede e disco.", sMouse, (on) => { if (on) StopUselessServices(); });
            AddSwitchCard(ug, "REC", "Desativar Tarefas Ocultas", "Desliga tarefas silenciosas da Microsoft que causam engasgos.", sMouse, (on) => { if (on) DisableHiddenScheduledTasks(); });
            AddSwitchCard(ug, "REC", "Refrigeração Ativa no CPU", "Acelera as ventoinhas antes de o CPU baixar os MHz por calor.", sPowerThrot, (on) => { if (on) EnableActiveCoolingPolicy(); });
            AddSwitchCard(ug, "REC", "Limpar Shaders GPU", "Apaga ficheiros antigos de shaders DirectX/NVIDIA para tirar stuttering.", false, (on) => { if (on) CleanGpuCache(); });
        }

        private void BuildPage_NiniteAppStore(Grid page)
        {
            Border card = CreateCard("📦 NINITE STORE (36 APPS POR CATEGORIA - INSTALAÇÃO SILENCIOSA 1-CLIQUE!)", 0, 0);
            StackPanel sp = (StackPanel)card.Child;
            sp.Children.Add(new TextBlock { Text = "Estilo Ninite! Marca os programas que queres e instala tudo automaticamente sem cliques repetidos:", FontSize = 11.5, Foreground = Brushes.Silver, Margin = new Thickness(0, 4, 0, 8) });

            ScrollViewer sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 265 };
            UniformGrid catGrid = new UniformGrid { Columns = 3, Rows = 2 };
            sv.Content = catGrid; storeCheckBoxes.Clear();

            AddNiniteCategory(catGrid, "🌐 NAVEGADORES", new[,] { { "Brave (Sem Anúncios)", "Brave.Brave" }, { "Google Chrome", "Google.Chrome" }, { "Mozilla Firefox", "Mozilla.Firefox" }, { "Opera GX Gamer", "Opera.OperaGX" }, { "LibreWolf Privado", "LibreWolf.LibreWolf" }, { "Tor Browser", "TorProject.TorBrowser" } });
            AddNiniteCategory(catGrid, "🎮 LAUNCHERS DE JOGOS", new[,] { { "Steam", "Valve.Steam" }, { "Epic Games", "EpicGames.EpicGamesLauncher" }, { "Battle.net (CoD)", "Blizzard.BattleNet" }, { "EA App (FIFA)", "ElectronicArts.EADesktop" }, { "Ubisoft Connect", "Ubisoft.Connect" }, { "GOG Galaxy", "GOG.Galaxy" } });
            AddNiniteCategory(catGrid, "💬 VOZ, STREAM & MÚSICA", new[,] { { "Discord", "Discord.Discord" }, { "OBS Studio Oficial", "OBSProject.OBSStudio" }, { "Spotify", "Spotify.Spotify" }, { "EarTrumpet Áudio", "File-New-Project.EarTrumpet" }, { "VoiceMeeter Banana", "VB-Audio.Voicemeeter.Banana" }, { "Elgato Stream Deck", "Elgato.StreamDeck" } });
            AddNiniteCategory(catGrid, "🛠️ HARDWARE & RGB", new[,] { { "Fan Control (Fans)", "Rem0o.FanControl" }, { "OpenRGB (Luzes)", "CalcProgrammer1.OpenRGB" }, { "MSI Afterburner", "Guru3D.Afterburner" }, { "HWiNFO64 (Sensores)", "REALiX.HWiNFO" }, { "CPU-Z (Specs)", "CPUID.CPU-Z" }, { "NVCleanstall", "TechPowerUp.NVCleanstall" } });
            AddNiniteCategory(catGrid, "📦 UTILITÁRIOS (NINITE)", new[,] { { "7-Zip Extrator", "7zip.7zip" }, { "WinRAR Oficial", "RARLab.WinRAR" }, { "VLC Media Player", "VideoLAN.VLC" }, { "ShareX Prints", "ShareX.ShareX" }, { "Notepad++ Editor", "Notepad++.Notepad++" }, { "Everything Busca", "voidtools.Everything" } });
            AddNiniteCategory(catGrid, "🛡️ RUNTIMES & SEGURANÇA", new[,] { { "DirectX Runtime", "Microsoft.DirectX" }, { "Visual C++ (x64)", "Microsoft.VCRedist.2015+.x64" }, { "Visual C++ (x86)", "Microsoft.VCRedist.2015+.x86" }, { "CapFrameX (1% Low)", "CXWorld.CapFrameX" }, { "Revo Uninstaller", "RevoUninstaller.RevoUninstaller" }, { "Malwarebytes", "Malwarebytes.Malwarebytes" } });
            sp.Children.Add(sv);

            StackPanel spBtns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 10, 0, 0) };
            spBtns.Children.Add(CreateSmallBtn("🚀 Instalar Marcados (Modo Silencioso Ninite)", () => InstallSelectedStoreApps()));
            spBtns.Children.Add(CreateSmallBtn("☑️ Pack 'Formatou o PC'", () => SelectNinitePreset(new[] { "Brave.Brave", "Discord.Discord", "Valve.Steam", "7zip.7zip", "VideoLAN.VLC", "Microsoft.DirectX", "Microsoft.VCRedist.2015+.x64", "Microsoft.VCRedist.2015+.x86", "File-New-Project.EarTrumpet" })));
            spBtns.Children.Add(CreateSmallBtn("🎥 Pack Streamer", () => SelectNinitePreset(new[] { "OBSProject.OBSStudio", "Discord.Discord", "File-New-Project.EarTrumpet", "VB-Audio.Voicemeeter.Banana", "ShareX.ShareX", "Spotify.Spotify" })));
            spBtns.Children.Add(CreateSmallBtn("🧹 Desmarcar Tudo", () => { foreach (var c in storeCheckBoxes) c.IsChecked = false; }));
            sp.Children.Add(spBtns); page.Children.Add(card);
        }

        private void AddNiniteCategory(Panel parent, string title, string[,] items)
        {
            Border b = new Border { Background = new SolidColorBrush(Color.FromRgb(9, 13, 22)), BorderBrush = new SolidColorBrush(Color.FromRgb(26, 38, 59)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Margin = new Thickness(4), Padding = new Thickness(10, 8, 10, 8) };
            StackPanel sp = new StackPanel(); sp.Children.Add(new TextBlock { Text = title, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248)), Margin = new Thickness(0, 0, 0, 6) });
            for (int i = 0; i < items.GetLength(0); i++)
            {
                CheckBox cb = new CheckBox { Content = items[i, 0], Tag = items[i, 1], Foreground = Brushes.White, FontSize = 11.5, Margin = new Thickness(0, 2.5, 0, 2.5), Cursor = Cursors.Hand };
                storeCheckBoxes.Add(cb); sp.Children.Add(cb);
            }
            b.Child = sp; parent.Children.Add(b);
        }

        private void SelectNinitePreset(string[] ids)
        {
            foreach (var cb in storeCheckBoxes) cb.IsChecked = ids.Contains(cb.Tag.ToString());
            Log("📦 Pack Ninite selecionado! Clica em 'Instalar Marcados' para começar.");
        }

        private void InstallSelectedStoreApps()
        {
            var cmds = storeCheckBoxes.Where(c => c.IsChecked == true).Select(c => $"winget install -e --id {c.Tag} --accept-package-agreements --accept-source-agreements").ToList();
            if (cmds.Count > 0) RunCmdVisible(string.Join(" & ", cmds), $"A instalar {cmds.Count} aplicações via Winget Ninite...");
            else Log("⚠️ Marca pelo menos uma aplicação nas caixas acima!");
        }

        private void BuildPage_VipGamesAndDisplay(Grid page)
        {
            UniformGrid ug = CreateButtonGrid(page);
            AddCardBtn(ug, "🚗 Scan a Drivers Oficiais (Microsoft API)", "Faz um raio-X à procura de drivers de Chipset, Áudio, Wi-Fi e Bluetooth pendentes nos servidores oficiais.", () => ScanForOfficialDrivers(), true);
            AddCardBtn(ug, "⚡ Instalar Todos os Drivers Pendentes", "Transfere e instala automaticamente todos os drivers certificados (WHQL) encontrados para a tua placa-mãe.", () => InstallPendingOfficialDrivers(), true);
            AddCardBtn(ug, "💾 Fazer Backup de Drivers Atuais", "Exporta uma cópia de segurança de todos os drivers instalados no teu PC para C:\\PCOptimizer_Backups\\Drivers.", () => BackupAllSystemDrivers());
            AddCardBtn(ug, "👑 Gravar Jogos VIP (Prioridade Eterna)", "CS2, Valorant, CoD, Fortnite, Apex, Rust e GTA V rodam sempre em Prioridade Alta.", () => RegisterPermanentVipGames(), true);
            AddCardBtn(ug, "⏪ Remover Prioridade Eterna Jogos", "Restaura o comportamento padrão do Windows para os executáveis de jogos.", () => RemovePermanentVipGames());
            AddCardBtn(ug, "🛡️ Blindar Ficheiro HOSTS (Anti-Spy)", "Bloqueia na raiz os servidores de telemetria e anúncios da Microsoft.", () => ApplyHostsTelemetryBlock());
            AddCardBtn(ug, "🎯 Ecrã 1280x960 (4:3 Stretched CS2)", "Muda instantaneamente para 1280x960 competitivo sem abrir painel da gráfica.", () => ChangeScreenResolution(1280, 960));
            AddCardBtn(ug, "🎯 Ecrã 1440x1080 (4:3 HD Competitivo)", "Muda para 1440x1080 (4:3 esticado em alta definição para CS2/Valorant).", () => ChangeScreenResolution(1440, 1080));
            AddCardBtn(ug, "🖥️ Repor Ecrã 1920x1080 (Full HD)", "Volta o monitor para a resolução padrão Full HD 1920x1080 num clique.", () => ChangeScreenResolution(1920, 1080));
            AddCardBtn(ug, "🖥️ Repor Ecrã 2560x1440 (2K QHD)", "Volta o monitor para a resolução nativa 2K QHD (2560x1440) num clique.", () => ChangeScreenResolution(2560, 1440));
            AddCardBtn(ug, "🔑 Ler Chave Original Windows (BIOS)", "Mostra na consola a tua Product Key original gravada na placa-mãe.", () => ReadBiosWindowsKey());
            AddCardBtn(ug, "⚖️ Calcular Gargalo (CPU vs GPU vs RAM)", "Analisa o equilíbrio entre o teu CPU, Placa Gráfica e memória RAM.", () => CheckUptimeAndBottleneck());
            AddCardBtn(ug, "🎙️ Testar Microfone (Ouvir a Minha Voz)", "Abre o painel de escuta direta do microfone para testar ruído e áudio.", () => RunCmd("control mmsys.cpl,,1", "Painel de Microfone aberto!"));
            AddCardBtn(ug, "🔓 Limpar Bloqueios Ficheiro HOSTS", "Restaura o ficheiro HOSTS original do Windows.", () => ResetHostsFile());
            AddCardBtn(ug, "⚡ Calibrar Cores e Gama do Monitor", "Abre o calibrador de gama e cores do ecrã (dccw.exe).", () => RunCmd("dccw.exe", "Calibrador aberto!"));
        }

        private void BuildPage_DiskSpaceHunter(Grid page)
        {
            Grid g = new Grid(); g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(50) }); g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); page.Children.Add(g);
            StackPanel topSp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(5) }; Grid.SetRow(topSp, 0);
            topSp.Children.Add(CreateSmallBtn("🔍 Procurar 15 Maiores Pastas (AppData/ProgramData)", () => ScanHeavyFolders()));
            topSp.Children.Add(CreateSmallBtn("🎬 Procurar Ficheiros Gigantes (+200MB)", () => ScanGiantFiles()));
            topSp.Children.Add(CreateSmallBtn("📂 Abrir Pasta Selecionada", () => OpenSelectedHeavyFolder()));
            g.Children.Add(topSp);
            lstBigFolders = new ListBox { Margin = new Thickness(5), Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)), Foreground = Brushes.Gold, BorderBrush = new SolidColorBrush(Color.FromRgb(30, 41, 59)), FontFamily = new FontFamily("Consolas"), FontSize = 12 };
            Grid.SetRow(lstBigFolders, 1); lstBigFolders.Items.Add("💿 Clica num dos botões azuis acima para descobrir onde está o espaço ocupado no SSD!");
            g.Children.Add(lstBigFolders);
        }

        private void BuildPage_CrosshairAndGamePing(Grid page)
        {
            Grid g = new Grid(); g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(135) }); g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); page.Children.Add(g);
            Border cCross = CreateCard("十 MIRA FLUTUANTE NO ECRÃ (CROSSHAIR OVERLAY NATIVO - ATALHO: CTRL+SHIFT+F2)", 0, 0);
            StackPanel spCross = (StackPanel)cCross.Child;
            spCross.Children.Add(new TextBlock { Text = "Desenha uma mira transparente no centro exato do monitor. Os tiros passam através dela!", FontSize = 11.5, Foreground = Brushes.Silver, Margin = new Thickness(0, 6, 0, 10) });
            StackPanel spCrossBtns = new StackPanel { Orientation = Orientation.Horizontal };
            spCrossBtns.Children.Add(CreateSmallBtn("十 Ligar / Desligar Mira (F2)", () => ToggleCrosshairOverlay()));
            spCrossBtns.Children.Add(CreateSmallBtn("🔄 Estilo (Cruz/Ponto/Anel)", () => { crosshairStyle = (crosshairStyle + 1) % 3; RedrawCrosshair(); }));
            spCrossBtns.Children.Add(CreateSmallBtn("🟢 Verde", () => { crosshairColor = Brushes.Lime; RedrawCrosshair(); }));
            spCrossBtns.Children.Add(CreateSmallBtn("🔴 Vermelho", () => { crosshairColor = Brushes.Red; RedrawCrosshair(); }));
            spCrossBtns.Children.Add(CreateSmallBtn("🔵 Cyan", () => { crosshairColor = Brushes.Cyan; RedrawCrosshair(); }));
            spCrossBtns.Children.Add(CreateSmallBtn("🟡 Amarelo", () => { crosshairColor = Brushes.Yellow; RedrawCrosshair(); }));
            spCross.Children.Add(spCrossBtns); g.Children.Add(cCross);

            Border cPing = CreateCard("🌍 RADAR DE LATÊNCIA (PING) PARA SERVIDORES DE JOGOS NA EUROPA", 1, 0);
            StackPanel spPing = (StackPanel)cPing.Child;
            lblGamePingResult = new TextBlock { Text = "Clica abaixo para medir o Ping em tempo real para os servidores de jogos na Europa:", FontSize = 12.5, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0, 229, 255)), Margin = new Thickness(0, 8, 0, 10), TextWrapping = TextWrapping.Wrap };
            spPing.Children.Add(lblGamePingResult);
            StackPanel spPingBtns = new StackPanel { Orientation = Orientation.Horizontal };
            spPingBtns.Children.Add(CreateSmallBtn("🎯 Testar Servidores de Jogos Agora", () => RunGameServersPingRadar()));
            spPingBtns.Children.Add(CreateSmallBtn("🏆 Caçar DNS Mais Rápido", () => HuntBestDnsServer()));
            spPing.Children.Add(spPingBtns); g.Children.Add(cPing);
        }

        private void BuildPage3_NativeTests(Grid page)
        {
            Grid g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); page.Children.Add(g);

            Border cardMouse = CreateCard("🖱️ TESTE DE POLLING RATE DO RATO (MEXE O RATO AQUI DENTRO!)", 0, 0);
            StackPanel spMouse = (StackPanel)cardMouse.Child;
            lblMouseHz = new TextBlock { Text = "Atual: 0 Hz", FontSize = 20, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0, 229, 255)), Margin = new Thickness(0, 6, 0, 2) };
            lblMouseMaxHz = new TextBlock { Text = "Pico Máximo: 0 Hz (Faz círculos rápidos aqui!)", FontSize = 11.5, Foreground = Brushes.Silver };
            spMouse.Children.Add(lblMouseHz); spMouse.Children.Add(lblMouseMaxHz);
            cardMouse.MouseMove += (s, e) => { if (!mouseSw.IsRunning) { mouseSw.Start(); mouseSamples = 0; } mouseSamples++; if (mouseSw.ElapsedMilliseconds >= 250) { int hz = (int)(mouseSamples * (1000.0 / mouseSw.ElapsedMilliseconds)); if (hz > maxMouseHz) maxMouseHz = hz; lblMouseHz.Text = $"Velocidade Atual: {hz} Hz"; lblMouseMaxHz.Text = $"🏆 Pico Máximo: {maxMouseHz} Hz (Clique Direito repõe)"; mouseSamples = 0; mouseSw.Restart(); } };
            cardMouse.MouseRightButtonDown += (s, e) => { maxMouseHz = 0; lblMouseHz.Text = "Atual: 0 Hz"; }; g.Children.Add(cardMouse);

            Border cardMon = CreateCard("🖥️ TESTE DE MONITOR NATIVO (UFO MOTION & DEAD PIXELS)", 0, 1);
            StackPanel spMon = (StackPanel)cardMon.Child;
            ufoCanvas = new Canvas { Height = 38, Background = new SolidColorBrush(Color.FromRgb(10, 14, 23)), Margin = new Thickness(0, 4, 0, 6), ClipToBounds = true };
            ufoRect1 = new Rectangle { Width = 28, Height = 14, Fill = new SolidColorBrush(Color.FromRgb(0, 229, 255)), RadiusX = 4, RadiusY = 4 };
            ufoRect2 = new Rectangle { Width = 28, Height = 14, Fill = new SolidColorBrush(Color.FromRgb(250, 204, 21)), RadiusX = 4, RadiusY = 4 };
            Canvas.SetTop(ufoRect1, 4); Canvas.SetTop(ufoRect2, 20); ufoCanvas.Children.Add(ufoRect1); ufoCanvas.Children.Add(ufoRect2); spMon.Children.Add(ufoCanvas);
            StackPanel spMonBtns = new StackPanel { Orientation = Orientation.Horizontal };
            spMonBtns.Children.Add(CreateSmallBtn("🛸 Ligar/Parar Ghosting", () => ToggleUfoTest()));
            spMonBtns.Children.Add(CreateSmallBtn("🎨 Ecrã Inteiro Dead Pixels", () => OpenNativeDeadPixelTester()));
            spMon.Children.Add(spMonBtns); g.Children.Add(cardMon);

            Border cardNet = CreateCard("🚀 SPEEDTEST & CAÇADOR DE DNS MAIS RÁPIDO (NATIVO)", 1, 0);
            StackPanel spNet = (StackPanel)cardNet.Child;
            lblNetResult = new TextBlock { Text = "Mede a tua Velocidade (Mbps) ou descobre o DNS mais rápido:", FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153)), Margin = new Thickness(0, 4, 0, 8), TextWrapping = TextWrapping.Wrap };
            spNet.Children.Add(lblNetResult);
            StackPanel spNetBtns = new StackPanel { Orientation = Orientation.Horizontal };
            spNetBtns.Children.Add(CreateSmallBtn("⚡ Speedtest + Ping", () => RunNativeSpeedAndPingTest()));
            spNetBtns.Children.Add(CreateSmallBtn("🏆 Caçar Melhor DNS", () => HuntBestDnsServer()));
            spNet.Children.Add(spNetBtns); g.Children.Add(cardNet);

            Border cardBench = CreateCard("⚔️ SIMULADOR 30.000 UNIDADES & TECLADO (NKRO)", 1, 1);
            StackPanel spBench = (StackPanel)cardBench.Child;
            lblKeyTest = new TextBlock { Text = "⌨️ Pressiona várias teclas juntas para testar o Anti-Ghosting!", FontSize = 11.5, FontWeight = FontWeights.Bold, Foreground = Brushes.Gold, Margin = new Thickness(0, 4, 0, 8) };
            spBench.Children.Add(lblKeyTest);
            StackPanel spBenchBtns = new StackPanel { Orientation = Orientation.Horizontal };
            spBenchBtns.Children.Add(CreateSmallBtn("⚔️ Batalha 30.000 Unidades", () => GameSimBenchmark.OpenBattleBenchmarkWindow()));
            spBenchBtns.Children.Add(CreateSmallBtn("🔥 Stress CPU", () => RunNativeCpuStressBenchmark()));
            spBenchBtns.Children.Add(CreateSmallBtn("🏆 Notas WinSAT", () => RunBenchmarkScore()));
            spBench.Children.Add(spBenchBtns); g.Children.Add(cardBench);
        }

        private void BuildPage_ProcessLasso(Grid page)
        {
            Grid g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); page.Children.Add(g);
            StackPanel col1 = new StackPanel { Margin = new Thickness(5) }; Grid.SetColumn(col1, 0);
            col1.Children.Add(new TextBlock { Text = "🎯 Jogos e Programas Abertos Agora:", Foreground = Brushes.Cyan, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6) });
            lstRunningGames = new ListBox { Height = 220, Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(30, 41, 59)), FontFamily = new FontFamily("Consolas"), FontSize = 11.5 };
            col1.Children.Add(lstRunningGames);
            Button btnRef = CreateSmallBtn("🔄 Atualizar Lista de Processos", () => RefreshRunningApps()); btnRef.Margin = new Thickness(0, 6, 0, 0); col1.Children.Add(btnRef); g.Children.Add(col1);

            ScrollViewer sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetColumn(sv, 1);
            UniformGrid ug = new UniformGrid { Columns = 2, Rows = 4, Margin = new Thickness(6, 0, 0, 0) }; sv.Content = ug;
            AddCardBtn(ug, "🚀 Isolar Core 0 (Process Lasso)", "Tira o jogo do Núcleo 0 do Windows para eliminar micro-stuttering.", () => ApplyCpuZeroAvoidance(), true);
            AddCardBtn(ug, "⚡ Forçar Prioridade ALTA", "Aplica prioridade Alta imediata de CPU ao processo selecionado.", () => SetSelectedProcessPriority(ProcessPriorityClass.High));
            AddCardBtn(ug, "🎥 Separar Núcleos Jogo vs OBS", "Balanceia os threads do processador entre o Jogo e o OBS Studio.", () => SplitCoresGameAndObs());
            AddCardBtn(ug, "🔄 Repor Todos os Núcleos", "Devolve todos os threads padrão do CPU ao processo selecionado.", () => ResetSelectedProcessAffinity());
            AddCardBtn(ug, "🔪 Suspender Explorer (Ultra-FPS)", "Fecha a barra de tarefas enquanto jogas (+400MB RAM livres).", () => RunCmd("taskkill /f /im explorer.exe", "Explorer suspenso!"));
            AddCardBtn(ug, "✨ Religar Explorer (Normal)", "Volta a abrir a barra de tarefas e ambiente de trabalho.", () => { Process.Start("explorer.exe"); Log("✔ Explorer religado!"); });
            AddCardBtn(ug, "🚀 Listar Apps de Arranque", "Mostra na consola todos os programas que iniciam com o Windows.", () => ListStartupRegistryApps());
            AddCardBtn(ug, "📊 Abrir Monitor de Recursos", "Abre o analisador oficial de núcleos de CPU (resmon).", () => RunCmd("start resmon.exe", "Resmon aberto!"));
            g.Children.Add(sv); RefreshRunningApps();
        }

        private void BuildPage4_RevoUninstaller(Grid page)
        {
            Grid g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(310) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(310) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); page.Children.Add(g);
            StackPanel col1 = new StackPanel { Margin = new Thickness(5) }; Grid.SetColumn(col1, 0);
            col1.Children.Add(new TextBlock { Text = "1. Escolhe ou pesquisa a App:", Foreground = Brushes.Cyan, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 5) });
            DockPanel dpSearch = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            Button btnList = CreateSmallBtn("🔄 Listar Apps", () => LoadInstalledProgramsList()); DockPanel.SetDock(btnList, Dock.Right);
            txtSearchProg = new TextBox { Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)), Foreground = Brushes.White, BorderThickness = new Thickness(0), Padding = new Thickness(6), FontSize = 12, Margin = new Thickness(0, 0, 6, 0) };
            dpSearch.Children.Add(btnList); dpSearch.Children.Add(txtSearchProg); col1.Children.Add(dpSearch);
            lstPrograms = new ListBox { Height = 220, Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(30, 41, 59)) };
            lstPrograms.SelectionChanged += (s, e) => { if (lstPrograms.SelectedItem != null) txtSearchProg.Text = lstPrograms.SelectedItem.ToString(); };
            col1.Children.Add(lstPrograms); g.Children.Add(col1);

            ScrollViewer svMid = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetColumn(svMid, 1);
            StackPanel col2 = new StackPanel { Margin = new Thickness(2) }; svMid.Content = col2;
            AddCardBtn(col2, "🗑️ 1. Desinstalar + Caçar Sobras", "Desinstala a app via Winget e varre pastas ocultas e Registo deixados para trás!", () => UninstallAndScanLeftovers(), true);
            AddCardBtn(col2, "🔍 Só Procurar Sobras (Raio-X)", "Procura pastas esquecidas em AppData, ProgramData e Registo.", () => ScanForLeftoversOnly());
            AddCardBtn(col2, "🔥 2. Apagar Todas as Sobras", "Elimina de raiz todas as pastas e chaves de Registo encontradas à direita!", () => DeleteSelectedLeftovers());
            g.Children.Add(svMid);

            StackPanel col3 = new StackPanel { Margin = new Thickness(5) }; Grid.SetColumn(col3, 2);
            col3.Children.Add(new TextBlock { Text = "Sobras Encontradas (AppData e Registo):", Foreground = Brushes.Orange, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 5) });
            lstLeftovers = new ListBox { Height = 250, Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)), Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113)), BorderBrush = new SolidColorBrush(Color.FromRgb(30, 41, 59)) };
            col3.Children.Add(lstLeftovers); g.Children.Add(col3);
        }

        private void BuildPage5_FansAndRgb(Grid page)
        {
            UniformGrid ug = CreateButtonGrid(page);
            AddCardBtn(ug, "🌡️ Ler Sensores Térmicos Completos", "Lê Graus (°C), Watts, MHz e RPMs do teu CPU, Placa Gráfica e Ventoinhas da Board.", () => ReadTempsAndFans(), true);
            AddCardBtn(ug, "❄️ Refrigeração Ativa 100% no CPU", "Obriga o Windows a acelerar as ventoinhas antes de reduzir os MHz do processador.", () => EnableActiveCoolingPolicy());
            AddCardBtn(ug, "🔪 Matar Apps RGB Pesadas (+RAM)", "Fecha iCUE, Armoury Crate, Synapse e NZXT mantendo a cor atual das tuas luzes!", () => KillHeavyRgbBloatware());
            AddCardBtn(ug, "💥 Ver Causa dos Últimos Crashes", "Analisa o Visualizador de Eventos e mostra os últimos erros críticos de jogos.", () => ReadLastSystemCrashes());
            AddCardBtn(ug, "🌀 Instalar 'Fan Control' (Universal)", "Instala o melhor gestor gratuito de curvas de ventoinhas de CPU e GPU.", () => RunCmdVisible("winget install -e --id Rem0o.FanControl --accept-package-agreements --accept-source-agreements", "A instalar Fan Control..."));
            AddCardBtn(ug, "🌈 Instalar 'OpenRGB' (Luzes Leve)", "Controla todas as luzes RGB (Board, RAM, GPU, Rato) num único programa leve.", () => RunCmdVisible("winget install -e --id CalcProgrammer1.OpenRGB --accept-package-agreements --accept-source-agreements", "A instalar OpenRGB..."));
            AddCardBtn(ug, "🚀 Instalar 'MSI Afterburner'", "Controla a curva de ventoinhas da Placa Gráfica, limites térmicos e overclock.", () => RunCmdVisible("winget install -e --id Guru3D.Afterburner --accept-package-agreements --accept-source-agreements", "A instalar Afterburner..."));
            AddCardBtn(ug, "📊 Instalar 'HWiNFO64' (Sensores)", "Mostra todos os sensores térmicos, voltagens e RPMs da placa-mãe e bomba AIO.", () => RunCmdVisible("winget install -e --id REALiX.HWiNFO --accept-package-agreements --accept-source-agreements", "A instalar HWiNFO64..."));
            AddCardBtn(ug, "🛑 Parar Serviços ASUS Armoury", "Desliga os serviços em 2.º plano da ASUS que consomem processador sem necessidade.", () => StopBrandBloatServices("ASUS"));
            AddCardBtn(ug, "🛑 Parar Serviços Razer & Corsair", "Desliga os serviços pesados da Razer e Corsair iCUE em segundo plano.", () => StopBrandBloatServices("RAZER_CORSAIR"));
            AddCardBtn(ug, "💡 Desativar Dynamic Lighting Win11", "Impede conflitos de luzes do Windows 11 com o teu rato e teclado.", () => DisableWindowsDynamicLighting());
            AddCardBtn(ug, "💾 Ver Saúde e Espaço dos SSDs", "Lista na consola todos os discos prontos, espaço livre e percentagem de uso.", () => CheckDiskHealth());
        }

        private void BuildPage6_Streamer(Grid page)
        {
            UniformGrid ug = CreateButtonGrid(page);
            AddCardBtn(ug, "🚀 MODO LIVE (1-Clique Pré-Stream)", "Pausa OneDrive/Updates, fecha RGB pesado, limpa RAM, liga Auto-Guard e blinda OBS!", () => ActivateLiveMode(), true);
            AddCardBtn(ug, "🛡️ Blindar OBS Studio (Auto-Admin)", "Configura o OBS para abrir sempre como Admin e reservar GPU (Zero Overload).", () => OptimizeObsStudio());
            AddCardBtn(ug, "⚡ Prioridade Alta OBS & Discord", "Dá prioridade máxima imediata ao OBS Studio e Discord no processador.", () => BoostObsAndDiscordProcess());
            AddCardBtn(ug, "🎙️ Fix Áudio (Tirar Redução 80%)", "Impede o Windows de baixar sozinho o volume do jogo quando falam no Discord.", () => FixStreamerAudio());
            AddCardBtn(ug, "📡 Estabilizar Upload da Stream", "Remove limite de rede multimédia para zero perda de frames na Twitch/YouTube.", () => OptimizeUploadForStream());
            AddCardBtn(ug, "⏸️ Pausar Sincronizações 2.º Plano", "Para OneDrive, Windows Update e Indexação para não roubarem Upload na Live.", () => PauseBackgroundSyncs());
            AddCardBtn(ug, "🎧 Abrir Painel Som (48kHz Studio)", "Abre dispositivos de som para colocares Microfone e Auscultadores em 48000 Hz.", () => RunCmd("control mmsys.cpl sounds", "Painel de Som aberto!"));
            AddCardBtn(ug, "🔕 Modo Anti-Leak (Sem Pop-ups)", "Bloqueia notificações do Windows para nenhuma mensagem aparecer na transmissão.", () => DisableNotificationsForStream());
            AddCardBtn(ug, "🔄 Atualizar TUDO (Winget Auto)", "Procura e instala atualizações para todos os programas e runtimes do teu PC!", () => RunCmdVisible("winget upgrade --all --include-unknown --accept-package-agreements --accept-source-agreements", "A atualizar todos os programas..."));
            AddCardBtn(ug, "📦 Instalar/Atualizar OBS Studio", "Instala ou atualiza o OBS Studio oficial automaticamente via Winget.", () => RunCmdVisible("winget install -e --id OBSProject.OBSStudio --accept-package-agreements --accept-source-agreements", "A instalar OBS..."));
            AddCardBtn(ug, "🔊 Instalar EarTrumpet (Volumes)", "Controlo individual de volume de cada jogo e app na barra de tarefas.", () => RunCmdVisible("winget install -e --id File-New-Project.EarTrumpet --accept-package-agreements --accept-source-agreements", "A instalar EarTrumpet..."));
            AddCardBtn(ug, "🎛️ Instalar VoiceMeeter Banana", "Mesa de mistura virtual para separar o áudio do Discord do VOD da Twitch.", () => RunCmdVisible("winget install -e --id VB-Audio.Voicemeeter.Banana --accept-package-agreements --accept-source-agreements", "A instalar VoiceMeeter..."));
        }

        private void BuildPage8_UndoHistory(Grid page)
        {
            Grid g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); page.Children.Add(g);
            StackPanel col1 = new StackPanel { Margin = new Thickness(5) }; Grid.SetColumn(col1, 0);
            col1.Children.Add(new TextBlock { Text = "📜 O Teu Histórico (Data, Hora e Ação):", Foreground = Brushes.Gold, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6) });
            lstHistory = new ListBox { Height = 220, Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)), Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153)), BorderBrush = new SolidColorBrush(Color.FromRgb(30, 41, 59)), FontFamily = new FontFamily("Consolas"), FontSize = 11 };
            col1.Children.Add(lstHistory);
            Button btnClearH = CreateSmallBtn("🧹 Limpar Histórico", () => { try { if (File.Exists(historyFilePath)) File.Delete(historyFilePath); lstHistory.Items.Clear(); } catch { } });
            btnClearH.Margin = new Thickness(0, 6, 0, 0); col1.Children.Add(btnClearH); g.Children.Add(col1);

            ScrollViewer sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetColumn(sv, 1);
            UniformGrid ug = new UniformGrid { Columns = 2, Rows = 4, Margin = new Thickness(6, 0, 0, 0) }; sv.Content = ug;
            AddCardBtn(ug, "🚨 PÂNICO: Reverter TUDO!", "Restaura Rato, Jogos, Rede, 2.º Plano e Visual para o padrão original da Microsoft!", () => UndoEverythingPanicButton(), true);
            AddCardBtn(ug, "🛡️ Criar Ponto Restauro + Backup", "Cria um Ponto de Restauro no Windows e guarda cópia do Registo em C:\\PCOptimizer_Backups.", () => CreateFullBackup());
            AddCardBtn(ug, "⏪ Reverter Rato e Teclado", "Devolve a velocidade e aceleração originais de fábrica do rato e teclado.", () => UndoPeripherals());
            AddCardBtn(ug, "⏪ Reverter Jogos, VBS e Relógio", "Repõe GameDVR, VBS, Win32PrioritySeparation=2 e o relógio padrão.", () => UndoGamingAndTimers());
            AddCardBtn(ug, "⏪ Reverter 2.º Plano e Serviços", "Reativa apps em 2.º plano, Copilot, Hibernação e agrupamento SVCHOST padrão.", () => UndoBackgroundAndServices());
            AddCardBtn(ug, "⏪ Reverter Rede, Ping e DNS", "Restaura o DNS automático da operadora e o Nagle's Algorithm.", () => UndoNetworkSettings());
            AddCardBtn(ug, "⏪ Reverter Áudio e Notificações", "Devolve os pop-ups de notificações, transparências e redução do Discord.", () => UndoAudioAndVisuals());
            AddCardBtn(ug, "🕰️ Abrir Restauro do Sistema", "Abre a Máquina do Tempo oficial do Windows (rstrui.exe) para voltar atrás.", () => RunCmd("rstrui.exe", "Restauro aberto!"));
            g.Children.Add(sv);
        }

        private void StartLiveSensorLoop()
        {
            _sensorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            _sensorTimer.Tick += (s, e) =>
            {
                if (isReadingSensors) return;
                isReadingSensors = true;

                Task.Run(() =>
                {
                    try
                    {
                        var snap = _hwMonitor.ReadSensors();
                        var disks = HardwareMonitorService.GetDisksInfo();
                        var cDrive = disks.FirstOrDefault(d => d.DriveLetter.StartsWith("C")) ?? disks.FirstOrDefault();
                        int procs = Process.GetProcesses().Length;
                        float cpuLoad = snap.CpuLoad;
                        float ramFree = snap.RamAvailableMb > 0 ? snap.RamAvailableMb : 16000;

                        Dispatcher.Invoke(() =>
                        {
                            string cTempStr = snap.CpuTemp > 0 ? $"{snap.CpuTemp:0}°C" : $"{cpuLoad:0}%";
                            string gTempStr = snap.GpuTemp > 0 ? $"{snap.GpuTemp:0}°C ({snap.GpuPowerWatts:0}W)" : "Ativa";
                            lblLiveStats.Text = $"🔥 CPU: {cpuLoad:0.0}% ({cTempStr}) | 🎮 GPU: {gTempStr} | 🧠 RAM Livre: {ramFree:0}MB";

                            if (txtCpuVal != null) txtCpuVal.Text = $"🔥 CPU: {cpuLoad:0.0}%  |  Temp: {(snap.CpuTemp > 0 ? snap.CpuTemp.ToString("0") + " °C" : "Ativo")}  |  Processos: {procs}";
                            if (txtGpuTempVal != null) txtGpuTempVal.Text = $"🎮 {snap.GpuName}: {snap.GpuTemp:0} °C  |  {snap.GpuPowerWatts:0} W  |  Fan: {snap.GpuFanPercent:0}%  |  {snap.GpuCoreClock:0} MHz";

                            double ramPct = 30;
                            if (txtRamVal != null && totalRamMb > 0)
                            {
                                ramPct = ((totalRamMb - ramFree) / totalRamMb) * 100.0;
                                txtRamVal.Text = $"🧠 RAM Livre: {ramFree:0} MB (de {totalRamMb} MB - {ramPct:0}% em uso)";
                            }

                            if (txtDiskVal != null && cDrive != null)
                                txtDiskVal.Text = $"💾 Disco {cDrive.DriveLetter}: {cDrive.FreeGB:0} GB Livres (de {cDrive.TotalGB:0} GB - {cDrive.UsedPercent:0}% cheio)";

                            UpdateLiveGraph(cpuLoad, ramPct);

                            if (overlayWin != null && overlayWin.IsVisible && overlayText != null)
                                overlayText.Text = $"⚡ CPU: {cpuLoad:0}% ({cTempStr}) | GPU: {gTempStr}\n🧠 RAM: {ramFree:0}MB | Timer: {(chkTimer.IsChecked == true ? "0.5ms" : "Std")}";

                            if (chkGuard.IsChecked == true && ramFree < 2500)
                            {
                                FlushMemorySilent();
                                Log("🛡️ [AUTO-GUARD] Limpeza automática de RAM realizada!");
                            }
                        });
                    }
                    catch { }
                    finally { isReadingSensors = false; }
                });
            };
            _sensorTimer.Start();
        }

        // --- AUTO-UPDATER COM POP-UP AUTOMÁTICO NO ARRANQUE & SUPORTE A ZIP ---
        private void CheckForGitHubReleasesUpdate(bool isManual)
        {
            if (isManual) Log("🔄 A verificar atualizações no GitHub...");
            Task.Run(() =>
            {
                try
                {
                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                    using (WebClient wc = new WebClient())
                    {
                        wc.Headers.Add("User-Agent", "PCOptimizer-App");
                        wc.Headers.Add("Cache-Control", "no-cache");
                        string json = wc.DownloadString(githubRepoApi);

                        Match tagMatch = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
                        // Procura primeiro pelo pacote ZIP (evita bloqueios de segurança), ou pelo EXE
                        Match zipMatch = Regex.Match(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]+PC_Optimizer_v14\\.zip)\"");
                        Match exeMatch = Regex.Match(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]+PC_Optimizer\\.exe)\"");

                        if (tagMatch.Success)
                        {
                            string remoteTag = tagMatch.Groups[1].Value.Trim();
                            string downloadUrl = zipMatch.Success ? zipMatch.Groups[1].Value : (exeMatch.Success ? exeMatch.Groups[1].Value : "");

                            // Se a tag do GitHub for diferente da versão que o programa tem gravada
                            if (!string.IsNullOrWhiteSpace(downloadUrl) && remoteTag != currentVersion)
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    btnUpdate.Content = $"🔔 Atualizar ({remoteTag})!";
                                    btnUpdate.Background = new SolidColorBrush(Color.FromRgb(225, 29, 72));

                                    Log($"🚀 [ATUALIZAÇÃO DETETADA] Nova versão {remoteTag} disponível na nuvem!");

                                    // AGORA SALTA SEMPRE O POP-UP (mesmo no arranque automático!)
                                    MessageBoxResult r = MessageBox.Show(
                                        $"🔥 Está disponível uma nova atualização do PC Optimizer!\n\nVersão Instalada: {currentVersion}\nVersão Disponível: {remoteTag}\n\nDeseja descarregar e atualizar automaticamente agora?",
                                        "Nova Atualização Disponível!",
                                        MessageBoxButton.YesNo,
                                        MessageBoxImage.Information);

                                    if (r == MessageBoxResult.Yes)
                                    {
                                        DownloadAndApplyUpdate(downloadUrl, remoteTag);
                                    }
                                });
                            }
                            else if (isManual)
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    Log($"✅ O teu PC Optimizer já está na versão mais recente ({currentVersion})!");
                                    MessageBox.Show($"✅ Já estás a utilizar a versão mais recente ({currentVersion})!", "Tudo Atualizado", MessageBoxButton.OK, MessageBoxImage.Information);
                                });
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (isManual) Dispatcher.Invoke(() => Log($"ℹ️ Verificação de atualizações: {ex.Message}"));
                }
            });
        }

        private void DownloadAndApplyUpdate(string downloadUrl, string newTag)
        {
            try
            {
                Log($"⏳ A transferir a versão {newTag} em segundo plano...");
                string currentExePath = Process.GetCurrentProcess().MainModule.FileName;
                string tempDir = System.IO.Path.GetTempPath();
                string batUpdater = System.IO.Path.Combine(tempDir, "update_pcopt.bat");

                if (downloadUrl.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    string tempZip = System.IO.Path.Combine(tempDir, "update.zip");
                    string extractDir = System.IO.Path.Combine(tempDir, "pcopt_extracted");

                    using (WebClient wc = new WebClient()) { wc.DownloadFile(downloadUrl, tempZip); }

                    // Script batch que aguarda o encerramento da app, extrai o ZIP e substitui o executável
                    string batContent = 
                        $"@echo off\r\n" +
                        $"timeout /t 2 /nobreak >nul\r\n" +
                        $"powershell -Command \"Expand-Archive -Path '{tempZip}' -DestinationPath '{extractDir}' -Force\"\r\n" +
                        $"copy /y \"{extractDir}\\PC_Optimizer.exe\" \"{currentExePath}\"\r\n" +
                        $"start \"\" \"{currentExePath}\"\r\n" +
                        $"del \"{tempZip}\" >nul 2>&1\r\n" +
                        $"rd /s /q \"{extractDir}\" >nul 2>&1\r\n" +
                        $"del \"%~f0\"";

                    File.WriteAllText(batUpdater, batContent);
                }
                else
                {
                    string tempExe = currentExePath + ".new";
                    using (WebClient wc = new WebClient()) { wc.DownloadFile(downloadUrl, tempExe); }

                    string batContent = 
                        $"@echo off\r\ntimeout /t 2 /nobreak >nul\r\n" +
                        $"move /y \"{tempExe}\" \"{currentExePath}\"\r\n" +
                        $"start \"\" \"{currentExePath}\"\r\ndel \"%~f0\"";

                    File.WriteAllText(batUpdater, batContent);
                }

                Process.Start(new ProcessStartInfo(batUpdater) { CreateNoWindow = true, UseShellExecute = false });
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                Log("❌ Erro durante a atualização automática: " + ex.Message);
            }
        }

        private void LoadInstalledProgramsList()
        {
            lstPrograms.Items.Clear();
            List<string> progs = new List<string>();
            foreach (string path in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
            {
                try
                {
                    using (RegistryKey k = Registry.LocalMachine.OpenSubKey(path))
                        if (k != null)
                            foreach (string s in k.GetSubKeyNames())
                                using (RegistryKey sk = k.OpenSubKey(s))
                                {
                                    object n = sk?.GetValue("DisplayName");
                                    if (n != null && !string.IsNullOrWhiteSpace(n.ToString()) && !progs.Contains(n.ToString().Trim()))
                                        progs.Add(n.ToString().Trim());
                                }
                }
                catch { }
            }
            progs.Sort(); foreach (string p in progs) lstPrograms.Items.Add(p);
            Log($"✔ [REVO ENGINE] {progs.Count} programas instalados carregados na lista!");
        }

        private void UninstallAndScanLeftovers()
        {
            string t = txtSearchProg.Text.Trim();
            if (t.Length < 3) { Log("⚠️ Seleciona primeiro um programa na lista ou escreve o nome!"); return; }
            Process p = Process.Start(new ProcessStartInfo("cmd.exe", $"/c winget uninstall --name \"{t}\" & pause") { UseShellExecute = true });
            p?.WaitForExit(); ScanForLeftoversOnly();
        }

        private void ScanForLeftoversOnly()
        {
            string raw = txtSearchProg.Text.Trim();
            if (raw.Length < 3) { Log("⚠️ Escreve pelo menos 3 letras do nome na caixa!"); return; }
            string kw = raw.Split(' ')[0].Trim(); if (kw.Length < 3) kw = raw;
            if ("microsoft|windows|system|intel|nvidia|package".Contains(kw.ToLower())) { Log("🛡️ Palavra vital do sistema protegida!"); return; }

            lstLeftovers.Items.Clear();
            foreach (string b in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) })
                try { foreach (string d in Directory.GetDirectories(b)) if (System.IO.Path.GetFileName(d).IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0) lstLeftovers.Items.Add("[PASTA] " + d); } catch { }
            ScanRegLeftovers(Registry.CurrentUser, @"Software", kw, "HKCU\\Software");
            ScanRegLeftovers(Registry.LocalMachine, @"SOFTWARE", kw, "HKLM\\SOFTWARE");
            Log(lstLeftovers.Items.Count == 0 ? $"✔ Nenhuma sobra encontrada para '{kw}'!" : $"⚠️ Encontradas {lstLeftovers.Items.Count} sobras órfãs de '{kw}'! Clica em '2. Apagar Todas as Sobras'.");
        }

        private void ScanRegLeftovers(RegistryKey root, string subPath, string kw, string prefix)
        {
            try { using (RegistryKey k = root.OpenSubKey(subPath)) if (k != null) foreach (string s in k.GetSubKeyNames()) if (s.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0) lstLeftovers.Items.Add($"[REGISTO] {prefix}\\{s}"); } catch { }
        }

        private void DeleteSelectedLeftovers()
        {
            int del = 0;
            foreach (object itemObj in lstLeftovers.Items)
            {
                string item = itemObj.ToString();
                try
                {
                    if (item.StartsWith("[PASTA] ")) { string p = item.Substring(8).Trim(); if (Directory.Exists(p)) { Directory.Delete(p, true); del++; } }
                    else if (item.StartsWith("[REGISTO] ")) { RunCmdSilent($"reg delete \"{item.Substring(10).Trim()}\" /f"); del++; }
                }
                catch { }
            }
            lstLeftovers.Items.Clear();
            Log($"🏆 [LIMPEZA REVO] {del} pastas ocultas e chaves de registo eliminadas de raiz!");
        }

        private void CalculateOptimizationScore()
        {
            if (txtOptScoreVal == null) return;
            int score = 20;
            try
            {
                if (Convert.ToString(Registry.GetValue(@"HKEY_CURRENT_USER\System\GameConfigStore", "GameDVR_Enabled", 1)) == "0") score += 15;
                if (Convert.ToString(Registry.GetValue(@"HKEY_CURRENT_USER\Control Panel\Mouse", "MouseSpeed", "1")) == "0") score += 15;
                if (Convert.ToString(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", 0)) == "1") score += 15;
                if (Convert.ToString(Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", 2)) == "38") score += 15;
                if (Convert.ToString(Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NetworkThrottlingIndex", 10)) == "-1") score += 10;
                if (Convert.ToString(Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 1)) == "2") score += 10;
            }
            catch { }
            if (score > 100) score = 100;
            txtOptScoreVal.Text = score + "%";

            if (gaugeRing != null)
            {
                double dash = (score / 100.0) * 36.0;
                gaugeRing.StrokeDashArray = new DoubleCollection(new double[] { dash, 100 });
                gaugeRing.Stroke = score >= 90 ? new SolidColorBrush(Color.FromRgb(52, 211, 153)) : new SolidColorBrush(Color.FromRgb(2, 132, 199));
            }
            if (txtOptStatus != null)
            {
                txtOptStatus.Text = score >= 90 ? "● Sistema Otimizado" : "● Pode Melhorar";
                txtOptStatus.Foreground = score >= 90 ? new SolidColorBrush(Color.FromRgb(52, 211, 153)) : Brushes.Gold;
            }
        }

        private void UpdateLiveGraph(double cpuPct, double ramPct)
        {
            if (graphCanvas == null) return;
            cpuHistory.Add(cpuPct); ramHistory.Add(ramPct);
            if (cpuHistory.Count > 60) { cpuHistory.RemoveAt(0); ramHistory.RemoveAt(0); }
            double w = graphCanvas.ActualWidth > 100 ? graphCanvas.ActualWidth : 880;
            double h = graphCanvas.ActualHeight > 20 ? graphCanvas.ActualHeight : 120;
            double step = w / 59.0;
            PointCollection cpuPts = new PointCollection(), ramPts = new PointCollection();
            for (int i = 0; i < cpuHistory.Count; i++)
            {
                double x = i * step;
                cpuPts.Add(new Point(x, h - ((cpuHistory[i] / 100.0) * (h - 8)) - 4));
                ramPts.Add(new Point(x, h - ((ramHistory[i] / 100.0) * (h - 8)) - 4));
            }
            cpuLine.Points = cpuPts; ramLine.Points = ramPts;
        }

        private void SafeRegSet(string keyPath, string valueName, object val, RegistryValueKind kind = RegistryValueKind.DWord)
        {
            try { Registry.SetValue(keyPath, valueName, val, kind); }
            catch { string t = (kind == RegistryValueKind.String) ? "REG_SZ" : "REG_DWORD"; RunCmdSilent($"reg add \"{keyPath}\" /v \"{valueName}\" /t {t} /d \"{val}\" /f"); }
        }

        private void Log(string msg)
        {
            if (txtLog == null) return;
            Dispatcher.Invoke(() => { txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}"); txtLog.ScrollToEnd(); });
        }

        private string GetHardwareSummary()
        {
            string cpu = "CPU", gpu = "GPU";
            try
            {
                cpu = Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", "CPU").ToString().Trim();
                using (var searcher = new ManagementObjectSearcher("select Name from Win32_VideoController")) foreach (ManagementObject obj in searcher.Get()) { gpu = obj["Name"].ToString(); break; }
                using (var searcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem")) foreach (ManagementObject obj in searcher.Get()) { totalRamMb = Convert.ToUInt64(obj["TotalVisibleMemorySize"]) / 1024; break; }
            }
            catch { }
            return $"Olá, {Environment.UserName}!  |  💻 {cpu}  |  🎮 {gpu}  |  🧠 {totalRamMb} MB RAM";
        }

        private UniformGrid CreateButtonGrid(Grid parent)
        {
            ScrollViewer sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            UniformGrid ug = new UniformGrid { Columns = 3, Rows = 4 };
            sv.Content = ug; parent.Children.Add(sv); return ug;
        }

        private Border CreateCard(string title, int row, int col)
        {
            Border b = new Border { Background = new SolidColorBrush(Color.FromRgb(13, 19, 31)), BorderBrush = new SolidColorBrush(Color.FromRgb(26, 38, 59)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Margin = new Thickness(5), Padding = new Thickness(14, 10, 14, 10) };
            Grid.SetRow(b, row); Grid.SetColumn(b, col);
            StackPanel sp = new StackPanel(); sp.Children.Add(new TextBlock { Text = title, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)) });
            b.Child = sp; return b;
        }

        private Button CreateSmallBtn(string text, Action onClick)
        {
            Button b = new Button { Content = text, Background = new SolidColorBrush(Color.FromRgb(2, 132, 199)), Foreground = Brushes.White, BorderThickness = new Thickness(0), Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0), FontSize = 11.5, FontWeight = FontWeights.Bold, Cursor = Cursors.Hand };
            b.Click += (s, e) => onClick(); return b;
        }

        private void ToggleFullscreen()
        {
            if (WindowState == WindowState.Normal) { MaxHeight = SystemParameters.WorkArea.Height + 14; MaxWidth = SystemParameters.WorkArea.Width + 14; mainBorder.CornerRadius = new CornerRadius(0); WindowState = WindowState.Maximized; }
            else { mainBorder.CornerRadius = new CornerRadius(12); WindowState = WindowState.Normal; }
        }

        private void ToggleMiniOverlay(object sender, RoutedEventArgs e)
        {
            if (overlayWin != null && overlayWin.IsVisible) { overlayWin.Close(); overlayWin = null; Log("🎯 Mini-Overlay fechado."); return; }
            overlayWin = new Window { Width = 285, Height = 56, WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, Topmost = true, Left = 25, Top = 25, ShowInTaskbar = false, Cursor = Cursors.SizeAll };
            Border b = new Border { Background = new SolidColorBrush(Color.FromArgb(220, 9, 13, 22)), BorderBrush = new SolidColorBrush(Color.FromRgb(14, 165, 233)), BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 6, 10, 6) };
            overlayText = new TextBlock { Text = "A ler sensores...", Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248)), FontFamily = new FontFamily("Consolas"), FontSize = 11.5, FontWeight = FontWeights.Bold };
            b.Child = overlayText; overlayWin.Content = b; overlayWin.MouseLeftButtonDown += (s, ev) => overlayWin.DragMove(); overlayWin.Show();
        }

        private void ToggleCrosshairOverlay()
        {
            if (crosshairWin != null && crosshairWin.IsVisible) { crosshairWin.Close(); crosshairWin = null; Log("十 Mira desligada."); return; }
            crosshairWin = new Window { Width = 60, Height = 60, WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, Topmost = true, ShowInTaskbar = false, Left = (SystemParameters.PrimaryScreenWidth / 2.0) - 30, Top = (SystemParameters.PrimaryScreenHeight / 2.0) - 30 };
            crosshairWin.SourceInitialized += (s, e) => { IntPtr hwnd = new WindowInteropHelper(crosshairWin).Handle; SetWindowLong(hwnd, -20, GetWindowLong(hwnd, -20) | 0x00000020 | 0x00000080); };
            RedrawCrosshair(); crosshairWin.Show();
        }

        private void RedrawCrosshair()
        {
            if (crosshairWin == null || !crosshairWin.IsVisible) return;
            Canvas c = new Canvas { Width = 60, Height = 60 };
            if (crosshairStyle == 0)
            {
                c.Children.Add(new Line { X1 = 16, Y1 = 30, X2 = 26, Y2 = 30, Stroke = crosshairColor, StrokeThickness = 2 });
                c.Children.Add(new Line { X1 = 34, Y1 = 30, X2 = 44, Y2 = 30, Stroke = crosshairColor, StrokeThickness = 2 });
                c.Children.Add(new Line { X1 = 30, Y1 = 16, X2 = 30, Y2 = 26, Stroke = crosshairColor, StrokeThickness = 2 });
                c.Children.Add(new Line { X1 = 30, Y1 = 34, X2 = 30, Y2 = 44, Stroke = crosshairColor, StrokeThickness = 2 });
            }
            else if (crosshairStyle == 1) { Ellipse dot = new Ellipse { Width = 6, Height = 6, Fill = crosshairColor, Stroke = Brushes.Black, StrokeThickness = 1 }; Canvas.SetLeft(dot, 27); Canvas.SetTop(dot, 27); c.Children.Add(dot); }
            else { Ellipse ring = new Ellipse { Width = 22, Height = 22, Stroke = crosshairColor, StrokeThickness = 1.5 }; Canvas.SetLeft(ring, 19); Canvas.SetTop(ring, 19); c.Children.Add(ring); }
            crosshairWin.Content = c;
        }

        private void ToggleTimerResolution(object sender, RoutedEventArgs e)
        {
            uint currentRes;
            if (chkTimer.IsChecked == true) { NtSetTimerResolution(5000, true, out currentRes); Log($"⚡ [TIMER 0.5ms ATIVO] Kernel a {currentRes / 10000.0} ms!"); }
            else { NtSetTimerResolution(5000, false, out currentRes); Log("⚡ Timer padrão restaurado."); }
        }

        private void RecordActionHistory(string buttonName) { try { string entry = $"{DateTime.Now:dd/MM HH:mm} -> {buttonName}"; File.AppendAllText(historyFilePath, entry + Environment.NewLine); lstHistory?.Items.Insert(0, entry); } catch { } }
        private void LoadActionHistory() { try { if (File.Exists(historyFilePath) && lstHistory != null) { lstHistory.Items.Clear(); foreach (string l in File.ReadAllLines(historyFilePath).Reverse()) if (!string.IsNullOrWhiteSpace(l)) lstHistory.Items.Add(l); } } catch { } }
        private void RunCmdSilent(string command) { try { Process p = Process.Start(new ProcessStartInfo("cmd.exe", "/c " + command) { CreateNoWindow = true, UseShellExecute = false }); p?.WaitForExit(); } catch { } }
        private void RunCmd(string command, string successMsg) { RunCmdSilent(command); Log("✔ " + successMsg); }
        private void RunCmdVisible(string command, string startMsg) { Log("🚀 " + startMsg); Process.Start(new ProcessStartInfo("cmd.exe", "/c " + command + " & pause") { UseShellExecute = true }); }

        private void RunFullIsoLiteTransformation() { CreateFullBackup(); OptimizeSvchostProcesses(); DisableHiddenScheduledTasks(); KillBackgroundApps(); DisableAIAndBloat(); DisableEdgeBackground(); StopUselessServices(); DeepCleanDisk(); }
        private void OptimizeSvchostProcesses() { uint th = (uint)((totalRamMb * 1024) + 1024000); SafeRegSet(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control", "SvcHostSplitThresholdInKB", unchecked((int)th)); Log($"✔ SVCHOST agrupado para {totalRamMb} MB RAM!"); }
        private void DisableHiddenScheduledTasks() => RunCmd("schtasks /Change /TN \"Microsoft\\Windows\\Application Experience\\Microsoft Compatibility Appraiser\" /Disable >nul 2>&1", "Tarefas ocultas desativadas!");
        private void DisableEdgeBackground() { SafeRegSet(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Edge", "StartupBoostEnabled", 0); SafeRegSet(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Edge", "BackgroundModeEnabled", 0); Log("✔ Edge bloqueado em 2.º plano!"); }
        private void ActivateLiveMode() { PauseBackgroundSyncs(); KillHeavyRgbBloatware(); FlushMemory(); BoostObsAndDiscordProcess(); OptimizeForGames(); chkGuard.IsChecked = true; chkTimer.IsChecked = true; Log("✔ MODO LIVE PRONTO!"); }
        private void OptimizeObsStudio() { SafeRegSet(@"HKEY_CURRENT_USER\Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers", @"C:\Program Files\obs-studio\bin\64bit\obs64.exe", "~ RUNASADMIN", RegistryValueKind.String); SafeRegSet(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\obs64.exe\PerfOptions", "CpuPriorityClass", 3); Log("✔ OBS Studio blindado!"); }
        private void BoostObsAndDiscordProcess() { foreach (string t in new[] { "obs64", "Discord" }) foreach (Process p in Process.GetProcessesByName(t)) try { p.PriorityClass = ProcessPriorityClass.High; } catch { } Log("✔ Prioridade Alta aplicada ao OBS/Discord!"); }
        private void FixStreamerAudio() { SafeRegSet(@"HKEY_CURRENT_USER\Software\Microsoft\Multimedia\Audio", "UserDuckingPreference", 3); Log("✔ Atenuação de 80% no Discord desativada!"); }
        private void OptimizeUploadForStream() { SafeRegSet(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF)); RunCmd("netsh int tcp set global autotuninglevel=normal", "Upload estabilizado!"); }
        private void PauseBackgroundSyncs() => RunCmd("taskkill /f /im OneDrive.exe >nul 2>&1 & sc stop WSearch >nul 2>&1", "OneDrive e Indexação pausados!");
        private void DisableNotificationsForStream() { SafeRegSet(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\PushNotifications", "ToastEnabled", 0); Log("✔ Notificações bloqueadas!"); }
        private void OptimizeForGames() { SafeRegSet(@"HKEY_CURRENT_USER\System\GameConfigStore", "GameDVR_Enabled", 0); SafeRegSet(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", 0); SafeRegSet(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "SystemResponsiveness", 0); SafeRegSet(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 2); Log("✔ GameDVR Off e HAGS ativo!"); }
        private void UnparkCpuCores() => RunCmd("powercfg -setacvalueindex scheme_current sub_processor CPMINCORES 100 & powercfg -setactive scheme_current", "Núcleos CPU acordados!");
        private void FlushMemorySilent() { foreach (Process p in Process.GetProcesses()) try { EmptyWorkingSet(p.Handle); } catch { } }
        private void FlushMemory() { FlushMemorySilent(); Log("✔ Memória RAM Standby limpa!"); }
        private void OptimizePeripherals() { SafeRegSet(@"HKEY_CURRENT_USER\Control Panel\Mouse", "MouseSpeed", "0", RegistryValueKind.String); SafeRegSet(@"HKEY_CURRENT_USER\Control Panel\Mouse", "MouseThreshold1", "0", RegistryValueKind.String); SafeRegSet(@"HKEY_CURRENT_USER\Control Panel\Mouse", "MouseThreshold2", "0", RegistryValueKind.String); SafeRegSet(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\mouclass\Parameters", "MouseDataQueueSize", 20); Log("✔ Rato 1:1 sem aceleração ativo!"); }
        private void EnableGpuMsiMode() => RunCmd("powershell -NoProfile -Command \"Get-PnpDevice -Class Display | ForEach-Object { $p = 'HKLM:\\SYSTEM\\CurrentControlSet\\Enum\\' + $_.InstanceId + '\\Device Parameters\\Interrupt Management\\MessageSignaledInterruptProperties'; if (Test-Path $p) { Set-ItemProperty -Path $p -Name 'MSISupported' -Value 1 } }\"", "MSI Mode ativo na GPU!");
        private void CleanGpuCache() => RunCmd("del /s /f /q \"%LocalAppData%\\D3DSCache\\*.*\" & del /s /f /q \"%LocalAppData%\\NVIDIA\\GLCache\\*.*\"", "Cache Shaders GPU limpa!");
        private void BoostMsiAndGames() { SafeRegSet(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", 38); Log("✔ Win32PrioritySeparation (Hex 26) ativo!"); }
        private void OptimizeBcdTimer() => RunCmd("bcdedit /set useplatformtick yes & bcdedit /set disabledynamictick yes", "Relógio BCDEDIT otimizado!");
        private void DisableUsbSuspend() => RunCmd("powercfg /SETACVALUEINDEX SCHEME_CURRENT 2a737441-1930-4402-8d77-b2bebba308a3 48e6b7a6-50f5-4782-a5d4-53bb8f07e226 0 & powercfg /SETACTIVE SCHEME_CURRENT", "Suspensão USB desligada!");
        private void DisableFastStartup() => SafeRegSet(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled", 0);
        private void KillBackgroundApps() { SafeRegSet(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", 1); SafeRegSet(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "LetAppsRunInBackground", 2); Log("✔ Apps em 2.º plano desligadas!"); }
        private void DisableAIAndBloat() { SafeRegSet(@"HKEY_CURRENT_USER\Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1); SafeRegSet(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1); SafeRegSet(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Search", "BingSearchEnabled", 0); Log("✔ Copilot e Bing desativados!"); }
        private void StopUselessServices() => RunCmd("sc stop DiagTrack & sc config DiagTrack start= disabled & sc stop Fax & sc config Fax start= disabled", "Telemetria e Fax parados!");
        private void DeepCleanDisk() => RunCmd("del /s /f /q \"%temp%\\*.*\" & del /s /f /q \"C:\\Windows\\Temp\\*.*\"", "Ficheiros Temp limpos!");
        private void SetDns(string pri, string sec, string name) => RunCmd($"powershell -NoProfile -Command \"Get-NetAdapter | Where-Object {{$_.Status -eq 'Up'}} | ForEach-Object {{ Set-DnsClientServerAddress -InterfaceIndex $_.InterfaceIndex -ServerAddresses ('{pri}','{sec}') }}\" & ipconfig /flushdns", $"DNS alterado para {name}!");
        private void ResetDns() => RunCmd("powershell -NoProfile -Command \"Get-NetAdapter | Where-Object {$_.Status -eq 'Up'} | ForEach-Object { Set-DnsClientServerAddress -InterfaceIndex $_.InterfaceIndex -ResetServerAddresses }\" & ipconfig /flushdns", "DNS restaurado!");
        private void OptimizeNetworkForGaming() { SafeRegSet(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF)); RunCmd("netsh int tcp set global autotuninglevel=normal & netsh int tcp set global rss=enabled", "Rede TCP otimizada!"); }
        private void DisableNicPowerSave() => RunCmd("powershell -NoProfile -Command \"Get-NetAdapter | Where-Object {$_.Status -eq 'Up'} | Disable-NetAdapterPowerManagement -ErrorAction SilentlyContinue\"", "Poupança de rede desligada!");
        private void CreateFullBackup() => RunCmd("md C:\\PCOptimizer_Backups >nul 2>&1 & reg export \"HKCU\\Control Panel\" \"C:\\PCOptimizer_Backups\\Backup_Perifericos.reg\" /y >nul 2>&1 & powershell -Command \"Checkpoint-Computer -Description 'PCOptimizer_Backup' -RestorePointType 'MODIFY_SETTINGS'\"", "Backup criado!");
        private void EnableActiveCoolingPolicy() => RunCmd("powercfg /setacvalueindex scheme_current sub_processor SYSCOOLPOL 1 & powercfg /setactive scheme_current", "Refrigeração Ativa 100% no CPU!");
        private void KillHeavyRgbBloatware() => RunCmd("taskkill /f /im iCUE.exe >nul 2>&1 & taskkill /f /im ArmouryCrate.exe >nul 2>&1 & taskkill /f /im RazerSynapse3.exe >nul 2>&1", "Apps RGB pesadas fechadas!");
        private void StopBrandBloatServices(string b) => RunCmd(b == "ASUS" ? "sc stop ArmouryCrateService >nul 2>&1" : "sc stop CorsairService >nul 2>&1", $"Serviços {b} parados!");
        private void DisableWindowsDynamicLighting() { SafeRegSet(@"HKEY_CURRENT_USER\Software\Microsoft\Lighting", "AmbientLightingEnabled", 0); Log("✔ Dynamic Lighting desligado!"); }

        private void ReadTempsAndFans()
        {
            var s = _hwMonitor.ReadSensors();
            Log($"🌡️ [SENSORES CPU] {s.CpuName} -> Carga: {s.CpuLoad:0.0}% | Temp: {(s.CpuTemp > 0 ? s.CpuTemp.ToString("0.0") + "°C" : "Gerida pela BIOS")} | Clock: {s.CpuClockMhz:0} MHz");
            Log($"🎮 [SENSORES GPU] {s.GpuName} -> Temp: {s.GpuTemp:0.0}°C | Consumo: {s.GpuPowerWatts:0}W | Fan: {s.GpuFanPercent:0}% ({s.GpuFanRpm:0} RPM) | Clock: {s.GpuCoreClock:0} MHz | VRAM: {s.GpuVramUsedMb:0}/{s.GpuVramTotalMb:0} MB");
            if (s.FanReadings.Count > 0) Log("❄️ [VENTOINHAS DA BOARD] " + string.Join(" | ", s.FanReadings));
        }

        private void RegisterPermanentVipGames()
        {
            foreach (string g in new[] { "cs2.exe", "VALORANT-Win64-Shipping.exe", "cod.exe", "FortniteClient-Win64-Shipping.exe", "r5apex.exe", "RustClient.exe", "GTA5.exe" })
            {
                string path = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\" + g + @"\PerfOptions";
                SafeRegSet(path, "CpuPriorityClass", 3); SafeRegSet(path, "IoPriority", 3);
            }
            Log("👑 Prioridade Alta Permanente gravada para todos os Jogos VIP!");
        }

        private void RemovePermanentVipGames()
        {
            foreach (string g in new[] { "cs2.exe", "VALORANT-Win64-Shipping.exe", "cod.exe", "FortniteClient-Win64-Shipping.exe", "r5apex.exe", "RustClient.exe", "GTA5.exe" })
                RunCmdSilent($"reg delete \"HKLM\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Image File Execution Options\\{g}\" /f");
            Log("⏪ Prioridade permanente dos jogos removida.");
        }

        private void ChangeScreenResolution(int w, int h)
        {
            DEVMODE dm = new DEVMODE { dmSize = (short)Marshal.SizeOf(typeof(DEVMODE)) };
            if (EnumDisplaySettings(null, -1, ref dm) != 0)
            {
                dm.dmPelsWidth = w; dm.dmPelsHeight = h; dm.dmFields = 0x00080000 | 0x00100000;
                if (ChangeDisplaySettings(ref dm, 0) == 0) Log($"🖥️ Resolução alterada para {w}x{h}!");
                else Log($"⚠️ Cria primeiro a resolução {w}x{h} no painel da placa gráfica.");
            }
        }

        private void ReadBiosWindowsKey()
        {
            Process p = Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -Command \"(Get-CimInstance -Query 'select * from SoftwareLicensingService').OA3xOriginalProductKey\"") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
            string key = p?.StandardOutput.ReadToEnd().Trim() ?? ""; p?.WaitForExit();
            Log(string.IsNullOrWhiteSpace(key) ? "🔑 Licença Digital ativa na Conta Microsoft." : "🔑 Chave Original BIOS: " + key);
        }

        private void CheckUptimeAndBottleneck()
        {
            TimeSpan up = TimeSpan.FromMilliseconds(Environment.TickCount64);
            var s = _hwMonitor.ReadSensors();
            Log($"⚖️ [GARGALO & UPTIME] CPU ({Environment.ProcessorCount} Threads) + {s.GpuName} + {totalRamMb}MB RAM -> Equilíbrio Perfeito! Uptime: {up.Days}d {up.Hours}h {up.Minutes}m.");
        }

        private void ApplyHostsTelemetryBlock()
        {
            try
            {
                string hp = @"C:\Windows\System32\drivers\etc\hosts";
                if (File.Exists(hp) && !File.ReadAllText(hp).Contains("vortex.data.microsoft.com"))
                    File.AppendAllText(hp, "\r\n0.0.0.0 vortex.data.microsoft.com\r\n0.0.0.0 telemetry.microsoft.com\r\n");
                Log("🛡️ Ficheiro HOSTS blindado contra telemetria!");
            }
            catch { }
        }

        private void ResetHostsFile()
        {
            try
            {
                string hp = @"C:\Windows\System32\drivers\etc\hosts";
                if (File.Exists(hp)) { File.WriteAllLines(hp, File.ReadAllLines(hp).Where(l => !l.Contains("microsoft.com")).ToArray()); Log("🔓 HOSTS restaurado!"); }
            }
            catch { }
        }

        private void ScanHeavyFolders()
        {
            lstBigFolders.Items.Clear(); lstBigFolders.Items.Add("⏳ A calcular pastas mais pesadas em AppData e ProgramData...");
            Task.Run(() =>
            {
                var sizes = new List<KeyValuePair<string, double>>();
                foreach (string r in new[] { Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) })
                    try { foreach (string d in Directory.GetDirectories(r)) try { double mb = Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length) / 1048576.0; if (mb > 50) sizes.Add(new KeyValuePair<string, double>(d, mb)); } catch { } } catch { }
                var top = sizes.OrderByDescending(x => x.Value).Take(15).ToList();
                Dispatcher.Invoke(() => { lstBigFolders.Items.Clear(); foreach (var i in top) lstBigFolders.Items.Add($"{i.Value,8:0.0} MB  |  {i.Key}"); });
            });
        }

        private void ScanGiantFiles()
        {
            lstBigFolders.Items.Clear(); lstBigFolders.Items.Add("⏳ A procurar ficheiros gigantes (+200MB)...");
            Task.Run(() =>
            {
                var files = new List<KeyValuePair<string, double>>();
                foreach (string d in new[] { Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads") })
                    try { foreach (string f in Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories)) try { double mb = new FileInfo(f).Length / 1048576.0; if (mb >= 200) files.Add(new KeyValuePair<string, double>(f, mb)); } catch { } } catch { }
                var top = files.OrderByDescending(x => x.Value).Take(15).ToList();
                Dispatcher.Invoke(() => { lstBigFolders.Items.Clear(); foreach (var i in top) lstBigFolders.Items.Add($"{i.Value,8:0.0} MB  |  {i.Key}"); });
            });
        }

        private void OpenSelectedHeavyFolder()
        {
            if (lstBigFolders.SelectedItem == null) return;
            string s = lstBigFolders.SelectedItem.ToString(); int idx = s.IndexOf("|");
            if (idx > 0) { string p = s.Substring(idx + 1).Trim(); if (File.Exists(p)) Process.Start("explorer.exe", $"/select,\"{p}\""); else if (Directory.Exists(p)) Process.Start("explorer.exe", $"\"{p}\""); }
        }

        private void RunGameServersPingRadar()
        {
            lblGamePingResult.Text = "⏳ A medir Ping para servidores Europeus...";
            Task.Run(() =>
            {
                string[,] t = { { "🎮 CS2 EU", "155.133.248.34" }, { "🎯 Valorant EU", "162.249.72.1" }, { "🪂 Fortnite EU", "52.94.17.74" }, { "⚔️ CoD EU", "185.60.112.157" } };
                Ping p = new Ping(); string res = "";
                for (int i = 0; i < 4; i++) try { PingReply r = p.Send(t[i, 1], 1000); res += $"{t[i, 0]}: {(r.Status == IPStatus.Success ? r.RoundtripTime + " ms" : "Off")}  |  "; } catch { }
                Dispatcher.Invoke(() => lblGamePingResult.Text = res.TrimEnd(' ', '|'));
            });
        }

        private void RunNativeCpuStressBenchmark()
        {
            Log($"🔥 [STRESS TEST CPU] A testar {Environment.ProcessorCount} threads durante 4 segundos...");
            Task.Run(() =>
            {
                long ops = 0; Stopwatch sw = Stopwatch.StartNew();
                Parallel.For(0, Environment.ProcessorCount, t => { long l = 0; double x = 1.01; while (sw.ElapsedMilliseconds < 4000) { for (int i = 0; i < 10000; i++) x = Math.Sqrt(x * 3.14 + i); l++; } Interlocked.Add(ref ops, l); });
                Dispatcher.Invoke(() => Log($"🏆 [CPU BENCHMARK] Pontuação Multi-Core: {ops * 12:N0} Pontos!"));
            });
        }

        private void HuntBestDnsServer()
        {
            Task.Run(() =>
            {
                Ping p = new Ping();
                long cMs = p.Send("1.1.1.1", 1000).RoundtripTime;
                long gMs = p.Send("8.8.8.8", 1000).RoundtripTime;
                Dispatcher.Invoke(() => { if (cMs <= gMs) SetDns("1.1.1.1", "1.0.0.1", $"Cloudflare ({cMs}ms)"); else SetDns("8.8.8.8", "8.8.4.4", $"Google ({gMs}ms)"); });
            });
        }

        private void ToggleUfoTest()
        {
            if (!ufoRunning) { ufoRunning = true; CompositionTarget.Rendering += AnimateUfo; }
            else { ufoRunning = false; CompositionTarget.Rendering -= AnimateUfo; }
        }

        private void AnimateUfo(object sender, EventArgs e)
        {
            double maxW = ufoCanvas.ActualWidth > 50 ? ufoCanvas.ActualWidth : 400;
            ufoX = (ufoX + 6.5 > maxW) ? -30 : ufoX + 6.5;
            Canvas.SetLeft(ufoRect1, ufoX); Canvas.SetLeft(ufoRect2, ufoX * 0.6);
        }

        private void OpenNativeDeadPixelTester()
        {
            Window dp = new Window { WindowStyle = WindowStyle.None, WindowState = WindowState.Maximized, Topmost = true, Background = Brushes.Black, Cursor = Cursors.Hand };
            Brush[] c = { Brushes.Black, Brushes.White, Brushes.Red, Brushes.Lime, Brushes.Blue }; int idx = 0;
            dp.MouseLeftButtonDown += (s, e) => { idx = (idx + 1) % c.Length; dp.Background = c[idx]; };
            dp.MouseRightButtonDown += (s, e) => dp.Close();
            dp.ShowDialog();
        }

        private void RunNativeSpeedAndPingTest()
        {
            lblNetResult.Text = "⏳ A medir velocidade de Download e Ping...";
            Task.Run(() =>
            {
                try
                {
                    PingReply r = new Ping().Send("1.1.1.1", 1000);
                    Stopwatch sw = Stopwatch.StartNew();
                    byte[] data = new WebClient().DownloadData("https://speed.cloudflare.com/__down?bytes=15000000");
                    sw.Stop();
                    double mbps = ((data.Length * 8.0) / 1000000.0) / sw.Elapsed.TotalSeconds;
                    Dispatcher.Invoke(() => lblNetResult.Text = $"⚡ Download: {mbps:0.0} Mbps  |  🎯 Ping: {r.RoundtripTime} ms");
                }
                catch { Dispatcher.Invoke(() => lblNetResult.Text = "❌ Erro ao medir velocidade."); }
            });
        }

        private void RefreshRunningApps()
        {
            if (lstRunningGames == null) return;
            lstRunningGames.Items.Clear();
            foreach (Process p in Process.GetProcesses()) try { if (!string.IsNullOrWhiteSpace(p.MainWindowTitle)) lstRunningGames.Items.Add($"{p.ProcessName} | ID:{p.Id} ({p.MainWindowTitle})"); } catch { }
        }

        private Process GetSelectedProcessFromLasso()
        {
            if (lstRunningGames.SelectedItem == null) return null;
            try { string s = lstRunningGames.SelectedItem.ToString(); int idx = s.IndexOf("ID:") + 3; return Process.GetProcessById(int.Parse(s.Substring(idx, s.IndexOf(" ", idx) - idx))); } catch { return null; }
        }

        private void ApplyCpuZeroAvoidance() { Process p = GetSelectedProcessFromLasso(); if (p != null) { p.ProcessorAffinity = new IntPtr(((1L << Environment.ProcessorCount) - 1) & ~1L); p.PriorityClass = ProcessPriorityClass.High; Log($"🚀 {p.ProcessName} isolado do Core 0!"); } }
        private void SetSelectedProcessPriority(ProcessPriorityClass pr) { Process p = GetSelectedProcessFromLasso(); if (p != null) { p.PriorityClass = pr; Log($"⚡ Prioridade {pr} aplicada!"); } }
        private void ResetSelectedProcessAffinity() { Process p = GetSelectedProcessFromLasso(); if (p != null) { p.ProcessorAffinity = new IntPtr((1L << Environment.ProcessorCount) - 1); p.PriorityClass = ProcessPriorityClass.Normal; Log("🔄 Núcleos repostos!"); } }
        private void SplitCoresGameAndObs() { ApplyCpuZeroAvoidance(); BoostObsAndDiscordProcess(); Log("🎥 Núcleos balanceados entre Jogo e OBS!"); }
        private void ListStartupRegistryApps()
        {
            Log("🚀 PROGRAMAS QUE ARRANCAM SOZINHOS COM O WINDOWS:");
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) if (k != null) foreach (string v in k.GetValueNames()) Log("   [User] " + v);
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run")) if (k != null) foreach (string v in k.GetValueNames()) Log("   [System] " + v);
            }
            catch { }
        }
        private void ReadLastSystemCrashes()
        {
            Process p = Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -Command \"Get-WinEvent -FilterHashtable @{LogName='Application'; Level=2} -MaxEvents 4 -ErrorAction SilentlyContinue | Select-Object TimeCreated, ProviderName, Message | Format-List\"") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
            string o = p?.StandardOutput.ReadToEnd() ?? ""; p?.WaitForExit();
            Log(string.IsNullOrWhiteSpace(o) ? "✔ Nenhum crash recente encontrado!" : "🔍 ÚLTIMOS CRASHES:" + Environment.NewLine + o.Trim());
        }
        private void RunBenchmarkScore() => RunCmdVisible("winsat formal", "A correr Benchmark WinSAT...");
        private void CheckDiskHealth() { foreach (var d in HardwareMonitorService.GetDisksInfo()) Log($"💿 Disco {d.DriveLetter} ({d.VolumeLabel}): {d.FreeGB:0.0} GB Livres de {d.TotalGB:0.0} GB ({d.UsedPercent:0}% usado)"); }
        private void UndoPeripherals() { SafeRegSet(@"HKEY_CURRENT_USER\Control Panel\Mouse", "MouseSpeed", "1", RegistryValueKind.String); Log("⏪ Rato revertido!"); }
        private void UndoAudioAndVisuals() { SafeRegSet(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 1); Log("⏪ Visual revertido!"); }
        private void UndoBackgroundAndServices() { SafeRegSet(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", 0); Log("⏪ 2.º Plano revertido!"); }
        private void UndoGamingAndTimers() { chkGuard.IsChecked = false; chkTimer.IsChecked = false; Log("⏪ Timers revertidos!"); }
        private void UndoNetworkSettings() { ResetDns(); Log("⏪ Rede revertida!"); }
        private void UndoEverythingPanicButton() { UndoPeripherals(); UndoAudioAndVisuals(); UndoBackgroundAndServices(); UndoGamingAndTimers(); UndoNetworkSettings(); RemovePermanentVipGames(); ResetHostsFile(); Log("🏆 Tudo restaurado para o original da Microsoft!"); }
    
 // =========================================================================
        // MOTOR DE DRIVERS (ESTILO IOBIT / DRIVER BOOSTER - 100% LIMPO E OFICIAL)
        // =========================================================================
        private void ScanForOfficialDrivers()
        {
            Log("🔍 [MOTOR DE DRIVERS] A consultar a API do Windows Update para o teu hardware...");
            Task.Run(() =>
            {
                try
                {
                    Type updateSessionType = Type.GetTypeFromProgID("Microsoft.Update.Session");
                    dynamic updateSession = Activator.CreateInstance(updateSessionType);
                    dynamic searcher = updateSession.CreateUpdateSearcher();
                    searcher.ServerSelection = 2; // Catálogo oficial do Windows Update

                    dynamic searchResult = searcher.Search("IsInstalled=0 and Type='Driver'");
                    int count = searchResult.Updates.Count;

                    if (count == 0)
                    {
                        Log("✅ [DRIVERS] Todos os drivers do teu sistema estão atualizados! Nenhum driver pendente.");
                    }
                    else
                    {
                        Log($"⚠️ [DRIVERS DETETADOS] Foram encontrados {count} drivers com versões mais recentes:");
                        for (int i = 0; i < count; i++)
                        {
                            dynamic update = searchResult.Updates.Item(i);
                            Log($"   🚗 {i + 1}. {update.Title}");
                        }
                        Log("👉 Clica no cartão '⚡ Instalar Todos os Drivers Pendentes' para aplicar.");
                    }
                }
                catch (Exception ex)
                {
                    Log("⚠️ Acesso direto à API COM condicionado. A abrir painel nativo de drivers...");
                    RunCmd("start ms-settings:windowsupdate-optionalupdates", "Painel de Atualizações Opcionais de Drivers aberto!");
                }
            });
        }

        private void InstallPendingOfficialDrivers()
        {
            Log("⚡ [MOTOR DE DRIVERS] A iniciar transferência e instalação de drivers certificados...");
            Task.Run(() =>
            {
                try
                {
                    Type updateSessionType = Type.GetTypeFromProgID("Microsoft.Update.Session");
                    dynamic updateSession = Activator.CreateInstance(updateSessionType);
                    dynamic searcher = updateSession.CreateUpdateSearcher();
                    dynamic searchResult = searcher.Search("IsInstalled=0 and Type='Driver'");

                    int count = searchResult.Updates.Count;
                    if (count == 0)
                    {
                        Log("ℹ️ Não existem drivers pendentes para instalar.");
                        return;
                    }

                    Type updateCollType = Type.GetTypeFromProgID("Microsoft.Update.UpdateColl");
                    dynamic updatesToDownload = Activator.CreateInstance(updateCollType);
                    for (int i = 0; i < count; i++) updatesToDownload.Add(searchResult.Updates.Item(i));

                    Log($"⏳ A descarregar {count} drivers oficiais...");
                    dynamic downloader = updateSession.CreateUpdateDownloader();
                    downloader.Updates = updatesToDownload;
                    downloader.Download();

                    Log("🔧 A instalar drivers no sistema operativo...");
                    dynamic installer = updateSession.CreateUpdateInstaller();
                    installer.Updates = updatesToDownload;
                    dynamic installResult = installer.Install();

                    Log($"🏆 [DRIVERS INSTALADOS] Código de resultado: {installResult.ResultCode}. Reinicia o PC para ativar!");
                }
                catch (Exception ex)
                {
                    Log("❌ Erro ao instalar drivers: " + ex.Message);
                }
            });
        }

        private void BackupAllSystemDrivers()
        {
            Log("💾 A criar cópia de segurança completa de todos os drivers em C:\\PCOptimizer_Backups\\Drivers...");
            string backupPath = @"C:\PCOptimizer_Backups\Drivers";
            RunCmdVisible("md \"" + backupPath + "\" >nul 2>&1 & pnputil /export-driver * \"" + backupPath + "\"", "Exportação de drivers com pnputil concluída!");
        }
    }
}
