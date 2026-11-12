using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace AstralOath.Shared.Updates
{
    internal static class ContentDirectoryRollback
    {
        // Recovery must outlive the cancelled installation, but must not wait indefinitely for a lock.
        internal static async Task MoveAsync(string source, string destination)
        {
            var elapsed = Stopwatch.StartNew();
            while (true)
            {
                try
                {
                    Directory.Move(source, destination);
                    return;
                }
                catch (Exception error) when (IsTemporaryWindowsError(error))
                {
                    double remaining = 1000d - elapsed.Elapsed.TotalMilliseconds;
                    if (remaining <= 0d) throw;
                    await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(50d, remaining))).ConfigureAwait(false);
                }
            }
        }

        private static bool IsTemporaryWindowsError(Exception error)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
                !(error is IOException || error is UnauthorizedAccessException))
                return false;
            int code = error.HResult & 0xffff;
            return code == 5 || code == 32 || code == 33;
        }
    }
}