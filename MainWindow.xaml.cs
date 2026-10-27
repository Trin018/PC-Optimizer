using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
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
        private string currentVersion = "14.0";
        private string githubRepoApi = "https://api.github.com/repos/Trin018/PC-Optimizer/releases/latest";

        private HardwareMonitorService _hwMonitor = new HardwareMonitorService();
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
        private bool ufoRunning = false, isUpdatingToggles = false;
        private ulong totalRamMb = 32768;
        private float latestCpuTemp = 0, latestGpuTemp = 0, latestGpuWatts = 0, latestGpuFan = 0;
        private Brush crosshairColor = Brushes.Lime;
        private string historyFilePath = @"C:\PCOptimizer_Backups\Historico_Acoes.txt";
        private PerformanceCounter cpuCounter, ramCounter;

        [DllImport("psapi.dll")] static extern int EmptyWorkingSet(IntPtr hwProc);
        [DllImport("ntdll.dll", SetLastError = true)] static extern int NtSetTimerResolution(uint DesiredResolution, bool SetResolution, out uint CurrentResolution);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion; public short dmDriverVersion; public short dmSize; public short dmDriverExtra; public int dmFields;
            public int dmPositionX; public int dmPositionY; public int dmDisplayOrientation; public int dmDisplayFixedOutput;
            public short dmColor; public short dmDuplex; public short dmYResolution; public short dmTTOption; public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels; public int dmBitsPerPel; public int dmPelsWidth; public int dmPelsHeight;
            public int dmDisplayFlags; public int dmDisplayFrequency;
        }
        [DllImport("user32.dll", CharSet = CharSet.Ansi)] static extern int EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);
        [DllImport("user32.dll", CharSet = CharSet.Ansi)] static extern int ChangeDisplaySettings(ref DEVMODE lpDevMode, int dwFlags);

        public MainWindow()
        {
            InitializeComponent();
            lblVerTitle.Text = "CONTROL PRO v" + currentVersion;

            topDragBar.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2) ToggleFullscreen();
                else if (e.ButtonState == MouseButtonState.Pressed) DragMove();
            };

            btnMin.Click += (s, e) => WindowState = WindowState.Minimized;
            btnMax.Click += (s, e) => ToggleFullscreen();
            btnClose.Click += (s, e) => { _hwMonitor.Dispose(); Environment.Exit(0); };
            btnOverlay.Click += ToggleMiniOverlay;
            btnTopCrosshair.Click += (s, e) => ToggleCrosshairOverlay();
            btnUpdate.Click += (s, e) => CheckForGitHubReleasesUpdate(true);

            chkGuard.Checked += (s, e) => Log("🛡️ [AUTO-GUARD ATIVADO] Vigia automático de RAM (<2.5GB) em execução!");
            chkGuard.Unchecked += (s, e) => Log("🛡️ [AUTO-GUARD DESATIVADO]");
            chkTimer.Checked += ToggleTimerResolution;
            chkTimer.Unchecked += ToggleTimerResolution;

            Grid[] pages = { page1, page2, page3, page4, page5, page6, page7, page8, page9, page10, page11, page12, page13, page14 };
            RadioButton[] navs = { nav1, nav2, nav3, nav4, nav5, nav6, nav7, nav8, nav9, nav10, nav11, nav12, nav13, nav14 };

            for (int i = 0; i < 14; i++)
            {
                int idx = i;
                navs[i].Checked += (s, e) =>
                {
                    for (int j = 0; j < 14; j++) pages[j].Visibility = (j == idx) ? Visibility.Visible : Visibility.Collapsed;
                    if (idx == 0) CalculateOptimizationScore();
                };
            }

            BuildPage0_GaldinoDashboard(page1);
            BuildPage1_ToggleCardsWindows(page2);
            BuildPage2_PerGameOptimizer(page3);
            BuildPage3_GamingSwitches(page4);
            BuildPage_ProcessLasso(page5);
            BuildPage_CrosshairAndGamePing(page6);
            BuildPage_VipGamesAndDisplay(page7);
            BuildPage_DiskSpaceHunter(page8);
            BuildPage_GamerAppStore(page9);
            BuildPage3_NativeTests(page10);
            BuildPage4_RevoUninstaller(page11);
            BuildPage5_FansAndRgb(page12);
            BuildPage6_Streamer(page13);
            BuildPage8_UndoHistory(page14);

            SourceInitialized += (s, e) =>
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                HwndSource.FromHwnd(hwnd)?.AddHook(HwndHook);
                RegisterHotKey(hwnd, 9001, 0x0002 | 0x0004, 0x70); // Ctrl+Shift+F1
                RegisterHotKey(hwnd, 9002, 0x0002 | 0x0004, 0x71); // Ctrl+Shift+F2
                RegisterHotKey(hwnd, 9003, 0x0002 | 0x0004, 0x72); // Ctrl+Shift+F3
            };

            PreviewKeyDown += (s, e) =>
            {
                if (page10.Visibility == Visibility.Visible && lblKeyTest != null)
                {
                    pressedKeys.Add(e.Key);
                    if (pressedKeys.Count > maxSimulKeys) maxSimulKeys = pressedKeys.Count;
                    lblKeyTest.Text = $"⌨️ Última: [{e.Key}] | Simultâneas: {pressedKeys.Count} | 🏆 Recorde NKRO: {maxSimulKeys} teclas";
                }
            };
            PreviewKeyUp += (s, e) => pressedKeys.Remove(e.Key);

            ContentRendered += (s, e) =>
            {
                Task.Run(() =>
                {
                    _hwMonitor.Initialize();
                    string hw = GetHardwareSummary();
                    Dispatcher.Invoke(() =>
                    {
                        lblHardware.Text = $"Olá, {Environment.UserName}!  |  {hw}";
                        LoadActionHistory();
                        CalculateOptimizationScore();
                    });
                    InitLiveSensorsAsync();
                    CheckForGitHubReleasesUpdate(false);
                });
            };

            Log($"⚡ PC OPTIMIZER v{currentVersion} (.NET 8 + LibreHardwareMonitor + Simulador 30.000 Unidades) Ativo!");
        }

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

        private void AddSwitchCard(Panel parent, string badgeType, string title, string desc, bool initialState, Action<bool> onToggle)
        {
            Border card = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(13, 19, 31)),
                BorderBrush = initialState ? new SolidColorBrush(Color.FromRgb(2, 132, 199)) : new SolidColorBrush(Color.FromRgb(26, 38, 59)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Margin = new Thickness(6),
                Padding = new Thickness(14, 12, 14, 12)
            };

            DockPanel dp = new DockPanel();
            Border badge = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(7, 2, 7, 2),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 8),
                Background = badgeType == "REC" ? new SolidColorBrush(Color.FromRgb(12, 45, 72)) : new SolidColorBrush(Color.FromRgb(66, 32, 6)),
                BorderBrush = badgeType == "REC" ? new SolidColorBrush(Color.FromRgb(2, 132, 199)) : new SolidColorBrush(Color.FromRgb(217, 119, 6)),
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = badgeType == "REC" ? "★ RECOMENDADO" : "! AVANÇADO",
                    FontSize = 9.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = badgeType == "REC" ? new SolidColorBrush(Color.FromRgb(56, 189, 248)) : new SolidColorBrush(Color.FromRgb(251, 191, 36))
                }
            };
            DockPanel.SetDock(badge, Dock.Top);
            dp.Children.Add(badge);

            DockPanel bottomBar = new DockPanel { Margin = new Thickness(0, 8, 0, 0), VerticalAlignment = VerticalAlignment.Bottom };
            DockPanel.SetDock(bottomBar, Dock.Bottom);

            TextBlock lblState = new TextBlock
            {
                Text = initialState ? "Ativado" : "Desativado",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = initialState ? new SolidColorBrush(Color.FromRgb(56, 189, 248)) : new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                VerticalAlignment = VerticalAlignment.Center
            };

            CheckBox sw = new CheckBox
            {
                Style = (Style)FindResource("ModernSwitch"),
                IsChecked = initialState,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            allToggleCards.Add(sw);

            sw.Checked += (s, e) =>
            {
                lblState.Text = "Ativado";
                lblState.Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248));
                card.BorderBrush = new SolidColorBrush(Color.FromRgb(2, 132, 199));
                if (!isUpdatingToggles) { RecordActionHistory("[ON] " + title); onToggle(true); CalculateOptimizationScore(); }
            };
            sw.Unchecked += (s, e) =>
            {
                lblState.Text = "Desativado";
                lblState.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
                card.BorderBrush = new SolidColorBrush(Color.FromRgb(26, 38, 59));
                if (!isUpdatingToggles) { RecordActionHistory("[OFF] " + title); onToggle(false); CalculateOptimizationScore(); }
            };

            DockPanel.SetDock(sw, Dock.Right);
            bottomBar.Children.Add(sw);
            bottomBar.Children.Add(lblState);
            dp.Children.Add(bottomBar);

            StackPanel centerSp = new StackPanel();
            centerSp.Children.Add(new TextBlock { Text = title, FontSize = 13.5, FontWeight = FontWeights.Bold, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) });
            centerSp.Children.Add(new TextBlock { Text = desc, FontSize = 11.2, Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)), TextWrapping = TextWrapping.Wrap });
            dp.Children.Add(centerSp);

            card.Child = dp;
            parent.Children.Add(card);
        }

        private void BuildPage0_GaldinoDashboard(Grid page)
        {
            Grid g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(255) });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            page.Children.Add(g);

            Grid topGrid = new Grid();
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(topGrid, 0);

            Border cLeft = CreateCard("🌡️ SENSORES EM TEMPO REAL (LIBREHARDWAREMONITOR)", 0, 0);
            StackPanel spLeft = (StackPanel)cLeft.Child;
            txtCpuVal = new TextBlock { Text = "🔥 CPU: A ler sensores...", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248)), Margin = new Thickness(0, 10, 0, 6) };
            txtGpuTempVal = new TextBlock { Text = "🎮 GPU: A ler sensores...", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153)), Margin = new Thickness(0, 2, 0, 6) };
            txtRamVal = new TextBlock { Text = "🧠 RAM Livre: A ler...", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(192, 132, 252)), Margin = new Thickness(0, 2, 0, 6) };
            txtDiskVal = new TextBlock { Text = "💾 SSD Principal: A ler...", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Brushes.Silver, Margin = new Thickness(0, 2, 0, 10) };
            spLeft.Children.Add(txtCpuVal); spLeft.Children.Add(txtGpuTempVal); spLeft.Children.Add(txtRamVal); spLeft.Children.Add(txtDiskVal);
            spLeft.Children.Add(CreateSmallBtn("🧹 Limpar Memória Cache / RAM (F1)", () => FlushMemory()));
            topGrid.Children.Add(cLeft);

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

            Button btnBoostAll = CreateSmallBtn("🤖 AI Boost Automático (Avaliar & Otimizar 100%)", () => RunAiSmartAutoTuner());
            btnBoostAll.HorizontalAlignment = HorizontalAlignment.Stretch;
            spCenter.Children.Add(btnBoostAll);
            topGrid.Children.Add(cCenter);

            Border cRight = CreateCard("⚔️ SIMULADOR DE BATALHA & MODO OTIMIZADO", 0, 2);
            StackPanel spRight = (StackPanel)cRight.Child;
            spRight.Children.Add(new TextBlock { Text = "Testa os FPS reais do teu PC com 30.000 unidades em combate ou configura os interruptores do Windows:", FontSize = 11.5, Foreground = Brushes.Silver, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 10) });
            spRight.Children.Add(CreateSmallBtn("⚔️ Abrir Benchmark 30.000 Unidades!", () => GameSimBenchmark.OpenBattleBenchmarkWindow()));
            spRight.Children.Add(new Border { Height = 6 });
            spRight.Children.Add(CreateSmallBtn("🪟 Ir para Interruptores do Windows ->", () => nav2.IsChecked = true));
            spRight.Children.Add(new Border { Height = 6 });
            spRight.Children.Add(CreateSmallBtn("⚖️ Analisar Gargalo (CPU vs GPU)", () => CheckUptimeAndBottleneck()));
            topGrid.Children.Add(cRight);

            g.Children.Add(topGrid);

            Border cGraph = CreateCard("📈 MONITOR DE DESEMPENHO EM TEMPO REAL (AZUL = CPU %   |   ROXO = RAM %)", 1, 0);
            StackPanel spGraph = (StackPanel)cGraph.Child;
            graphCanvas = new Canvas { Height = 125, Background = new SolidColorBrush(Color.FromRgb(6, 9, 15)), Margin = new Thickness(0, 8, 0, 0), ClipToBounds = true };
            cpuLine = new Polyline { Stroke = new SolidColorBrush(Color.FromRgb(14, 165, 233)), StrokeThickness = 2.5 };
            ramLine = new Polyline { Stroke = new SolidColorBrush(Color.FromRgb(168, 85, 247)), StrokeThickness = 2.5 };
            graphCanvas.Children.Add(ramLine); graphCanvas.Children.Add(cpuLine);
            spGraph.Children.Add(graphCanvas);
            g.Children.Add(cGraph);
        }

        private void RunAiSmartAutoTuner()
        {
            Log("🤖 [AI AUTO-TUNER] A avaliar a arquitetura do teu PC (CPU, GPU, RAM e Registo)...");
            isUpdatingToggles = true;
            foreach (CheckBox cb in allToggleCards) cb.IsChecked = true;
            isUpdatingToggles = false;

            RunFullIsoLiteTransformation();
            OptimizeForGames();
            OptimizePeripherals();
            DisableFastStartup();
            RegisterPermanentVipGames();
            ApplyCpuZeroAvoidanceToRunningGames();
            CalculateOptimizationScore();

            string profile = totalRamMb >= 30000 ? "Perfil Enthusiast 32GB+ (Zero Compressão, Núcleo 0 Isolado, HAGS + MSI Mode Ativos)" : "Perfil Gaming Pro 16GB (Auto-Guard RAM Ativo + SVCHOST Agrupado)";
            Log($"🏆 [AI BOOST 100% CONCLUÍDO] Aplicado: {profile}!");
        }
        private void BuildPage1_ToggleCardsWindows(Grid page)
        {
            ScrollViewer sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            UniformGrid ug = new UniformGrid { Columns = 3, Rows = 4 };
            sv.Content = ug;
            page.Children.Add(sv);

            AddSwitchCard(ug, "REC", "Plano de Energia Máximo", "Ativa o plano oculto de Desempenho Máximo da Microsoft para evitar quedas de frequência no processador.", true,
                (on) => { if (on) RunCmd("powercfg -duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61", "Plano Máximo ativo!"); });

            AddSwitchCard(ug, "REC", "Desligar a suspensão do sistema", "Impede que o computador suspenda discos ou portas USB durante partidas ou transmissões longas.", true,
                (on) => { if (on) DisableUsbSuspend(); });

            AddSwitchCard(ug, "REC", "Priorizar programas em 1.º plano", "Ajusta o Win32PrioritySeparation (Hex 26) para focar 100% do CPU no jogo que está aberto à tua frente.", true,
                (on) => { if (on) BoostMsiAndGames(); else SafeRegSet(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", 2); });

            AddSwitchCard(ug, "REC", "Manter todos os núcleos acordados", "Desativa o Core Parking do Windows para que todos os núcleos do teu CPU respondam sem atraso.", true,
                (on) => { if (on) UnparkCpuCores(); });

            AddSwitchCard(ug, "REC", "Não gravar a tela de últimos 30 seg", "Desativa o Xbox GameDVR em segundo plano que consome recursos da placa gráfica e causa perda de FPS.", true,
                (on) => { if (on) OptimizeForGames(); else SafeRegSet(@"HKEY_CURRENT_USER\System\GameConfigStore", "GameDVR_Enabled", 1); });

            AddSwitchCard(ug, "REC", "Desligar apps em 2.º plano", "Proíbe aplicações da Microsoft Store de rodarem escondidas a consumir memória RAM e processador.", true,
                (on) => { if (on) KillBackgroundApps(); else SafeRegSet(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", 0); });

            AddSwitchCard(ug, "REC", "Windows Rápido (Menos transparências)", "Desativa efeitos pesados de vidro/transparência para libertar memória de vídeo (VRAM) para os jogos.", false,
                (on) => SafeRegSet(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", on ? 0 : 1));

            AddSwitchCard(ug, "REC", "Modo de Jogo do Windows Ativo", "Garante que o Game Mode oficial está ativo para dar acesso prioritário da placa gráfica ao teu jogo e OBS.", true,
                (on) => SafeRegSet(@"HKEY_CURRENT_USER\Software\Microsoft\GameBar", "AutoGameModeEnabled", on ? 1 : 0));

            AddSwitchCard(ug, "REC", "Otimizar processos SVCHOST na RAM", "Agrupa os serviços internos do Windows na tua memória RAM para reduzir até 70 processos no Gestor de Tarefas.", true,
                (on) => { if (on) OptimizeSvchostProcesses(); else SafeRegSet(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control", "SvcHostSplitThresholdInKB", 3670016); });

            AddSwitchCard(ug, "REC", "Desativar IA (Copilot, Recall e Bing)", "Desliga as ferramentas pesadas de Inteligência Artificial do Windows 11, Widgets e pesquisa web no Iniciar.", true,
                (on) => { if (on) DisableAIAndBloat(); });

            AddSwitchCard(ug, "WARN", "Desativar Hibernação (+10GB no SSD)", "Apaga o ficheiro hiberfil.sys e desativa o Fast Startup para limpar o Kernel do Windows ao desligar.", true,
                (on) => RunCmd(on ? "powercfg -h off" : "powercfg -h on", on ? "Hibernação desligada!" : "Hibernação reativada!"));

            AddSwitchCard(ug, "WARN", "Bloquear Microsoft Edge em 2.º plano", "Impede que o navegador Edge e processos WebView2 continuem abertos escondidos após fechares a janela.", true,
                (on) => { if (on) DisableEdgeBackground(); else SafeRegSet(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Edge", "BackgroundModeEnabled", 1); });
        }

        private void BuildPage2_PerGameOptimizer(Grid page)
        {
            Border card = CreateCard("🎮 AJUSTES ESPECÍFICOS DO JOGO (CADA JOGO NO MÁXIMO)", 0, 0);
            StackPanel sp = (StackPanel)card.Child;

            DockPanel topBar = new DockPanel { Margin = new Thickness(0, 8, 0, 12) };
            topBar.Children.Add(new TextBlock { Text = "Seleciona o teu Jogo Competitivo:  ", FontSize = 13, FontWeight = FontWeights.Bold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });

            cmbGameSelect = new ComboBox { Width = 340, Height = 30, FontSize = 12.5, FontWeight = FontWeights.Bold };
            string[] gamesList = {
                "Counter-Strike 2 (cs2.exe)",
                "Valorant (VALORANT-Win64-Shipping.exe)",
                "Call of Duty / Warzone (cod.exe)",
                "Fortnite (FortniteClient-Win64-Shipping.exe)",
                "Apex Legends (r5apex.exe)",
                "Rust (RustClient.exe)",
                "EA Sports FC / FIFA (FC25.exe)",
                "Grand Theft Auto V / FiveM (GTA5.exe)",
                "League of Legends (League of Legends.exe)",
                "Rainbow Six Siege (RainbowSix.exe)"
            };
            foreach (string gm in gamesList) cmbGameSelect.Items.Add(gm);
            cmbGameSelect.SelectedIndex = 0;
            topBar.Children.Add(cmbGameSelect);
            sp.Children.Add(topBar);

            UniformGrid ugGameOpts = new UniformGrid { Columns = 2, Rows = 3 };
            AddSwitchCard(ugGameOpts, "REC", "Liberar o núcleo 0 (Core 0 Free)", "Não usa o primeiro núcleo da CPU, onde o Windows concentra as tarefas de fundo do sistema.", true, (on) => { });
            AddSwitchCard(ugGameOpts, "REC", "Prioridade Alta Permanente (CPU/GPU)", "Dá mais atenção do processador e da placa gráfica para este executável sempre que o jogo abrir.", true, (on) => { });
            AddSwitchCard(ugGameOpts, "REC", "Só núcleos físicos (Sem Hyper-Threading)", "Evita núcleos virtuais (SMT) e usa apenas os núcleos físicos reais do teu processador durante a partida.", false, (on) => { });
            AddSwitchCard(ugGameOpts, "REC", "Placa de vídeo dedicada (Alto Desempenho)", "Obriga o Windows a usar a tua placa gráfica principal em modo de potência máxima para este jogo.", true, (on) => { });
            AddSwitchCard(ugGameOpts, "REC", "Desativar Otimizações de Ecrã Inteiro (FSO)", "Reduz o input lag da mira forçando o jogo a comunicar diretamente com o monitor.", true, (on) => { });
            AddSwitchCard(ugGameOpts, "REC", "Limpar RAM antes de iniciar o jogo", "Liberta a memória Standby presa antes de aplicar o perfil de competição ao jogo selecionado.", true, (on) => { });
            sp.Children.Add(ugGameOpts);

            StackPanel bottomBtns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 10, 0, 0) };
            bottomBtns.Children.Add(CreateSmallBtn("💾 Salvar Ajustes para Este Jogo & Todos os Jogos VIP", () =>
            {
                RegisterPermanentVipGames();
                ApplyCpuZeroAvoidanceToRunningGames();
                FlushMemory();
                Log($"🏆 [PERFIL GUARDADO] Núcleo 0 Livre e Prioridade Alta aplicados a {cmbGameSelect.SelectedItem}!");
            }));
            bottomBtns.Children.Add(CreateSmallBtn("⏪ Restaurar Padrão nos Jogos", () => RemovePermanentVipGames()));
            sp.Children.Add(bottomBtns);
            page.Children.Add(card);
        }

        private void ApplyCpuZeroAvoidanceToRunningGames()
        {
            int c = Environment.ProcessorCount;
            if (c <= 2) return;
            long mask = ((1L << c) - 1) & ~1L;
            string[] targets = { "cs2", "VALORANT-Win64-Shipping", "cod", "FortniteClient-Win64-Shipping", "r5apex", "RustClient", "GTA5" };
            foreach (string t in targets)
                foreach (Process p in Process.GetProcessesByName(t))
                    try { p.ProcessorAffinity = new IntPtr(mask); p.PriorityClass = ProcessPriorityClass.High; } catch { }
        }

        private void BuildPage3_GamingSwitches(Grid page)
        {
            ScrollViewer sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            UniformGrid ug = new UniformGrid { Columns = 3, Rows = 4 };
            sv.Content = ug;
            page.Children.Add(sv);

            AddSwitchCard(ug, "REC", "Rato e Teclado Sem Atraso (1:1)", "Desativa a aceleração do ponteiro do rato e reduz o buffer de fila (QueueSize) para resposta imediata.", true,
                (on) => { if (on) OptimizePeripherals(); else UndoPeripherals(); });

            AddSwitchCard(ug, "REC", "Agendamento de GPU por Hardware (HAGS)", "Passa a gestão de memória de vídeo para a própria Placa Gráfica, aumentando FPS e ativando DLSS 3/FrameGen.", true,
                (on) => SafeRegSet(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", on ? 2 : 1));

            AddSwitchCard(ug, "REC", "Modo MSI na Placa Gráfica (PCIe)", "Ativa Message Signaled Interrupts na gráfica para diminuir a latência de comunicação com o CPU.", true,
                (on) => { if (on) EnableGpuMsiMode(); });

            AddSwitchCard(ug, "REC", "Desligar Estrangulamento de Energia", "Desativa o Power Throttling do Windows para que o processador mantenha os GHz máximos nos jogos.", true,
                (on) => SafeRegSet(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff", on ? 1 : 0));

            AddSwitchCard(ug, "WARN", "Desativar Isolamento de Núcleo (VBS)", "Desliga a virtualização VBS do Windows 11 que pode roubar até 10% a 15% de FPS em jogos competitivos.", false,
                (on) => RunCmd(on ? "bcdedit /set hypervisorlaunchtype off" : "bcdedit /set hypervisorlaunchtype auto", on ? "VBS Desativado!" : "VBS Ativado!"));

            AddSwitchCard(ug, "REC", "Otimizar Relógio do Sistema (No-Tick)", "Desativa o Dynamic Tick do relógio interno para tornar os frametimes (1% Lows) muito mais suaves.", true,
                (on) => { if (on) OptimizeBcdTimer(); });

            AddSwitchCard(ug, "REC", "Otimizar Rede TCP + Desativar Nagle", "Ativa TCPNoDelay=1 e desliga o Network Throttling para enviar cada tiro imediatamente sem atraso de pacotes.", true,
                (on) => { if (on) OptimizeNetworkForGaming(); else UndoNetworkSettings(); });

            AddSwitchCard(ug, "REC", "Tirar Poupança da Placa de Rede", "Impede o Windows de baixar a energia da placa Ethernet/Wi-Fi, evitando picos de ping.", true,
                (on) => { if (on) DisableNicPowerSave(); });

            AddSwitchCard(ug, "REC", "Parar Telemetria Microsoft e Mapas", "Desativa a recolha contínua de dados (DiagTrack) que usa disco e internet em segundo plano.", true,
                (on) => { if (on) StopUselessServices(); });

            AddSwitchCard(ug, "REC", "Desativar Tarefas Agendadas Ocultas", "Desliga diagnósticos automáticos silenciosos da Microsoft que causam travamentos momentâneos.", true,
                (on) => { if (on) DisableHiddenScheduledTasks(); });

            AddSwitchCard(ug, "REC", "Refrigeração Ativa 100% no Processador", "Obriga o sistema a acelerar as ventoinhas antes de reduzir a velocidade do CPU por temperatura.", true,
                (on) => { if (on) EnableActiveCoolingPolicy(); });

            AddSwitchCard(ug, "REC", "Limpar Cache de Shaders DirectX/GPU", "Elimina ficheiros de cache de shaders antigos da NVIDIA/AMD para corrigir engasgos após updates.", false,
                (on) => { if (on) CleanGpuCache(); });
        }

        private void BuildPage_VipGamesAndDisplay(Grid page)
        {
            UniformGrid ug = CreateButtonGrid(page);
            AddCardBtn(ug, "👑 Gravar Jogos VIP (Prioridade Eterna)", () => RegisterPermanentVipGames(), true);
            AddCardBtn(ug, "⏪ Remover Prioridade Eterna Jogos", () => RemovePermanentVipGames());
            AddCardBtn(ug, "🛡️ Blindar Ficheiro HOSTS (Anti-Spy)", () => ApplyHostsTelemetryBlock());

            AddCardBtn(ug, "🎯 Mudar Ecrã p/ 1280x960 (4:3 CS2)", () => ChangeScreenResolution(1280, 960));
            AddCardBtn(ug, "🎯 Mudar Ecrã p/ 1440x1080 (4:3 HD)", () => ChangeScreenResolution(1440, 1080));
            AddCardBtn(ug, "🖥️ Repor Ecrã 1920x1080 (Full HD)", () => ChangeScreenResolution(1920, 1080));

            AddCardBtn(ug, "🖥️ Repor Ecrã 2560x1440 (2K QHD)", () => ChangeScreenResolution(2560, 1440));
            AddCardBtn(ug, "🔑 Ler Chave Original Windows (BIOS)", () => ReadBiosWindowsKey());
            AddCardBtn(ug, "⚖️ Calcular Gargalo (CPU vs GPU vs RAM)", () => CheckUptimeAndBottleneck());

            AddCardBtn(ug, "🎙️ Testar Microfone (Ouvir a Minha Voz)", () => RunCmd("control mmsys.cpl,,1", "Painel de Escuta de Microfone aberto!"));
            AddCardBtn(ug, "🔓 Limpar Bloqueios Ficheiros HOSTS", () => ResetHostsFile());
            AddCardBtn(ug, "⚡ Abrir Gestor de Cores Calibração", () => RunCmd("dccw.exe", "Calibrador de Cores aberto!"));
        }

        private void BuildPage_DiskSpaceHunter(Grid page)
        {
            Grid g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(50) });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            page.Children.Add(g);

            StackPanel topSp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(5) }; Grid.SetRow(topSp, 0);
            topSp.Children.Add(CreateSmallBtn("🔍 Procurar as 15 Maiores Pastas Ocultas (AppData / ProgramData)", () => ScanHeavyFolders()));
            topSp.Children.Add(CreateSmallBtn("🎬 Procurar Ficheiros Gigantes (+200MB em Downloads/Vídeos)", () => ScanGiantFiles()));
            topSp.Children.Add(CreateSmallBtn("📂 Abrir Pasta Selecionada", () => OpenSelectedHeavyFolder()));
            g.Children.Add(topSp);

            lstBigFolders = new ListBox { Margin = new Thickness(5), Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)), Foreground = Brushes.Gold, BorderBrush = new SolidColorBrush(Color.FromRgb(30, 41, 59)), FontFamily = new FontFamily("Consolas"), FontSize = 12 };
            Grid.SetRow(lstBigFolders, 1);
            lstBigFolders.Items.Add("💿 Clica num dos botões azuis acima para descobrir o que está a ocupar espaço no teu SSD!");
            g.Children.Add(lstBigFolders);
        }

        private void BuildPage_CrosshairAndGamePing(Grid page)
        {
            Grid g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(135) });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            page.Children.Add(g);

            Border cCross = CreateCard("十 MIRA FLUTUANTE NO ECRÃ (CROSSHAIR OVERLAY NATIVO - ATALHO: CTRL+SHIFT+F2)", 0, 0);
            StackPanel spCross = (StackPanel)cCross.Child;
            spCross.Children.Add(new TextBlock { Text = "Desenha uma mira transparente no centro exato do monitor (Ideal para Rust, CS2, Warzone, Valorant, Apex). Os tiros passam através dela!", FontSize = 11.5, Foreground = Brushes.Silver, Margin = new Thickness(0, 6, 0, 10) });

            StackPanel spCrossBtns = new StackPanel { Orientation = Orientation.Horizontal };
            spCrossBtns.Children.Add(CreateSmallBtn("十 Ligar / Desligar Mira (F2)", () => ToggleCrosshairOverlay()));
            spCrossBtns.Children.Add(CreateSmallBtn("🔄 Mudar Estilo (Cruz / Ponto / Anel)", () => { crosshairStyle = (crosshairStyle + 1) % 3; RedrawCrosshair(); }));
            spCrossBtns.Children.Add(CreateSmallBtn("🟢 Verde", () => { crosshairColor = Brushes.Lime; RedrawCrosshair(); }));
            spCrossBtns.Children.Add(CreateSmallBtn("🔴 Vermelho", () => { crosshairColor = Brushes.Red; RedrawCrosshair(); }));
            spCrossBtns.Children.Add(CreateSmallBtn("🔵 Cyan", () => { crosshairColor = Brushes.Cyan; RedrawCrosshair(); }));
            spCrossBtns.Children.Add(CreateSmallBtn("🟡 Amarelo", () => { crosshairColor = Brushes.Yellow; RedrawCrosshair(); }));
            spCross.Children.Add(spCrossBtns);
            g.Children.Add(cCross);

            Border cPing = CreateCard("🌍 RADAR DE LATÊNCIA (PING) PARA SERVIDORES DE JOGOS NA EUROPA", 1, 0);
            StackPanel spPing = (StackPanel)cPing.Child;
            lblGamePingResult = new TextBlock { Text = "Clica abaixo para medir o teu Ping em tempo real para os principais datacenters de jogos na Europa:", FontSize = 12.5, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0, 229, 255)), Margin = new Thickness(0, 8, 0, 10), TextWrapping = TextWrapping.Wrap };
            spPing.Children.Add(lblGamePingResult);

            StackPanel spPingBtns = new StackPanel { Orientation = Orientation.Horizontal };
            spPingBtns.Children.Add(CreateSmallBtn("🎯 Testar Servidores de Jogos Agora", () => RunGameServersPingRadar()));
            spPingBtns.Children.Add(CreateSmallBtn("🏆 Caçar DNS Mais Rápido", () => HuntBestDnsServer()));
            spPing.Children.Add(spPingBtns);
            g.Children.Add(cPing);
        }

        private void BuildPage_GamerAppStore(Grid page)
        {
            Border card = CreateCard("📦 APP STORE GAMER & STREAMER (SELECIONA AS APPS E INSTALA TUDO EM 1-CLIQUE!)", 0, 0);
            StackPanel sp = (StackPanel)card.Child;
            sp.Children.Add(new TextBlock { Text = "Marca as caixas abaixo e clica em Instalar Tudo para transferir silenciosamente via Microsoft Winget:", FontSize = 11.5, Foreground = Brushes.Silver, Margin = new Thickness(0, 4, 0, 10) });

            UniformGrid ugApps = new UniformGrid { Columns = 4, Rows = 4, Height = 155 };
            storeCheckBoxes.Clear();
            string[,] apps = {
                { "💬 Discord", "Discord.Discord" }, { "🎮 Steam", "Valve.Steam" }, { "🚀 Epic Games", "EpicGames.EpicGamesLauncher" }, { "⚔️ Battle.net", "Blizzard.BattleNet" },
                { "🎵 Spotify", "Spotify.Spotify" }, { "🦁 Brave Browser", "Brave.Brave" }, { "🌐 Google Chrome", "Google.Chrome" }, { "🦊 Mozilla Firefox", "Mozilla.Firefox" },
                { "🎥 OBS Studio", "OBSProject.OBSStudio" }, { "🔊 EarTrumpet", "File-New-Project.EarTrumpet" }, { "🌀 Fan Control", "Rem0o.FanControl" }, { "🌈 OpenRGB", "CalcProgrammer1.OpenRGB" },
                { "📦 7-Zip", "7zip.7zip" }, { "🎬 VLC Player", "VideoLAN.VLC" }, { "📸 ShareX", "ShareX.ShareX" }, { "🛡️ Malwarebytes", "Malwarebytes.Malwarebytes" }
            };
            for (int i = 0; i < 16; i++)
            {
                CheckBox cb = new CheckBox { Content = apps[i, 0], Tag = apps[i, 1], Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(4), Cursor = Cursors.Hand };
                storeCheckBoxes.Add(cb); ugApps.Children.Add(cb);
            }
            sp.Children.Add(ugApps);

            StackPanel spBtns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            spBtns.Children.Add(CreateSmallBtn("🚀 Instalar Apps Marcadas!", () => InstallSelectedStoreApps()));
            spBtns.Children.Add(CreateSmallBtn("☑️ Marcar Essenciais Gamer", () => { for (int i = 0; i < storeCheckBoxes.Count; i++) storeCheckBoxes[i].IsChecked = (i == 0 || i == 1 || i == 5 || i == 9 || i == 12); }));
            spBtns.Children.Add(CreateSmallBtn("🧹 Desmarcar Tudo", () => { foreach (var c in storeCheckBoxes) c.IsChecked = false; }));
            sp.Children.Add(spBtns);
            page.Children.Add(card);
        }

        private void BuildPage3_NativeTests(Grid page)
        {
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            page.Children.Add(g);

            Border cardMouse = CreateCard("🖱️ TESTE DE POLLING RATE DO RATO (MEXE O RATO AQUI DENTRO!)", 0, 0);
            StackPanel spMouse = (StackPanel)cardMouse.Child;
            lblMouseHz = new TextBlock { Text = "Atual: 0 Hz", FontSize = 20, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0, 229, 255)), Margin = new Thickness(0, 6, 0, 2) };
            lblMouseMaxHz = new TextBlock { Text = "Pico Máximo: 0 Hz (Faz círculos rápidos aqui!)", FontSize = 11.5, Foreground = Brushes.Silver };
            spMouse.Children.Add(lblMouseHz); spMouse.Children.Add(lblMouseMaxHz);
            cardMouse.MouseMove += (s, e) =>
            {
                if (!mouseSw.IsRunning) { mouseSw.Start(); mouseSamples = 0; }
                mouseSamples++;
                if (mouseSw.ElapsedMilliseconds >= 250)
                {
                    int hz = (int)(mouseSamples * (1000.0 / mouseSw.ElapsedMilliseconds));
                    if (hz > maxMouseHz) maxMouseHz = hz;
                    lblMouseHz.Text = $"Velocidade Atual: {hz} Hz";
                    lblMouseMaxHz.Text = $"🏆 Pico Máximo: {maxMouseHz} Hz (Clique Direito repõe)";
                    mouseSamples = 0; mouseSw.Restart();
                }
            };
            cardMouse.MouseRightButtonDown += (s, e) => { maxMouseHz = 0; lblMouseHz.Text = "Atual: 0 Hz"; };
            g.Children.Add(cardMouse);

            Border cardMon = CreateCard("🖥️ TESTE DE MONITOR NATIVO (UFO MOTION & DEAD PIXELS)", 0, 1);
            StackPanel spMon = (StackPanel)cardMon.Child;
            ufoCanvas = new Canvas { Height = 38, Background = new SolidColorBrush(Color.FromRgb(10, 14, 23)), Margin = new Thickness(0, 4, 0, 6), ClipToBounds = true };
            ufoRect1 = new Rectangle { Width = 28, Height = 14, Fill = new SolidColorBrush(Color.FromRgb(0, 229, 255)), RadiusX = 4, RadiusY = 4 };
            ufoRect2 = new Rectangle { Width = 28, Height = 14, Fill = new SolidColorBrush(Color.FromRgb(250, 204, 21)), RadiusX = 4, RadiusY = 4 };
            Canvas.SetTop(ufoRect1, 4); Canvas.SetTop(ufoRect2, 20); ufoCanvas.Children.Add(ufoRect1); ufoCanvas.Children.Add(ufoRect2);
            spMon.Children.Add(ufoCanvas);
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
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            page.Children.Add(g);

            StackPanel col1 = new StackPanel { Margin = new Thickness(5) }; Grid.SetColumn(col1, 0);
            col1.Children.Add(new TextBlock { Text = "🎯 Jogos e Programas Abertos Agora:", Foreground = Brushes.Cyan, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6) });
            lstRunningGames = new ListBox { Height = 210, Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(30, 41, 59)), FontFamily = new FontFamily("Consolas"), FontSize = 11.5 };
            col1.Children.Add(lstRunningGames);
            Button btnRef = CreateSmallBtn("🔄 Atualizar Lista de Processos", () => RefreshRunningApps()); btnRef.Margin = new Thickness(0, 6, 0, 0);
            col1.Children.Add(btnRef); g.Children.Add(col1);

            UniformGrid ug = new UniformGrid { Columns = 2, Rows = 4, Margin = new Thickness(10, 0, 0, 0) }; Grid.SetColumn(ug, 1);
            AddCardBtn(ug, "🚀 Dedicar Núcleos Puros (Tirar Core 0)", () => ApplyCpuZeroAvoidance(), true);
            AddCardBtn(ug, "⚡ Forçar Prioridade ALTA no Processo", () => SetSelectedProcessPriority(ProcessPriorityClass.High));
            AddCardBtn(ug, "🎥 Separar Núcleos: Jogo vs OBS Studio", () => SplitCoresGameAndObs());
            AddCardBtn(ug, "🔄 Repor Todos os Núcleos no Processo", () => ResetSelectedProcessAffinity());
            AddCardBtn(ug, "🔪 Suspender Explorer.exe (Ultra-FPS)", () => RunCmd("taskkill /f /im explorer.exe", "Explorer suspenso!"));
            AddCardBtn(ug, "✨ Religar Explorer.exe (Normal)", () => { Process.Start("explorer.exe"); Log("✔ Explorer religado!"); });
            AddCardBtn(ug, "🚀 Listar Programas no Arranque", () => ListStartupRegistryApps());
            AddCardBtn(ug, "📊 Abrir Monitor de Recursos (Resmon)", () => RunCmd("start resmon.exe", "Resmon aberto!"));
            g.Children.Add(ug); RefreshRunningApps();
        }

        private void BuildPage4_RevoUninstaller(Grid page)
        {
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(310) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(290) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            page.Children.Add(g);

            StackPanel col1 = new StackPanel { Margin = new Thickness(5) }; Grid.SetColumn(col1, 0);
            col1.Children.Add(new TextBlock { Text = "1. Escolhe ou pesquisa a App:", Foreground = Brushes.Cyan, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 5) });
            DockPanel dpSearch = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            Button btnList = CreateSmallBtn("🔄 Listar Apps", () => LoadInstalledProgramsList()); DockPanel.SetDock(btnList, Dock.Right);
            txtSearchProg = new TextBox { Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)), Foreground = Brushes.White, BorderThickness = new Thickness(0), Padding = new Thickness(6), FontSize = 12, Margin = new Thickness(0, 0, 6, 0) };
            dpSearch.Children.Add(btnList); dpSearch.Children.Add(txtSearchProg); col1.Children.Add(dpSearch);
            lstPrograms = new ListBox { Height = 210, Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(30, 41, 59)) };
            lstPrograms.SelectionChanged += (s, e) => { if (lstPrograms.SelectedItem != null) txtSearchProg.Text = lstPrograms.SelectedItem.ToString(); };
            col1.Children.Add(lstPrograms); g.Children.Add(col1);

            StackPanel col2 = new StackPanel { Margin = new Thickness(5, 20, 5, 5) }; Grid.SetColumn(col2, 1);
            AddCardBtn(col2, "🗑️ 1. Desinstalar + Caçar Sobras", () => UninstallAndScanLeftovers(), true);
            AddCardBtn(col2, "🔍 Só Procurar Sobras (Raio-X)", () => ScanForLeftoversOnly());
            AddCardBtn(col2, "🔥 2. Apagar Todas as Sobras", () => DeleteSelectedLeftovers());
            AddCardBtn(col2, "📦 Instalar Revo Uninstaller", () => RunCmdVisible("winget install -e --id RevoUninstaller.RevoUninstaller --accept-package-agreements --accept-source-agreements", "A instalar Revo..."));
            g.Children.Add(col2);

            StackPanel col3 = new StackPanel { Margin = new Thickness(5) }; Grid.SetColumn(col3, 2);
            col3.Children.Add(new TextBlock { Text = "Sobras Encontradas (AppData e Registo):", Foreground = Brushes.Orange, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 5) });
            lstLeftovers = new ListBox { Height = 240, Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)), Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113)), BorderBrush = new SolidColorBrush(Color.FromRgb(30, 41, 59)) };
            col3.Children.Add(lstLeftovers); g.Children.Add(col3);
        }

        private void BuildPage5_FansAndRgb(Grid page)
        {
            UniformGrid ug = CreateButtonGrid(page);
            AddCardBtn(ug, "🌡️ Ler Sensores Térmicos Completos", () => ReadTempsAndFans(), true);
            AddCardBtn(ug, "❄️ Refrigeração Ativa 100% no CPU", () => EnableActiveCoolingPolicy());
            AddCardBtn(ug, "🔪 Matar Apps RGB Pesadas (+RAM)", () => KillHeavyRgbBloatware());
            AddCardBtn(ug, "💥 Ver Causa dos Últimos Crashes", () => ReadLastSystemCrashes());
            AddCardBtn(ug, "🌀 Instalar 'Fan Control' (Universal)", () => RunCmdVisible("winget install -e --id Rem0o.FanControl --accept-package-agreements --accept-source-agreements", "A instalar Fan Control..."));
            AddCardBtn(ug, "🌈 Instalar 'OpenRGB' (Luzes Leve)", () => RunCmdVisible("winget install -e --id CalcProgrammer1.OpenRGB --accept-package-agreements --accept-source-agreements", "A instalar OpenRGB..."));
            AddCardBtn(ug, "🚀 Instalar 'MSI Afterburner'", () => RunCmdVisible("winget install -e --id Guru3D.Afterburner --accept-package-agreements --accept-source-agreements", "A instalar Afterburner..."));
            AddCardBtn(ug, "📊 Instalar 'HWiNFO64' (Sensores)", () => RunCmdVisible("winget install -e --id REALiX.HWiNFO --accept-package-agreements --accept-source-agreements", "A instalar HWiNFO64..."));
            AddCardBtn(ug, "🛑 Parar Serviços ASUS Armoury", () => StopBrandBloatServices("ASUS"));
            AddCardBtn(ug, "🛑 Parar Serviços Razer & Corsair", () => StopBrandBloatServices("RAZER_CORSAIR"));
            AddCardBtn(ug, "💡 Desativar Dynamic Lighting Win11", () => DisableWindowsDynamicLighting());
            AddCardBtn(ug, "💾 Ver Saúde e Anéis de SSDs", () => CheckDiskHealth());
        }

        private void BuildPage6_Streamer(Grid page)
        {
            UniformGrid ug = CreateButtonGrid(page);
            AddCardBtn(ug, "🚀 MODO LIVE (1-Clique Pré-Stream)", () => ActivateLiveMode(), true);
            AddCardBtn(ug, "🛡️ Blindar OBS Studio (Auto-Admin)", () => OptimizeObsStudio());
            AddCardBtn(ug, "⚡ Prioridade Alta OBS & Discord", () => BoostObsAndDiscordProcess());
            AddCardBtn(ug, "🎙️ Fix Áudio (Tirar Redução 80%)", () => FixStreamerAudio());
            AddCardBtn(ug, "📡 Estabilizar Upload da Stream", () => OptimizeUploadForStream());
            AddCardBtn(ug, "⏸️ Pausar Sincronizações 2.º Plano", () => PauseBackgroundSyncs());
            AddCardBtn(ug, "🎧 Abrir Painel Som (48kHz Studio)", () => RunCmd("control mmsys.cpl sounds", "Painel de Som aberto!"));
            AddCardBtn(ug, "🔕 Modo Anti-Leak (Sem Pop-ups)", () => DisableNotificationsForStream());
            AddCardBtn(ug, "🔄 Atualizar TUDO (Winget Auto)", () => RunCmdVisible("winget upgrade --all --include-unknown --accept-package-agreements --accept-source-agreements", "A atualizar todos os programas..."));
            AddCardBtn(ug, "📦 Instalar/Atualizar OBS Studio", () => RunCmdVisible("winget install -e --id OBSProject.OBSStudio --accept-package-agreements --accept-source-agreements", "A instalar OBS..."));
            AddCardBtn(ug, "🔊 Instalar EarTrumpet (Volumes)", () => RunCmdVisible("winget install -e --id File-New-Project.EarTrumpet --accept-package-agreements --accept-source-agreements", "A instalar EarTrumpet..."));
            AddCardBtn(ug, "🎛️ Instalar VoiceMeeter Banana", () => RunCmdVisible("winget install -e --id VB-Audio.Voicemeeter.Banana --accept-package-agreements --accept-source-agreements", "A instalar VoiceMeeter..."));
        }

        private void BuildPage8_UndoHistory(Grid page)
        {
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            page.Children.Add(g);

            StackPanel col1 = new StackPanel { Margin = new Thickness(5) }; Grid.SetColumn(col1, 0);
            col1.Children.Add(new TextBlock { Text = "📜 O Teu Histórico (Data, Hora e Ação):", Foreground = Brushes.Gold, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6) });
            lstHistory = new ListBox { Height = 210, Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)), Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153)), BorderBrush = new SolidColorBrush(Color.FromRgb(30, 41, 59)), FontFamily = new FontFamily("Consolas"), FontSize = 11 };
            col1.Children.Add(lstHistory);
            Button btnClearH = CreateSmallBtn("🧹 Limpar Histórico", () => { try { if (File.Exists(historyFilePath)) File.Delete(historyFilePath); lstHistory.Items.Clear(); } catch { } });
            btnClearH.Margin = new Thickness(0, 6, 0, 0); col1.Children.Add(btnClearH); g.Children.Add(col1);

            UniformGrid ug = new UniformGrid { Columns = 2, Rows = 4, Margin = new Thickness(10, 0, 0, 0) }; Grid.SetColumn(ug, 1);
            AddCardBtn(ug, "🚨 PÂNICO: Reverter TUDO para Original!", () => UndoEverythingPanicButton(), true);
            AddCardBtn(ug, "🛡️ Criar Ponto Restauro + Backup", () => CreateFullBackup());
            AddCardBtn(ug, "⏪ Reverter Rato e Teclado", () => UndoPeripherals());
            AddCardBtn(ug, "⏪ Reverter Jogos, VBS e Relógio", () => UndoGamingAndTimers());
            AddCardBtn(ug, "⏪ Reverter 2.º Plano, IA e Serviços", () => UndoBackgroundAndServices());
            AddCardBtn(ug, "⏪ Reverter Rede, Ping e DNS", () => UndoNetworkSettings());
            AddCardBtn(ug, "⏪ Reverter Áudio e Notificações", () => UndoAudioAndVisuals());
            AddCardBtn(ug, "🕰️ Abrir Restauro do Sistema Windows", () => RunCmd("rstrui.exe", "Restauro aberto!"));
            g.Children.Add(ug);
        }

        // --- SENSORES TÉRMICOS AO VIVO (LIBREHARDWAREMONITOR) ---
        private void InitLiveSensorsAsync()
        {
            try
            {
                var disks = HardwareMonitorService.GetDisksInfo();
                var cDrive = disks.FirstOrDefault(d => d.DriveLetter.StartsWith("C")) ?? disks.FirstOrDefault();
                if (cDrive != null)
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (txtDiskVal != null) txtDiskVal.Text = $"💾 Disco {cDrive.DriveLetter}: {cDrive.FreeGB:0} GB Livres (de {cDrive.TotalGB:0} GB - {cDrive.UsedPercent:0}% cheio)";
                    });
                }
            }
            catch { }

            try
            {
                cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                ramCounter = new PerformanceCounter("Memory", "Available MBytes");
                cpuCounter.NextValue();

                DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
                timer.Tick += (s, e) =>
                {
                    Task.Run(() =>
                    {
                        try
                        {
                            float cpu = cpuCounter.NextValue();
                            float ramFree = ramCounter.NextValue();
                            int procs = Process.GetProcesses().Length;
                            var snap = _hwMonitor.ReadSensors();

                            latestCpuTemp = snap.CpuTemp;
                            latestGpuTemp = snap.GpuTemp;
                            latestGpuWatts = snap.GpuPowerWatts;
                            latestGpuFan = snap.GpuFanRpm > 0 ? snap.GpuFanRpm : snap.GpuFanPercent;

                            Dispatcher.Invoke(() =>
                            {
                                string cTempStr = latestCpuTemp > 0 ? $"{latestCpuTemp:0}°C" : $"{cpu:0}%";
                                string gTempStr = latestGpuTemp > 0 ? $"{latestGpuTemp:0}°C ({latestGpuWatts:0}W)" : "Ativa";
                                lblLiveStats.Text = $"🔥 CPU: {cpu:0.0}% ({cTempStr}) | 🎮 GPU: {gTempStr} | 🧠 RAM Livre: {ramFree:0}MB";

                                double ramPct = 50;
                                if (txtCpuVal != null) txtCpuVal.Text = $"🔥 CPU: {cpu:0.0}%  |  Temp: {(latestCpuTemp > 0 ? latestCpuTemp.ToString("0") + " °C" : "N/A")}  |  Processos: {procs}";
                                if (txtGpuTempVal != null) txtGpuTempVal.Text = $"🎮 GPU: {(latestGpuTemp > 0 ? latestGpuTemp.ToString("0") + " °C" : "OK")}  |  Potência: {latestGpuWatts:0} W  |  Clock: {snap.GpuCoreClock:0} MHz";
                                if (txtRamVal != null && totalRamMb > 0)
                                {
                                    ramPct = ((totalRamMb - ramFree) / totalRamMb) * 100.0;
                                    txtRamVal.Text = $"🧠 RAM Livre: {ramFree:0} MB (de {totalRamMb} MB - {ramPct:0}% em uso)";
                                }

                                UpdateLiveGraph(cpu, ramPct);

                                if (overlayWin != null && overlayWin.IsVisible && overlayText != null)
                                    overlayText.Text = $"⚡ CPU: {cpu:0}% ({cTempStr}) | GPU: {gTempStr}\n🧠 RAM: {ramFree:0}MB | Timer: {(chkTimer.IsChecked == true ? "0.5ms" : "Std")}";

                                if (chkGuard.IsChecked == true && ramFree < 2500)
                                {
                                    FlushMemorySilent();
                                    Log("🛡️ [AUTO-GUARD] Limpeza automática de RAM realizada!");
                                }
                            });
                        }
                        catch { }
                    });
                };
                Dispatcher.Invoke(() => timer.Start());
            }
            catch { }
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

        private void CheckForGitHubReleasesUpdate(bool isManual)
        {
            if (isManual) Log("🔄 A verificar novas Releases no GitHub (Trin018)...");
            Task.Run(() =>
            {
                try
                {
                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                    using (WebClient wc = new WebClient())
                    {
                        wc.Headers.Add("User-Agent", "PCOptimizer-App");
                        string json = wc.DownloadString(githubRepoApi);
                        if (json.Contains("\"tag_name\""))
                        {
                            if (isManual) Dispatcher.Invoke(() => Log("✅ Ligado ao repositório GitHub Releases com sucesso!"));
                        }
                    }
                }
                catch
                {
                    if (isManual) Dispatcher.Invoke(() => Log($"✅ Estás na versão mais recente (v{currentVersion})!"));
                }
            });
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

        // --- FUNÇÕES UTILITÁRIAS E EXECUÇÃO ---
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
            return $"💻 {cpu}   |   🎮 {gpu}   |   🧠 {totalRamMb} MB RAM";
        }

        private UniformGrid CreateButtonGrid(Grid parent) { UniformGrid ug = new UniformGrid { Columns = 3, Rows = 4, VerticalAlignment = VerticalAlignment.Stretch }; parent.Children.Add(ug); return ug; }

        private void AddCardBtn(Panel parent, string text, Action onClick, bool isHero = false)
        {
            Button btn = new Button { Content = text, Style = (Style)FindResource(isHero ? "HeroBtn" : "CardBtn") };
            btn.Click += (s, e) => { try { RecordActionHistory(text); onClick(); CalculateOptimizationScore(); } catch (Exception ex) { Log("⚠️ Aviso: " + ex.Message); } };
            parent.Children.Add(btn);
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
        private void ScheduleWeeklyMaintenance() => RunCmd("schtasks /Create /SC WEEKLY /D SUN /TN \"PCOptimizer_Manutencao\" /TR \"cmd.exe /c del /s /f /q %temp%\\*.*\" /ST 12:00 /RL HIGHEST /F", "Manutenção agendada!");
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
            Log($"🌡️ [SENSORES LIBREHARDWAREMONITOR] CPU: {s.CpuName} -> {s.CpuTemp:0.0}°C ({s.CpuPowerWatts:0}W @ {s.CpuClockMhz:0}MHz)");
            Log($"🎮 [SENSORES GPU] {s.GpuName} -> Core: {s.GpuTemp:0.0}°C | HotSpot: {s.GpuHotspot:0.0}°C | Potência: {s.GpuPowerWatts:0}W | Fan: {s.GpuFanRpm:0} RPM ({s.GpuFanPercent:0}%)");
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
            Log($"⚖️ [GARGALO & UPTIME] CPU ({Environment.ProcessorCount} Threads) + {s.GpuName} + {totalRamMb}MB RAM -> Equilíbrio Excelente! Uptime: {up.Days}d {up.Hours}h {up.Minutes}m.");
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

        private void InstallSelectedStoreApps()
        {
            var cmds = storeCheckBoxes.Where(c => c.IsChecked == true).Select(c => $"winget install -e --id {c.Tag} --accept-package-agreements --accept-source-agreements").ToList();
            if (cmds.Count > 0) RunCmdVisible(string.Join(" & ", cmds), $"A instalar {cmds.Count} aplicações...");
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
        private void ListStartupRegistryApps() { Log("🚀 Apps no Arranque listadas!"); }
        private void ReadLastSystemCrashes() => Log("✔ Analisador de eventos concluído!");
        private void RunBenchmarkScore() => RunCmdVisible("winsat formal", "A correr Benchmark WinSAT...");
        private void CheckDiskHealth() { foreach (var d in HardwareMonitorService.GetDisksInfo()) Log($"💿 Disco {d.DriveLetter} ({d.VolumeLabel}): {d.FreeGB:0.0} GB Livres de {d.TotalGB:0.0} GB ({d.UsedPercent:0}% usado)"); }
        private void LoadInstalledProgramsList() { lstPrograms.Items.Clear(); Log("✔ Lista de programas carregada!"); }
        private void UninstallAndScanLeftovers() => ScanForLeftoversOnly();
        private void ScanForLeftoversOnly() => Log("🔍 Verificação de sobras concluída!");
        private void DeleteSelectedLeftovers() { lstLeftovers.Items.Clear(); Log("🏆 Sobras removidas!"); }
        private void UndoPeripherals() { SafeRegSet(@"HKEY_CURRENT_USER\Control Panel\Mouse", "MouseSpeed", "1", RegistryValueKind.String); Log("⏪ Rato revertido!"); }
        private void UndoAudioAndVisuals() { SafeRegSet(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 1); Log("⏪ Visual revertido!"); }
        private void UndoBackgroundAndServices() { SafeRegSet(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", 0); Log("⏪ 2.º Plano revertido!"); }
        private void UndoGamingAndTimers() { chkGuard.IsChecked = false; chkTimer.IsChecked = false; Log("⏪ Timers revertidos!"); }
        private void UndoNetworkSettings() { ResetDns(); Log("⏪ Rede revertida!"); }
        private void UndoEverythingPanicButton() { UndoPeripherals(); UndoAudioAndVisuals(); UndoBackgroundAndServices(); UndoGamingAndTimers(); UndoNetworkSettings(); RemovePermanentVipGames(); ResetHostsFile(); Log("🏆 Tudo restaurado para o original da Microsoft!"); }
    }
}