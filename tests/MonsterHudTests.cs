using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows.Forms;
using Client.Controls;
using Client.Envir;
using Client.Models;
using Client.Scenes;
using Client.Scenes.Views;
using Library;
using Library.SystemModels;

internal static class MonsterHudTests
{
    private static int checks;
    private static void Equal<T>(T expected, T actual, string reason)
    {
        checks++;
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception(reason + ": expected [" + expected + "], actual [" + actual + "]");
    }
    private static T Empty<T>() { return (T)FormatterServices.GetUninitializedObject(typeof(T)); }
    private static void Field(object target, string name, object value)
    {
        for (Type type = target.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
            if (field == null) continue;
            field.SetValue(target, value);
            return;
        }
        throw new Exception("Missing field " + name);
    }
    private static IEnumerable<DXControl> Tree(DXControl root)
    {
        yield return root;
        var children = (IEnumerable<DXControl>)typeof(DXControl).GetProperty("Controls", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(root);
        foreach (DXControl child in children)
            foreach (DXControl nested in Tree(child)) yield return nested;
    }

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Formatting(); Selection(); Hold(); ActualHud(args.Length > 0 ? args[0] : null);
            Console.WriteLine("Monster HUD: " + checks + " checks passed (production formatting, input state and real DX controls).");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { GameScene.Game = null; }
    }

    private static void Formatting()
    {
        Equal("", MonsterHudPresentation.Details(false, 12450, 18000, false, false), "default normal");
        Equal("首领", MonsterHudPresentation.Details(true, 12450, 18000, false, false), "default boss");
        Equal("12,450 / 18,000", MonsterHudPresentation.Details(false, 12450, 18000, true, false), "health only");
        Equal("69.2%", MonsterHudPresentation.Details(false, 12450, 18000, false, true), "percentage only");
        Equal("首领 · 12,450 / 18,000 · 69.2%", MonsterHudPresentation.Details(true, 12450, 18000, true, true), "combined details");
        Equal("<0.1%", MonsterHudPresentation.Details(false, 1, 18000, false, true), "tiny positive health");
        Equal("0.1%", MonsterHudPresentation.Details(false, 1, 1000, false, true), "percentage threshold");
        Equal("99.9%", MonsterHudPresentation.Details(false, 99999, 100000, false, true), "not full never rounds to full");
        Equal("100.0%", MonsterHudPresentation.Details(false, 100000, 100000, false, true), "full");
        Equal("首领", MonsterHudPresentation.Details(true, 500, 0, true, true), "unknown maximum");
        Equal("", MonsterHudPresentation.Details(false, 0, 100, true, true), "dead details");
        Equal("100 / 100 · 100.0%", MonsterHudPresentation.Details(false, 150, 100, true, true), "transient health clamped");
        Equal(1, MonsterHudPresentation.FillWidth(1, int.MaxValue, 358), "positive minimum pixel");
        Equal(358, MonsterHudPresentation.FillWidth(int.MaxValue, int.MaxValue, 358), "overflow safe fill");
        Equal(358, MonsterHudPresentation.FillWidth(110, 100, 358), "overfull clamped fill");
        Equal(179, MonsterHudPresentation.FillWidth(50, 100, 358), "half fill");
        Equal(0, MonsterHudPresentation.FillWidth(0, 100, 358), "dead fill");
        Equal(0, MonsterHudPresentation.FillWidth(50, 0, 358), "unknown fill");
        Equal(0, MonsterHudPresentation.FillWidth(50, 100, 0), "zero width");
    }

    private static void Selection()
    {
        object hover = new object(), focus = new object(), invalid = new object();
        var selection = new MonsterHudSelection<object>();
        Predicate<object> valid = candidate => candidate != invalid;
        Equal(hover, selection.Select(hover, focus, valid), "hover wins");
        Equal(focus, selection.Select(invalid, focus, valid), "invalid hover falls back");
        Equal(focus, selection.Select(null, focus, valid), "clicked focus remains");
        Equal<object>(null, selection.Select(invalid, invalid, valid), "no stale target");
        selection.Suppress(hover, focus);
        for (int i = 0; i < 3; i++) Equal<object>(null, selection.Select(hover, focus, valid), "Escape remains hidden");
        Equal(focus, selection.Select(null, focus, valid), "cursor leaves resumes");
        Equal(hover, selection.Select(hover, focus, valid), "cursor returns");
        selection.Suppress(hover, focus);
        selection.Reset();
        Equal(hover, selection.Select(hover, focus, valid), "explicit click resumes same target");
        selection.Suppress(hover, focus);
        Equal(focus, selection.Select(focus, focus, valid), "new hover resumes");
    }

