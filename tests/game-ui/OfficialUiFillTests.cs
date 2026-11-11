using System;
using System.Drawing;
using Client.Models.GameUi;

internal static class OfficialUiFillTests
{
    private static int checks;
    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception(message);
    }
    private static void Reject(Action action, string message)
    {
        try { action(); }
        catch (ArgumentOutOfRangeException) { checks++; return; }
        throw new Exception(message);
    }
    private static void Case(long current, long maximum, int expectedHeight)
    {
        var source = new Rectangle(11, 23, 53, 101);
        var destination = new Point(39, 668);
        var result = GameUiFillGeometry.Bottom(source, destination, current, maximum);
        Check(result.IsEmpty == (expectedHeight == 0), "Empty flag");
        if (expectedHeight == 0)
        {
            Check(result.Source == Rectangle.Empty && result.Destination == Rectangle.Empty, "Zero fill must draw nothing");
            return;
        }
        Check(result.Source == new Rectangle(11, 124 - expectedHeight, 53, expectedHeight), "Bottom source crop");
        Check(result.Destination == new Rectangle(39, 769 - expectedHeight, 53, expectedHeight), "Bottom destination crop");
        Check(result.Source.Size == result.Destination.Size, "No scaling");
    }
    private static void Run()
    {
        Case(0, 100, 0); Case(-1, 100, 0); Case(long.MinValue, 100, 0);
        Case(50, 0, 0); Case(50, -1, 0); Case(50, long.MinValue, 0);
        Case(1, 100, 1); Case(1, 101, 1); Case(1, 102, 0);
        Case(50, 100, 50); Case(99, 100, 99); Case(100, 100, 101); Case(101, 100, 101);
        Case(long.MaxValue, long.MaxValue, 101); Case(long.MaxValue - 1, long.MaxValue, 100);
        Case(long.MaxValue / 2, long.MaxValue, 50); Case(long.MaxValue, 1, 101);
        foreach (int height in new[] { 1, 2, 44, 101, 768, 1080, int.MaxValue })
        {
            int previous = 0;
            for (int n = 0; n <= 1000; n++)
            {
                var region = GameUiFillGeometry.Bottom(new Rectangle(0, 0, 48, height), new Point(-100, 0), n, 1000);
                int expected = (int)((long)height * n / 1000);
                Check(region.Source.Height == expected, "Exact floor across range");
                Check(region.Source.Height >= previous, "Monotonic fill");
                Check(region.Source.Height <= height, "Fill bounded by source");
                if (!region.IsEmpty)
                {
                    Check(region.Source.Bottom == height && region.Destination.Bottom == height, "Stable bottom edge");
                    Check(region.Destination.X == -100 && region.Source.X == 0, "Draw position independent of source");
                }
                previous = region.Source.Height;
            }
        }
        var huge = GameUiFillGeometry.Bottom(new Rectangle(0, 0, 1, int.MaxValue), Point.Empty, long.MaxValue - 1, long.MaxValue);
        Check(huge.Source.Height == int.MaxValue - 1 && huge.Source.Bottom == int.MaxValue, "Large integer precision");
        Reject(() => GameUiFillGeometry.Bottom(new Rectangle(-1, 0, 1, 1), Point.Empty, 1, 1), "Negative source");
        Reject(() => GameUiFillGeometry.Bottom(new Rectangle(0, 0, 0, 1), Point.Empty, 1, 1), "Empty source");
        Reject(() => GameUiFillGeometry.Bottom(new Rectangle(int.MaxValue, 0, 1, 1), Point.Empty, 1, 1), "Source right overflow");
        Reject(() => GameUiFillGeometry.Bottom(new Rectangle(0, int.MaxValue, 1, 1), Point.Empty, 1, 1), "Source bottom overflow");
        Reject(() => GameUiFillGeometry.Bottom(new Rectangle(0, 0, 1, 1), new Point(int.MaxValue, 0), 1, 1), "Target right overflow");
        Reject(() => GameUiFillGeometry.Bottom(new Rectangle(0, 0, 1, 1), new Point(0, int.MaxValue), 1, 1), "Target bottom overflow");
    }
    private static int Main()
    {
        try { Run(); Console.WriteLine("Official UI fill geometry passed: " + checks + " checks."); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
