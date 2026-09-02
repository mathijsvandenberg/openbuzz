namespace OpenBuzz.Cli;

using System.Text.Json;
using OpenBuzz.Graphics;

/// Reads StudioCameras.rp2, the file that holds every camera in the show.
internal static class CameraCommands
{
    private const string File = "StudioCameras.rp2";

    public static int List(string inDir)
    {
        var cameras = Load(inDir, out string path);
        if (cameras is null) return 1;

        Console.WriteLine($"{path}: {cameras.Count} cameras");
        foreach (var c in cameras.OrderBy(c => c.Name, StringComparer.Ordinal))
            Console.WriteLine(
                $"  {c.Name,-30} pos({c.Position[0],9:0.##} {c.Position[1],8:0.##} {c.Position[2],9:0.##})  " +
                $"fwd({c.Forward[0],6:0.###} {c.Forward[1],6:0.###} {c.Forward[2],6:0.###})  " +
                $"fov {c.FovHorizontalDegrees,5:0.##}h/{c.FovVerticalDegrees,5:0.##}v  " +
                $"near {c.Near,8:0.##} far {c.Far,10:0.##}");

        return 0;
    }

    public static int Export(string inDir, string outPath)
    {
        var cameras = Load(inDir, out _);
        if (cameras is null) return 1;

        var payload = cameras.ToDictionary(c => c.Name, c => new
        {
            position = c.Position,
            forward = c.Forward,
            up = c.Up,
            right = c.Right,
            target = c.Target,
            viewWindow = c.ViewWindow,
            viewOffset = c.ViewOffset,
            fovHorizontal = Math.Round(c.FovHorizontalDegrees, 4),
            fovVertical = Math.Round(c.FovVerticalDegrees, 4),
            aspect = Math.Round(c.Aspect, 4),
            near = c.Near,
            far = c.Far,
            projection = c.Projection,
        });

        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        System.IO.File.WriteAllText(outPath,
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));

        Console.WriteLine($"Wrote {cameras.Count} cameras to {outPath}");
        return 0;
    }

    private static List<RwCameraView>? Load(string inDir, out string path)
    {
        path = Path.Combine(inDir, File);
        if (!System.IO.File.Exists(path))
        {
            Console.Error.WriteLine($"{path} not found");
            return null;
        }
        return RwCameraSet.Parse(System.IO.File.ReadAllBytes(path));
    }
}
