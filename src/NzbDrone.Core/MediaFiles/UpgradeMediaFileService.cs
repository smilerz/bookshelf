using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books.Calibre;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.MediaFiles
{
    public interface IUpgradeMediaFiles
    {
        BookFileMoveResult UpgradeBookFile(BookFile bookFile, LocalBook localBook, bool copyOnly = false);
    }

    public class UpgradeMediaFileService : IUpgradeMediaFiles
    {
        private readonly IRecycleBinProvider _recycleBinProvider;
        private readonly IMediaFileService _mediaFileService;
        private readonly IMetadataTagService _metadataTagService;
        private readonly IMoveBookFiles _bookFileMover;
        private readonly IDiskProvider _diskProvider;
        private readonly IRootFolderService _rootFolderService;
        private readonly ICalibreProxy _calibre;
        private readonly Logger _logger;

        public UpgradeMediaFileService(IRecycleBinProvider recycleBinProvider,
                                       IMediaFileService mediaFileService,
                                       IMetadataTagService metadataTagService,
                                       IMoveBookFiles bookFileMover,
                                       IDiskProvider diskProvider,
                                       IRootFolderService rootFolderService,
                                       ICalibreProxy calibre,
                                       Logger logger)
        {
            _recycleBinProvider = recycleBinProvider;
            _mediaFileService = mediaFileService;
            _metadataTagService = metadataTagService;
            _bookFileMover = bookFileMover;
            _diskProvider = diskProvider;
            _rootFolderService = rootFolderService;
            _calibre = calibre;
            _logger = logger;
        }

        public BookFileMoveResult UpgradeBookFile(BookFile bookFile, LocalBook localBook, bool copyOnly = false)
        {
            var moveFileResult = new BookFileMoveResult();
            // A book that was only just added has no lazy loader for its files (the import failed here with a NullReferenceException),
            // so read them from the database instead of assuming they were loaded.
            var existingFiles = localBook.Book.BookFiles?.Value
                                ?? _mediaFileService.GetFilesByBook(localBook.Book.Id)
                                ?? new List<BookFile>();

            // An author without a folder, or one whose folder is not inside any root folder, cannot take the file. Report it as a
            // missing root folder (the import handles that case and says so) instead of failing with a NullReferenceException.
            if (localBook.Author == null || localBook.Author.Path.IsNullOrWhiteSpace())
            {
                throw new RootFolderNotFoundException("The author has no folder, so the book cannot be imported.");
            }

            var rootFolderPath = _diskProvider.GetParentFolder(localBook.Author.Path);
            var rootFolder = _rootFolderService.GetBestRootFolder(rootFolderPath);

            if (rootFolder == null)
            {
                throw new RootFolderNotFoundException($"No root folder contains '{rootFolderPath}'.");
            }

            var isCalibre = rootFolder.IsCalibreLibrary && rootFolder.CalibreSettings != null;

            var settings = rootFolder.CalibreSettings;

            // If there are existing book files and the root folder is missing, throw, so the old file isn't left behind during the import process.
            if (existingFiles.Any() && !_diskProvider.FolderExists(rootFolderPath))
            {
                throw new RootFolderNotFoundException($"Root folder '{rootFolderPath}' was not found.");
            }

            // Existing files are moved aside, not deleted, while the new file is put in place. The new file may need the very path the old
            // one has, but if getting it in place fails the old file must still exist (with no recycle bin configured, deleting first meant
            // the book could be lost). Once the new file is in place the old files are recycled; if anything fails they are put back.
            var movedAside = new List<(string Original, string Aside, string Subfolder)>();

            try
            {
                foreach (var file in existingFiles)
                {
                    var bookFilePath = file.Path;
                    var subfolder = rootFolderPath.GetRelativePath(_diskProvider.GetParentFolder(bookFilePath));

                    bookFile.CalibreId = file.CalibreId;

                    if (_diskProvider.FileExists(bookFilePath))
                    {
                        if (!isCalibre)
                        {
                            var asidePath = GetAsidePath(bookFilePath);
                            _logger.Debug("Moving existing book file aside: {0} CalibreId: {1}", file, file.CalibreId);

                            _diskProvider.MoveFile(bookFilePath, asidePath);
                            movedAside.Add((bookFilePath, asidePath, subfolder));
                        }
                        else
                        {
                            _logger.Debug("Removing existing book file: {0} CalibreId: {1}", file, file.CalibreId);

                            var existing = _calibre.GetBook(file.CalibreId, settings);
                            var existingFormats = existing.Formats.Keys;
                            _logger.Debug($"Removing existing formats {existingFormats.ConcatToString()} from calibre");
                            _calibre.RemoveFormats(file.CalibreId, existingFormats, settings);
                        }
                    }
                }

                if (!isCalibre)
                {
                    if (copyOnly)
                    {
                        moveFileResult.BookFile = _bookFileMover.CopyBookFile(bookFile, localBook);
                    }
                    else
                    {
                        moveFileResult.BookFile = _bookFileMover.MoveBookFile(bookFile, localBook);
                    }

                    _metadataTagService.WriteTags(bookFile, true);
                }
                else
                {
                    var source = bookFile.Path;

                    moveFileResult.BookFile = _calibre.AddAndConvert(bookFile, settings);

                    if (!copyOnly)
                    {
                        _diskProvider.DeleteFile(source);
                    }
                }

                // The new file is in place, so the old ones can go. A failure to recycle one must not undo the import: the file is
                // simply kept under its temporary name next to the new one.
                foreach (var (original, aside, subfolder) in movedAside)
                {
                    try
                    {
                        _recycleBinProvider.DeleteFile(aside, subfolder);
                    }
                    catch (Exception e)
                    {
                        _logger.Warn(e, "The new file for {0} is in place, but the file it replaced could not be recycled; it is kept at {1}", original, aside);
                    }
                }

                foreach (var file in existingFiles)
                {
                    moveFileResult.OldFiles.Add(file);
                    _mediaFileService.Delete(file, DeleteMediaFileReason.Upgrade);
                }
            }
            catch
            {
                RestoreMovedAside(movedAside);
                throw;
            }

            return moveFileResult;
        }

        private static string GetAsidePath(string path)
        {
            // Same folder (so the move is a rename), and the real extension so a recycled copy still opens.
            return Path.Combine(
                Path.GetDirectoryName(path) ?? string.Empty,
                Path.GetFileNameWithoutExtension(path) + ".replaced-" + Guid.NewGuid().ToString("N").Substring(0, 8) + Path.GetExtension(path));
        }

        private void RestoreMovedAside(List<(string Original, string Aside, string Subfolder)> movedAside)
        {
            foreach (var (original, aside, _) in movedAside)
            {
                try
                {
                    if (!_diskProvider.FileExists(aside))
                    {
                        continue;
                    }

                    if (_diskProvider.FileExists(original))
                    {
                        _logger.Error("Could not put the existing book file back at {0} because a file is there now; it is kept at {1}", original, aside);
                        continue;
                    }

                    _diskProvider.MoveFile(aside, original);
                    _logger.Debug("Put the existing book file back at {0}", original);
                }
                catch (Exception e)
                {
                    _logger.Error(e, "Could not put the existing book file back at {0}; it is kept at {1}", original, aside);
                }
            }
        }
    }
}
