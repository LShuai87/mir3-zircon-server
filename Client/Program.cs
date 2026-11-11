using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Client.Controls;
using Client.Envir;
using Client.Scenes;
using Library;
using SlimDX.Windows;

namespace Client
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            foreach (KeyValuePair<LibraryFile, string> pair in Libraries.LibraryList)
            {
                if (!File.Exists(@".\" + pair.Value)) continue;

                CEnvir.LibraryList[pair.Key] = new MirLibrary(@".\" + pair.Value);
            }


            ConfigReader.Load();
            
            CEnvir.LoadDatabase();

            CEnvir.Target = new TargetForm();
            DXManager.Create();
            try
            {
                using (var startupUi = GameUiStartupSession.PrepareForLaunch(Config.GameUiStyle, Application.StartupPath, args))
                {
                    CEnvir.GameUiSession = startupUi;
                    if (startupUi.Diagnostic != null)
                    {
                        CEnvir.SaveError("Game UI startup: " + startupUi.Diagnostic);
                        if (args.Any(a => string.Equals(a, "--game-ui-preview", StringComparison.OrdinalIgnoreCase)))
                            MessageBox.Show(startupUi.Diagnostic, "Game UI preview unavailable", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    try
                    {
                        DXSoundManager.Create();
                        DXControl.ActiveScene = new LoginScene(Config.IntroSceneSize);
                        MessagePump.Run(CEnvir.Target, CEnvir.GameLoop);

                        ConfigReader.Save();
                        CEnvir.Session?.Save(true);
                    }
                    finally
                    {
                        // Stop scene consumers before the application package lease
                        // leaves its using scope; D3D remains alive for texture release.
                        try { DXControl.ActiveScene?.Dispose(); }
                        finally
                        {
                            try { CEnvir.Unload(); }
                            finally { CEnvir.GameUiSession = null; }
                        }
                    }
                }
            }
            finally
            {
                try { DXManager.Unload(); }
                finally { DXSoundManager.Unload(); }
            }
        }
    }
}
