using UiPreview;

int captureArgumentIndex = Array.FindIndex(
    args,
    argument => argument.Equals("--capture", StringComparison.OrdinalIgnoreCase));

if (captureArgumentIndex >= 0 &&
    (captureArgumentIndex + 1 >= args.Length || string.IsNullOrWhiteSpace(args[captureArgumentIndex + 1])))
{
    Console.Error.WriteLine("Usage: UiPreview --capture <output.png>");
    Environment.ExitCode = 2;
    return;
}

string? capturePath = captureArgumentIndex >= 0
    ? Path.GetFullPath(args[captureArgumentIndex + 1])
    : null;

using HudPreviewGame game = new(capturePath);
game.Run();
