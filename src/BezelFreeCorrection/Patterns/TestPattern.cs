namespace BezelFreeCorrection.Patterns;

// Optional overlay drawn on top of the wallpaper to help calibrate bezel
// correction. The wallpaper itself is always rendered as the base layer
// (or a gradient fallback when none is chosen); these values only toggle
// the guide that sits above it.
public enum TestPattern
{
    None,
    HorizontalLines,
}
