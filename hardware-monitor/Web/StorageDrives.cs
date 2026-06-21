namespace hardware_monitor.Web;

/// <summary>
/// Resolves the configured drive list (from appsettings) into ready <see cref="DriveInfo"/>s.
/// Accepts loose inputs like "C", "C:", "C:\". Falls back to the OS system drive when nothing
/// valid is configured.
/// </summary>
public static class StorageDrives
{
    public static List<DriveInfo> Resolve(IEnumerable<string>? configured)
    {
        var result = new List<DriveInfo>();

        if (configured is not null)
        {
            foreach (var raw in configured)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string letter = raw.Trim().TrimEnd('\\', '/', ':');
                if (letter.Length == 0) continue;
                try
                {
                    var info = new DriveInfo(letter);
                    if (info.IsReady && !result.Any(d => d.Name == info.Name))
                    {
                        result.Add(info);
                    }
                }
                catch
                {
                    // Skip invalid/unmounted drive specs rather than failing startup.
                }
            }
        }

        if (result.Count == 0)
        {
            // Default: the drive Windows is installed on (typically C:).
            string systemRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            result.Add(new DriveInfo(systemRoot));
        }

        return result;
    }

    /// <summary>The bare drive letter for naming/labels, e.g. "C:\" -&gt; "C".</summary>
    public static string Letter(DriveInfo drive) => drive.Name.TrimEnd('\\', '/', ':').ToUpperInvariant();
}
