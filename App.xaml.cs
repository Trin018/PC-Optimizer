using System;
using System.IO;
using System.Windows;

namespace PCOptimizer
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            try { Directory.CreateDirectory(@"C:\PCOptimizer_Backups"); } catch { }

            this.DispatcherUnhandledException += (s, args) =>
            {
                args.Handled = true;
            };
        }
    }
}