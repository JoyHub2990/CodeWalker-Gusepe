using System;
using System.IO;
using System.Threading;

namespace CodeWalker.World
{
    /// <summary>Publish an already-written package without discarding it when Windows holds its directory open.</summary>
    public static class ZonePackageFinalizer
    {
        // A reader that does not allow delete sharing can temporarily prevent a directory rename.
        // The export worker waits here; the UI and cancellation remain available.
        public static bool TryRename(string stage, string destination, CancellationToken token, Action<string> progress = null)
        {
            const int attempts = 12;
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                token.ThrowIfCancellationRequested();
                if (Directory.Exists(destination) || File.Exists(destination))
                    throw new IOException("The destination already exists and will not be overwritten: " + destination);
                try
                {
                    Directory.Move(stage, destination);
                    return true;
                }
                catch (Exception ex) when (IsRenameBlocked(ex) && Directory.Exists(stage)
                    && !Directory.Exists(destination) && !File.Exists(destination))
                {
                    progress?.Invoke("Files exported. Waiting for Windows to release the output folder (" + (attempt + 1) + "/" + attempts + ")…");
                    if (attempt + 1 == attempts) break;
                    if (token.WaitHandle.WaitOne(250)) token.ThrowIfCancellationRequested();
                }
            }
            token.ThrowIfCancellationRequested();
            return false; // The complete package is still accessible at stage.
        }

        private static bool IsRenameBlocked(Exception ex)
        {
            if (!(ex is IOException) && !(ex is UnauthorizedAccessException)) return false;
            int code = ex.HResult & 0xffff;
            return code == 5 || code == 32 || code == 33; // Access denied, sharing violation, lock violation.
        }
    }
}
