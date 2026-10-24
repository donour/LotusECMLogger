namespace LotusECMLogger.Controls;

/// <summary>
/// Blends between the min and max colours in HSB space, like RomRaider's ColorScaler
/// (which uses java.awt.Color's HSB model).
/// </summary>
public static class ColorScaler
{
    public static Color GetScaledColor(double scale, Color min, Color max)
    {
        if (double.IsNaN(scale))
            return max;
        if (scale <= 0)
            return min;
        if (scale >= 1)
            return max;

        var (h1, s1, b1) = RgbToHsb(min);
        var (h2, s2, b2) = RgbToHsb(max);
        float f = (float)scale;
        return HsbToRgb(h1 + (h2 - h1) * f, s1 + (s2 - s1) * f, b1 + (b2 - b1) * f);
    }

    /// <summary>Same algorithm as java.awt.Color.RGBtoHSB.</summary>
    private static (float h, float s, float b) RgbToHsb(Color c)
    {
        int r = c.R, g = c.G, bl = c.B;
        int cmax = Math.Max(r, Math.Max(g, bl));
        int cmin = Math.Min(r, Math.Min(g, bl));
        float brightness = cmax / 255f;
        float saturation = cmax != 0 ? (float)(cmax - cmin) / cmax : 0;
        float hue;
        if (saturation == 0)
        {
            hue = 0;
        }
        else
        {
            float redc = (float)(cmax - r) / (cmax - cmin);
            float greenc = (float)(cmax - g) / (cmax - cmin);
            float bluec = (float)(cmax - bl) / (cmax - cmin);
            if (r == cmax)
                hue = bluec - greenc;
            else if (g == cmax)
                hue = 2.0f + redc - bluec;
            else
                hue = 4.0f + greenc - redc;
            hue /= 6.0f;
            if (hue < 0)
                hue += 1.0f;
        }
        return (hue, saturation, brightness);
    }

    /// <summary>Same algorithm as java.awt.Color.HSBtoRGB.</summary>
    private static Color HsbToRgb(float hue, float saturation, float brightness)
    {
        int r = 0, g = 0, b = 0;
        if (saturation == 0)
        {
            r = g = b = (int)(brightness * 255.0f + 0.5f);
        }
        else
        {
            float h = (hue - (float)Math.Floor(hue)) * 6.0f;
            float f = h - (float)Math.Floor(h);
            float p = brightness * (1.0f - saturation);
            float q = brightness * (1.0f - saturation * f);
            float t = brightness * (1.0f - (saturation * (1.0f - f)));
            switch ((int)h)
            {
                case 0: r = (int)(brightness * 255.0f + 0.5f); g = (int)(t * 255.0f + 0.5f); b = (int)(p * 255.0f + 0.5f); break;
                case 1: r = (int)(q * 255.0f + 0.5f); g = (int)(brightness * 255.0f + 0.5f); b = (int)(p * 255.0f + 0.5f); break;
                case 2: r = (int)(p * 255.0f + 0.5f); g = (int)(brightness * 255.0f + 0.5f); b = (int)(t * 255.0f + 0.5f); break;
                case 3: r = (int)(p * 255.0f + 0.5f); g = (int)(q * 255.0f + 0.5f); b = (int)(brightness * 255.0f + 0.5f); break;
                case 4: r = (int)(t * 255.0f + 0.5f); g = (int)(p * 255.0f + 0.5f); b = (int)(brightness * 255.0f + 0.5f); break;
                case 5: r = (int)(brightness * 255.0f + 0.5f); g = (int)(p * 255.0f + 0.5f); b = (int)(q * 255.0f + 0.5f); break;
            }
        }
        return Color.FromArgb(Math.Clamp(r, 0, 255), Math.Clamp(g, 0, 255), Math.Clamp(b, 0, 255));
    }

    /// <summary>Black or white, whichever reads better on <paramref name="background"/>.</summary>
    public static Color ContrastText(Color background)
    {
        double luminance = (0.299 * background.R + 0.587 * background.G + 0.114 * background.B) / 255;
        return luminance > 0.5 ? Color.Black : Color.White;
    }
}
