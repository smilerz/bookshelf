using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Download.TrackedDownloads;

namespace NzbDrone.Core.MediaFiles.BookImport.Manual
{
    // A file or folder chosen by path in manual import may belong to a download Bookshelf is tracking. Tying the import to that download
    // records where the file came from and lets the download be marked imported, instead of staying in the queue as Import Failed.
    public static class TrackedDownloadPathMatcher
    {
        // The tracked download whose output folder (or file) is the path or contains it. With nested matches the most specific one wins.
        public static TrackedDownload Find(IEnumerable<TrackedDownload> trackedDownloads, string path)
        {
            if (path.IsNullOrWhiteSpace())
            {
                return null;
            }

            var wanted = new OsPath(path);

            return trackedDownloads
                .Where(t => t.DownloadItem != null && !t.DownloadItem.OutputPath.IsEmpty && t.DownloadItem.OutputPath.Contains(wanted))
                .OrderByDescending(t => t.DownloadItem.OutputPath.FullPath.Length)
                .FirstOrDefault();
        }
    }
}
