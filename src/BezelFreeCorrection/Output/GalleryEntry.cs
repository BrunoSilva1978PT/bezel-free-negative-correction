using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BezelFreeCorrection.Output;

// Metadata persisted alongside the PNGs produced by a single Apply. The
// gallery reads these files back on start-up so the user can re-apply a
// previous configuration without regenerating the wallpaper.
public sealed class GalleryEntry
{
    // Folder name inside the output root (a timestamp — unique per Apply).
    public string Id { get; init; } = string.Empty;

    // Absolute path of the source wallpaper the user picked when this
    // entry was generated. Not required to still exist for re-apply,
    // since the generated PNGs are self-contained.
    public string SourcePath { get; init; } = string.Empty;

    // Display-friendly name, typically the source file name without the
    // extension. Shown under each gallery thumbnail.
    public string DisplayName { get; init; } = string.Empty;

    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;

    // "Surround" or "Separate" — tells the re-apply code whether to push
    // one file to the span or three files to individual monitors.
    public string Topology { get; init; } = "Surround";

    // Per-junction bezel values at the time of generation. Purely
    // informational — the PNGs already have the corrections baked in.
    public int LeftOverlap { get; init; }
    public int RightOverlap { get; init; }
    public int LeftVOffset { get; init; }
    public int RightVOffset { get; init; }

    // File references, relative to the entry folder.
    public List<GalleryFile> Files { get; init; } = new();

    [JsonIgnore]
    public string? FolderPath { get; set; }
}

// One rendered image tied to a specific monitor role, with the desktop-
// coordinate bounds that identify which physical monitor it belongs to.
// Bounds let the re-apply code match to the live IDesktopWallpaper
// device paths without requiring the monitor layout to be unchanged.
public sealed class GalleryFile
{
    public string Role { get; init; } = string.Empty; // "surround", "left", "center", "right"
    public string FileName { get; init; } = string.Empty;

    public double BoundsX { get; init; }
    public double BoundsY { get; init; }
    public double BoundsWidth { get; init; }
    public double BoundsHeight { get; init; }
}
