using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BezelFreeCorrection.Output;

// Owns the on-disk gallery: every Apply creates a time-stamped subfolder
// containing the generated PNG(s), a meta.json with enough information
// to re-apply the same wallpaper later, and a small thumbnail used by
// the HUD. Nothing here talks to the desktop — DesktopWallpaper does.
public static class GalleryStore
{
    public static string RootDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WallpaperBezelFreeCorrection",
            "output");

    public const string MetaFile = "meta.json";
    public const string ThumbFile = "thumb.png";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string NewEntryId() => DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");

    public static string EntryFolder(string entryId) =>
        Path.Combine(RootDirectory, entryId);

    // Write the metadata JSON next to the PNGs. Called by WallpaperGenerator
    // after the files are in place.
    public static void SaveMeta(GalleryEntry entry)
    {
        var folder = EntryFolder(entry.Id);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, MetaFile);
        var json = JsonSerializer.Serialize(entry, JsonOpts);
        File.WriteAllText(path, json);
        entry.FolderPath = folder;
    }

    // Scans the output root for subfolders that look like gallery
    // entries (folder with a meta.json). Returns newest-first. Skips
    // entries that fail to parse rather than crashing the whole HUD.
    public static IReadOnlyList<GalleryEntry> List()
    {
        if (!Directory.Exists(RootDirectory)) return Array.Empty<GalleryEntry>();

        var entries = new List<GalleryEntry>();
        foreach (var folder in Directory.EnumerateDirectories(RootDirectory))
        {
            var metaPath = Path.Combine(folder, MetaFile);
            if (!File.Exists(metaPath)) continue;
            try
            {
                var json = File.ReadAllText(metaPath);
                var entry = JsonSerializer.Deserialize<GalleryEntry>(json, JsonOpts);
                if (entry == null) continue;
                entry.FolderPath = folder;
                entries.Add(entry);
            }
            catch
            {
                // Ignore corrupted entries — they simply won't show up in
                // the gallery. The user can still delete the folder manually.
            }
        }
        return entries
            .OrderByDescending(e => e.CreatedUtc)
            .ToList();
    }

    public static void Delete(GalleryEntry entry)
    {
        if (string.IsNullOrEmpty(entry.FolderPath)) return;
        if (!Directory.Exists(entry.FolderPath)) return;
        try
        {
            Directory.Delete(entry.FolderPath, recursive: true);
        }
        catch
        {
            // If something has the file open (preview, antivirus) deletion
            // can fail — better to silently no-op than blow up the HUD.
        }
    }

    public static string FullFilePath(GalleryEntry entry, GalleryFile file) =>
        Path.Combine(entry.FolderPath ?? EntryFolder(entry.Id), file.FileName);
}
