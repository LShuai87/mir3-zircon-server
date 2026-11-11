// Test-only host for the real catalog and real CPU package preflight.
// No Program, game scene, network or usable GPU path exists in this assembly.
using System;
using System.Drawing;
[assembly: System.Reflection.AssemblyDescription("Isolated OfficialV1 deployment test fixture")]
namespace Client.Controls
{
    public sealed class ButtonImageStates
    {
        public int Normal { get; }
        public int Hover { get; }
        public int Pressed { get; }
        public int Disabled { get; }
        public Color NormalTint { get; }
        public Color HoverTint { get; }
        public Color PressedTint { get; }
        public Color DisabledTint { get; }
        public ButtonImageStates(int normal, int hover, int pressed, int disabled,
            Color? normalTint = null, Color? hoverTint = null, Color? pressedTint = null, Color? disabledTint = null)
        {
            Normal = normal; Hover = hover; Pressed = pressed; Disabled = disabled;
            NormalTint = normalTint ?? Color.White; HoverTint = hoverTint ?? Color.White;
            PressedTint = pressedTint ?? Color.White; DisabledTint = disabledTint ?? Color.White;
        }
    }
}
namespace Client.Envir
{
    public enum ImageType { Image }
    public static class DXManager
    {
        public static SlimDX.Direct3D9.Device Device => throw new InvalidOperationException("Fixture must never initialize GPU.");
    }
    public sealed class MirImage
    {
        public bool ImageValid => throw new InvalidOperationException("Fixture has no GPU.");
        public SlimDX.Direct3D9.Texture Image => throw new InvalidOperationException("Fixture has no GPU.");
        public void DisposeTexture() { throw new InvalidOperationException("Fixture has no GPU."); }
    }
    public sealed class MirLibrary : IDisposable
    {
        public MirImage[] Images;
        public MirLibrary(string path) { throw new InvalidOperationException("Fixture has no GPU."); }
        public void ReadLibrary() { throw new InvalidOperationException("Fixture has no GPU."); }
        public MirImage CreateImage(int index, ImageType type) { throw new InvalidOperationException("Fixture has no GPU."); }
        public void Dispose() { throw new InvalidOperationException("Fixture has no GPU."); }
    }
}
