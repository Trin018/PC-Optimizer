using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PCOptimizer
{
    public class GameSimBenchmark
    {
        private struct Unit
        {
            public float X, Y, VX, VY;
            public byte Team;
        }

        public static void OpenBattleBenchmarkWindow()
        {
            Window win = new Window
            {
                Title = "⚡ PC Optimizer - Game Simulation Benchmark (30.000 Unidades em Batalha)",
                Width = 960,
                Height = 620,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = new SolidColorBrush(Color.FromRgb(9, 13, 22)),
                ResizeMode = ResizeMode.NoResize
            };

            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(65) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Topo com Métricas Ao Vivo (FPS, 1% Low, Unidades, Score)
            Border header = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(20, 10, 20, 10)
            };
            Grid.SetRow(header, 0);

            TextBlock lblStats = new TextBlock
            {
                Text = "🔥 A preparar 30.000 Unidades no Campo de Batalha (15.000 Cyan vs 15.000 Crimson)...",
                Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            };
            header.Child = lblStats;
            root.Children.Add(header);

            // Ecrã de Renderização Rápida por Bitmap (WriteableBitmap)
            int bmpW = 920, bmpH = 500;
            WriteableBitmap bmp = new WriteableBitmap(bmpW, bmpH, 96, 96, PixelFormats.Bgr32, null);
            Image img = new Image { Source = bmp, Stretch = Stretch.Fill, Margin = new Thickness(15) };
            Grid.SetRow(img, 1);
            root.Children.Add(img);
            win.Content = root;

            // Inicializar 30.000 Unidades
            int unitCount = 30000;
            Unit[] units = new Unit[unitCount];
            Random rnd = new Random(42);
            for (int i = 0; i < unitCount; i++)
            {
                bool teamA = i < (unitCount / 2);
                units[i] = new Unit
                {
                    X = teamA ? (float)(rnd.NextDouble() * (bmpW * 0.35)) : (float)(bmpW * 0.65 + rnd.NextDouble() * (bmpW * 0.35)),
                    Y = (float)(rnd.NextDouble() * bmpH),
                    VX = teamA ? (float)(rnd.NextDouble() * 2.5 + 0.5) : -(float)(rnd.NextDouble() * 2.5 + 0.5),
                    VY = (float)(rnd.NextDouble() * 2.0 - 1.0),
                    Team = (byte)(teamA ? 0 : 1)
                };
            }

            int[] pixels = new int[bmpW * bmpH];
            Stopwatch totalSw = Stopwatch.StartNew();
            Stopwatch frameSw = Stopwatch.StartNew();
            List<double> fpsSamples = new List<double>();
            bool running = true;

            EventHandler renderLoop = null;
            renderLoop = (s, e) =>
            {
                if (!running) return;
                double dtMs = frameSw.Elapsed.TotalMilliseconds;
                frameSw.Restart();
                if (dtMs > 0) fpsSamples.Add(1000.0 / dtMs);

                // Limpar frame para azul escuro profundo
                Array.Fill(pixels, (10 << 16) | (15 << 8) | 26);

                // Simular Física Multi-Core nos 30.000 guerreiros!
                Parallel.For(0, unitCount, i =>
                {
                    Unit u = units[i];
                    // Atração para o centro de batalha + turbulência física
                    float targetX = bmpW * 0.5f;
                    float dirX = (targetX - u.X) * 0.0018f;
                    u.VX = (u.VX * 0.992f) + dirX + (float)Math.Sin(u.Y * 0.05f + i) * 0.08f;
                    u.VY = (u.VY * 0.992f) + (float)Math.Cos(u.X * 0.05f + i) * 0.08f;

                    u.X += u.VX;
                    u.Y += u.VY;

                    if (u.X < 2) { u.X = 2; u.VX = -u.VX; }
                    else if (u.X >= bmpW - 2) { u.X = bmpW - 3; u.VX = -u.VX; }
                    if (u.Y < 2) { u.Y = 2; u.VY = -u.VY; }
                    else if (u.Y >= bmpH - 2) { u.Y = bmpH - 3; u.VY = -u.VY; }

                    units[i] = u;

                    int px = (int)u.X;
                    int py = (int)u.Y;
                    int idx = py * bmpW + px;
                    if (idx >= 0 && idx < pixels.Length - 1)
                    {
                        int color = u.Team == 0 ? ((0 << 16) | (229 << 8) | 255) : ((244 << 16) | (63 << 8) | 94);
                        pixels[idx] = color;
                        pixels[idx + 1] = color;
                    }
                });

                bmp.WritePixels(new Int32Rect(0, 0, bmpW, bmpH), pixels, bmpW * 4, 0);

                double elapsedSec = totalSw.Elapsed.TotalSeconds;
                if (fpsSamples.Count > 5)
                {
                    double avgFps = fpsSamples.Average();
                    var sorted = fpsSamples.OrderBy(x => x).ToList();
                    double onePctLow = sorted[Math.Max(0, (int)(sorted.Count * 0.01))];
                    int score = (int)(avgFps * 145 + onePctLow * 60);

                    lblStats.Text = $"⚔️ 30.000 Unidades  |  🔥 FPS Atual: {fpsSamples.Last():0}  |  📊 Média: {avgFps:0.0} FPS  |  📉 1% Low: {onePctLow:0.0} FPS  |  🏆 Score: {score:N0} pts ({Math.Max(0, 15 - (int)elapsedSec)}s)";

                    if (elapsedSec >= 15.0)
                    {
                        running = false;
                        CompositionTarget.Rendering -= renderLoop;
                        lblStats.Foreground = Brushes.Gold;
                        lblStats.Text = $"🏆 BENCHMARK CONCLUÍDO! Média: {avgFps:0.0} FPS | 1% Low: {onePctLow:0.0} FPS | PONTUAÇÃO FINAL: {score:N0} PTS!";
                    }
                }
            };

            win.Closed += (s, e) => { running = false; CompositionTarget.Rendering -= renderLoop; };
            CompositionTarget.Rendering += renderLoop;
            win.Show();
        }
    }
}