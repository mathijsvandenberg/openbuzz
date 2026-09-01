using OpenBuzz.Lamps;

// The wired Buzz buzzers: Sony vendor, buzzer product.
const ushort VendorId = 0x054C;
const ushort ProductId = 0x1000;

if (args.Contains("--help"))
{
    Console.WriteLine("""
        obz-lamps - lights the red lamps on the Buzz buzzers

          obz-lamps --set 1010     lamps 1 and 3 on, 2 and 4 off
          obz-lamps --serve        read a 4-digit pattern per line from stdin
          obz-lamps --probe        report what the device says about itself

        The engine cannot write HID output reports, so it drives this instead.
        """);
    return 0;
}

using var device = HidDevice.Open(VendorId, ProductId);
if (device is null)
{
    Console.Error.WriteLine($"No HID device {VendorId:X4}:{ProductId:X4} present.");
    return 1;
}

if (args.Contains("--probe"))
{
    Console.WriteLine($"path   : {device.Path}");
    Console.WriteLine($"output report length: {device.OutputReportLength}");
    return 0;
}

if (args.Contains("--serve"))
{
    // One pattern per line, so a long-running caller can keep the handle open
    // rather than paying for a process per change.
    Console.WriteLine("ready");
    for (string? line = Console.ReadLine(); line is not null; line = Console.ReadLine())
    {
        var trimmed = line.Trim();
        if (trimmed is "quit" or "exit") break;
        if (trimmed.Length == 0) continue;

        Console.WriteLine(Send(device, trimmed) ? "ok" : "fail");
    }
    return 0;
}

int at = Array.IndexOf(args, "--set");
var pattern = at >= 0 && at + 1 < args.Length ? args[at + 1] : "1111";

if (!Send(device, pattern))
{
    Console.Error.WriteLine("Write failed.");
    return 1;
}

Console.WriteLine($"set {pattern}");
return 0;

/// <summary>
/// Builds and sends the lamp report.
///
/// The layout is the one the Linux hid-sony driver uses for these buzzers: a
/// leading zero, then one byte per lamp, 0xFF for lit. The report is padded to
/// whatever length the device declares, because a report of the wrong size is
/// rejected outright rather than truncated.
/// </summary>
static bool Send(HidDevice device, string pattern)
{
    int length = device.OutputReportLength > 0 ? device.OutputReportLength : 8;
    var report = new byte[length];

    // report[0] is the report id, which is zero here.
    for (int lamp = 0; lamp < 4; lamp++)
    {
        bool on = lamp < pattern.Length && pattern[lamp] is not ('0' or 'o' or 'O');
        int index = 2 + lamp;
        if (index < report.Length) report[index] = on ? (byte)0xFF : (byte)0x00;
    }

    return device.Write(report);
}