    private static void Hold()
    {
        var hold = new MonsterHudDetailHold();
        Equal(false, hold.Active, "hold default");
        hold.Begin(Keys.H); Equal(true, hold.Active, "hold starts");
        hold.Update(Keys.G, true); Equal(true, hold.Active, "unrelated key release");
        hold.Update(Keys.H, true); Equal(false, hold.Active, "key release");
        hold.Begin(Keys.Control | Keys.H);
        hold.Update(Keys.ControlKey, true); Equal(false, hold.Active, "modifier released first");
        hold.Begin(Keys.H); hold.Update(Keys.Control | Keys.ControlKey, false);
        Equal(false, hold.Active, "added modifier no longer matches");
        hold.Begin(Keys.H); hold.Clear(); Equal(false, hold.Active, "focus/chat/modal clears");
        hold.Begin(Keys.NumPad1); hold.Update(Keys.D1, true);
        Equal(false, hold.Active, "numeric bind normalization");
        hold.Begin(Keys.Shift | Keys.F2); hold.Update(Keys.Shift | Keys.F2, true);
        Equal(false, hold.Active, "secondary/custom chord release");
    }

    private static void ActualHud(string renderPath)
    {
        // Isolate world data without starting a connection, map renderer or live game scene.
        var scene = Empty<GameScene>();
        scene.MapControl = Empty<MapControl>();
        scene.MapControl.Objects = new List<MapObject>();
        Field(scene.MapControl, "_MapInfo", Empty<MapInfo>());
        scene.DataDictionary = new SortedDictionary<uint, ClientObjectData>();
        GameScene.Game = scene;
        var info = Empty<MonsterInfo>();
        Field(info, "_MonsterName", "赤月恶魔");
        Field(info, "_IsBoss", true);
        var monster = Empty<MonsterObject>();
        monster.MonsterInfo = info;
        monster.ObjectID = 1;
        monster.Visible = true;
        scene.MapControl.Objects.Add(monster);
        var data = new ClientObjectData { ObjectID = 1, Health = 12450, MaxHealth = 18000 };
        scene.DataDictionary[1] = data;
        Config.MonsterBoxVisible = true;
        Config.MonsterBoxShowHealth = Config.MonsterBoxShowPercentage = false;
        using (var hud = new MonsterDialog())
        {
            scene.MonsterBox = hud;
            hud.LayoutForViewport(1024);
            Equal(new Point(332, 24), hud.Location, "centered top anchor");
            Equal(new Size(360, 52), hud.Size, "logical HUD bounds");
            hud.UpdateTarget(monster, null);
            Equal(monster, hud.Monster, "real HUD accepts valid monster");
            hud.UpdateTarget(Empty<MonsterObject>(), monster);
            Equal(monster, hud.Monster, "real HUD falls back from invalid hover");
            Equal(true, hud.Visible, "real HUD visible");
            var labels = Tree(hud).OfType<DXLabel>().ToList();
            Equal(2, labels.Count, "only name and secondary line");
            Equal("赤月恶魔", labels[0].Text, "real name label");
            Equal("首领", labels[1].Text, "real boss label");
            Equal(Color.FromArgb(0xD7, 0xBC, 0x80), labels[0].ForeColour, "boss gold");
            Equal(true, Tree(hud).All(control => !control.IsControl && !control.Movable), "all descendants passive");
            Config.MonsterBoxShowHealth = true; hud.RefreshHealth();
            Equal("首领 · 12,450 / 18,000", labels[1].Text, "health setting applied");
            Config.MonsterBoxShowHealth = false; Config.MonsterBoxShowPercentage = true; hud.RefreshHealth();
            Equal("首领 · 69.2%", labels[1].Text, "percentage setting applied");
            hud.BeginDetails(Keys.H);
            Equal("首领 · 12,450 / 18,000 · 69.2%", labels[1].Text, "temporary detail override");
            hud.UpdateDetailKey(Keys.H, true);
            Equal("首领 · 69.2%", labels[1].Text, "release restores configured flags");
            hud.LayoutForViewport(360);
            Equal(new Point(24, 24), hud.Location, "narrow viewport margin");
            Equal(312, hud.Size.Width, "narrow viewport width");
            hud.LayoutForViewport(40); Equal(false, hud.Visible, "extreme width hides HUD");
            hud.LayoutForViewport(1024);
            hud.Suppress(monster, monster); hud.UpdateTarget(monster, monster);
            Equal(false, hud.Visible, "actual HUD Escape suppression");
            hud.Resume(); hud.UpdateTarget(monster, monster); Equal(true, hud.Visible, "actual HUD click recovery");
            data.Health = 0; hud.RefreshHealth(); Equal(false, hud.Visible, "zero HP clears immediately");
            data.Health = 1; hud.UpdateTarget(monster, null); Equal(true, hud.Visible, "positive HP can return");
            Equal("首领 · <0.1%", labels[1].Text, "actual tiny percentage");
            data.Dead = true; hud.UpdateTarget(monster, null); Equal(false, hud.Visible, "data death invalidates");
            data.Dead = false; Field(monster, "_Dead", true); hud.UpdateTarget(monster, null);
            Equal(false, hud.Visible, "world death invalidates");
            Field(monster, "_Dead", false); Field(monster, "_PetOwner", "主人"); hud.UpdateTarget(monster, null);
            Equal(false, hud.Visible, "owned pet excluded");
            Field(monster, "_PetOwner", null); monster.CompanionObject = Empty<ClientCompanionObject>(); hud.UpdateTarget(monster, null);
            Equal(false, hud.Visible, "companion excluded");
            monster.CompanionObject = null; Field(info, "_AI", -1); hud.UpdateTarget(monster, null);
            Equal(false, hud.Visible, "guard excluded");
            Field(info, "_AI", 0); monster.Visible = false; hud.UpdateTarget(monster, null);
            Equal(false, hud.Visible, "invisible excluded");
            monster.Visible = true; data.MaxHealth = 0; hud.UpdateTarget(monster, null);
            Equal(false, hud.Visible, "unknown max excluded");
            data.MaxHealth = 18000; data.MapIndex = 9; hud.UpdateTarget(monster, null);
            Equal(false, hud.Visible, "old map data excluded");
            data.MapIndex = 0; scene.MapControl.Objects.Clear(); hud.UpdateTarget(monster, null);
            Equal(false, hud.Visible, "removed world object excluded");
            scene.MapControl.Objects.Add(monster); scene.DataDictionary.Clear(); hud.UpdateTarget(monster, null);
            Equal(false, hud.Visible, "removed health data excluded");
            scene.DataDictionary[1] = data; Config.MonsterBoxVisible = false; hud.UpdateTarget(monster, null);
            Equal(false, hud.Visible, "master setting hides");
            Config.MonsterBoxVisible = true; Field(info, "_IsBoss", false); hud.UpdateTarget(monster, null);
            Equal(Color.FromArgb(0xE7, 0xDD, 0xCA), labels[0].ForeColour, "normal warm white");
            Config.MonsterBoxShowPercentage = false; hud.RefreshHealth();
            Equal(false, labels[1].Visible, "ordinary default has no extra line");
            hud.Clear(); Equal<MonsterObject>(null, hud.Monster, "scene cleanup forgets monster");
            if (renderPath != null) Render(hud, scene, monster, data, info, renderPath);
        }
    }
    private static void DxProperty(string name, object value)
    {
        typeof(DXManager).GetProperty(name).SetValue(null, value, null);
    }

