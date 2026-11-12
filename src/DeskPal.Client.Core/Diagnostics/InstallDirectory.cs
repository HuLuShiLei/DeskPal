namespace DeskPal.Client.Core.Diagnostics;

/// <summary>
/// The Velopack install root (the parent of current\) survives updates, so logs and the WebView2 cache live there.
/// Uninstalling, or running the installer again over the same folder, deletes the whole root.
/// </summary>
public static class InstallDirectory
{
    /// <summary>Returns the root when it can be written to, otherwise null so callers fall back to a per-user folder.</summary>
    public static string? Writable(string? root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return null;
        }

        try
        {
            Directory.CreateDirectory(root);
            var probe = Path.Combine(root, $".write-test-{Guid.NewGuid():N}");
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
            return root;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}