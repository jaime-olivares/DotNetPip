namespace DotNetPip;

internal readonly record struct PythonVersionSpec(int Major, int Minor)
{
    public string Dotted => $"{Major}.{Minor}";
    public string Compact => $"{Major}{Minor}";

    public static bool TryParse(string? version, out PythonVersionSpec spec)
    {
        spec = default;
        if (string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        string[] parts = version.Split('.');
        if (parts.Length < 2)
        {
            return false;
        }

        if (!int.TryParse(parts[0], out int major) || !int.TryParse(parts[1], out int minor))
        {
            return false;
        }

        spec = new PythonVersionSpec(major, minor);
        return true;
    }

    public string[] InterpreterNames() =>
    [
        $"python{Dotted}",
        $"python{Compact}",
        $"python{Major}",
    ];

    public string[] WellKnownExecutables() =>
    [
        $"/opt/homebrew/bin/python{Dotted}",
        $"/opt/homebrew/opt/python@{Dotted}/bin/python{Dotted}",
        $"/usr/local/bin/python{Dotted}",
        $"/usr/bin/python{Dotted}",
        $"/usr/bin/python{Major}",
        "/usr/bin/python3",
    ];

    public string[] StagedLibraryFileNames()
    {
        if (OperatingSystem.IsWindows())
        {
            return [$"python{Compact}.dll"];
        }

        if (OperatingSystem.IsMacOS())
        {
            return
            [
                $"python{Compact}.dylib",
                $"libpython{Compact}.dylib",
                $"libpython{Dotted}.dylib",
            ];
        }

        return
        [
            $"libpython{Dotted}.so",
            $"libpython{Dotted}.so.1.0",
            $"python{Compact}",
        ];
    }

    public string[] LibrarySearchPaths(string baseDir)
    {
        string[] nextToApp = StagedLibraryFileNames();
        for (int i = 0; i < nextToApp.Length; i++)
        {
            nextToApp[i] = Path.Combine(baseDir, nextToApp[i]);
        }

        return
        [
            .. nextToApp,
            $"/opt/homebrew/opt/python@{Dotted}/Frameworks/Python.framework/Versions/{Dotted}/lib/libpython{Dotted}.dylib",
            $"/opt/homebrew/opt/python@{Dotted}/Frameworks/Python.framework/Versions/{Dotted}/Python",
            $"/Library/Frameworks/Python.framework/Versions/{Dotted}/Python",
            $"/usr/lib/x86_64-linux-gnu/libpython{Dotted}.so.1.0",
            $"/usr/lib/aarch64-linux-gnu/libpython{Dotted}.so.1.0",
            $"python{Compact}",
            $"libpython{Dotted}.dylib",
            $"libpython{Dotted}.so.1.0",
            $"python{Compact}.dll",
        ];
    }
}