    private static void Render(MonsterDialog hud, GameScene scene, MonsterObject monster,
        ClientObjectData data, MonsterInfo info, string path)
    {
        // Render production controls and the real shared artwork against fixture data; no live scene startup.
        using (var host = new Form { ClientSize = new Size(800, 400), ShowInTaskbar = false })
        using (var d3d = new SlimDX.Direct3D9.Direct3D())
        {
            var parameters = new SlimDX.Direct3D9.PresentParameters
            {
                Windowed = true, SwapEffect = SlimDX.Direct3D9.SwapEffect.Discard,
                BackBufferFormat = SlimDX.Direct3D9.Format.X8R8G8B8, BackBufferWidth = 800, BackBufferHeight = 400,
                PresentFlags = SlimDX.Direct3D9.PresentFlags.LockableBackBuffer
            };
            using (var device = new SlimDX.Direct3D9.Device(d3d, 0, SlimDX.Direct3D9.DeviceType.Hardware, host.Handle,
                SlimDX.Direct3D9.CreateFlags.SoftwareVertexProcessing, parameters))
            using (var root = new DXControl { Size = new Size(800, 400) })
            {
                DxProperty("Device", device);
                DxProperty("Parameters", parameters);
                typeof(DXManager).GetMethod("LoadTextures", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
                Field(root, "_IsVisible", true);
                Field(scene, "_DisplayArea", new Rectangle(0, 0, 800, 400));
                typeof(DXControl).GetField("_ActiveScene", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, scene);
                // The isolated process owns this library until exit (legacy Dispose assumes no empty image slots).
                var artwork = new MirLibrary(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GameInter.Zl"));
                artwork.ReadLibrary();
                CEnvir.LibraryList[LibraryFile.GameInter] = artwork;
                Equal(new Size(456, 32), artwork.GetSize(327), "original HUD ornament resource");
                hud.Parent = root;
                hud.LayoutForViewport(800);
                device.Clear(SlimDX.Direct3D9.ClearFlags.Target, Color.FromArgb(18, 20, 23), 1, 0);
                device.BeginScene();
                DXManager.Sprite.Begin(SlimDX.Direct3D9.SpriteFlags.AlphaBlend);
                for (int row = 0; row < 4; row++)
                {
                    Field(info, "_IsBoss", row != 0);
                    Field(info, "_MonsterName", row == 0 ? "骷髅战士" : "赤月恶魔");
                    data.Health = row == 0 ? data.MaxHealth : row == 3 ? 1 : 12450;
                    Config.MonsterBoxShowHealth = row == 2;
                    Config.MonsterBoxShowPercentage = row >= 2;
                    hud.UpdateTarget(monster, null);
                    hud.Location = new Point(220, 30 + row * 95);
                    hud.Draw();
                    DXManager.Sprite.Flush();
                }
                DXManager.Sprite.End();
                device.EndScene();
                SlimDX.Direct3D9.Surface.ToFile(DXManager.MainSurface, path, SlimDX.Direct3D9.ImageFileFormat.Png);
                hud.Parent = null;
                CEnvir.LibraryList.Remove(LibraryFile.GameInter);
                Console.WriteLine("Rendered production SlimDX HUD preview: " + path);
            }
        }
    }

}
