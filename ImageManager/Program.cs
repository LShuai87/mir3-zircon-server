using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DevExpress.UserSkins;
using DevExpress.Skins;
using DevExpress.LookAndFeel;
using DevExpress.XtraEditors.Controls;

namespace ImageManager
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length > 0)
            {
                try
                {
                    if (args.Length != 5 || args[0] != "--official-ui")
                        throw new ArgumentException("Usage: ImageManager --official-ui <reference|prepared> <input-directory> <output-directory> <manifest-path>");
                    if (args[1] == "prepared") OfficialUiAssetBuilder.Build(args[2], args[3], args[4]);
                    else OfficialUiBuilder.Build(args[2], args[3], args[4], args[1]);
                    Console.WriteLine("Official UI " + args[1] + " conversion completed: " + Path.GetFullPath(args[3]));
                    return 0;
                }
                catch (Exception error)
                {
                    Console.Error.WriteLine(error.ToString());
                    return 1;
                }
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            BonusSkins.Register();
            SkinManager.EnableFormSkins();
            UserLookAndFeel.Default.SetSkinStyle("DevExpress Style");

            Application.Run(new IMain());
            return 0;
        }
    }
}
