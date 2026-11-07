using System;
using System.Globalization;
using System.Windows.Forms;

namespace Client.Models
{
    // Keeps HUD-only Escape suppression separate from combat targeting.
    internal sealed class MonsterHudSelection<T> where T : class
    {
        private bool suppressed;
        private T suppressedHover, suppressedFocus;

        public T Select(T hover, T focus, Predicate<T> valid)
        {
            if (suppressed)
            {
                if (ReferenceEquals(hover, suppressedHover) && ReferenceEquals(focus, suppressedFocus))
                    return null;
                Reset();
            }
            return hover != null && valid(hover) ? hover : focus != null && valid(focus) ? focus : null;
        }

        public void Suppress(T hover, T focus)
        {
            suppressed = true;
            suppressedHover = hover;
            suppressedFocus = focus;
        }

        public void Reset()
        {
            suppressed = false;
            suppressedHover = suppressedFocus = null;
        }
    }

    internal sealed class MonsterHudDetailHold
    {
        private Keys keyData;
        public bool Active => keyData != Keys.None;

        public void Begin(Keys value) { keyData = Normalize(value); }
        public void Clear() { keyData = Keys.None; }
        public void Update(Keys value, bool released)
        {
            value = Normalize(value);
            if ((released && (value & Keys.KeyCode) == (keyData & Keys.KeyCode)) ||
                (value & Keys.Modifiers) != (keyData & Keys.Modifiers))
                Clear();
        }

        private static Keys Normalize(Keys value)
        {
            Keys key = value & Keys.KeyCode;
            if (key >= Keys.NumPad0 && key <= Keys.NumPad9)
                key = Keys.D0 + (key - Keys.NumPad0);
            return key | (value & Keys.Modifiers);
        }
    }

    internal static class MonsterHudPresentation
    {
        public static int FillWidth(int health, int maxHealth, int width)
        {
            if (health <= 0 || maxHealth <= 0 || width <= 0) return 0;
            return Math.Max(1, (int)((long)Math.Min(health, maxHealth) * width / maxHealth));
        }

        public static string Details(bool boss, int health, int maxHealth, bool showHealth, bool showPercentage)
        {
            string result = boss ? "首领" : string.Empty;
            if (maxHealth <= 0 || health <= 0) return result;
            if (showHealth)
                Append(ref result, Math.Min(health, maxHealth).ToString("N0", CultureInfo.InvariantCulture) + " / " + maxHealth.ToString("N0", CultureInfo.InvariantCulture));
            if (showPercentage)
            {
                double percent = (double)Math.Min(health, maxHealth) * 100 / maxHealth;
                string text = percent < 0.1 ? "<0.1%" :
                    (health >= maxHealth ? 100 : Math.Min(99.9, Math.Round(percent, 1, MidpointRounding.AwayFromZero))).ToString("F1", CultureInfo.InvariantCulture) + "%";
                Append(ref result, text);
            }
            return result;
        }

        private static void Append(ref string result, string value)
        {
            result = result.Length == 0 ? value : result + " · " + value;
        }
    }
}
