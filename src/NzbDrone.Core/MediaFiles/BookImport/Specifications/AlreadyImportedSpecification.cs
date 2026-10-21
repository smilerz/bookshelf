using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.History;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.BookImport.Specifications
{
    public class AlreadyImportedSpecification : IImportDecisionEngineSpecification<LocalEdition>
    {
        private readonly IHistoryService _historyService;
        private readonly Logger _logger;

        public AlreadyImportedSpecification(IHistoryService historyService,
                                            Logger logger)
        {
            _historyService = historyService;
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Database;

        public Decision IsSatisfiedBy(LocalEdition localBookRelease, DownloadClientItem downloadClientItem)
        {
            if (downloadClientItem == null)
            {
                // Manual import and drop-folder imports carry no download id, so the download cannot be compared. Compare the files instead.
                return CheckFiles(localBookRelease);
            }

            var bookRelease = localBookRelease.Edition;

            if ((!bookRelease.BookFiles?.Value?.Any()) ?? true)
            {
                _logger.Debug("Skipping already imported check for book without files");
                return Decision.Accept();
            }

            var bookHistory = _historyService.GetByBook(bookRelease.BookId, null);
            var lastImported = bookHistory.FirstOrDefault(h => h.EventType == EntityHistoryEventType.BookFileImported);
            var lastGrabbed = bookHistory.FirstOrDefault(h => h.EventType == EntityHistoryEventType.Grabbed);

            if (lastImported == null)
            {
                _logger.Trace("Book file has not been imported");
                return Decision.Accept();
            }

            if (lastGrabbed != null && lastGrabbed.Date.After(lastImported.Date))
            {
                _logger.Trace("Book file was grabbed again after importing");
                return Decision.Accept();
            }

            if (lastImported.DownloadId == downloadClientItem.DownloadId)
            {
                _logger.Debug("Book previously imported at {0}", lastImported.Date);
                return Decision.Reject("Book already imported at {0}", lastImported.Date);
            }

            return Decision.Accept();
        }

        private Decision CheckFiles(LocalEdition localBookRelease)
        {
            var edition = localBookRelease.Edition;

            if (edition == null || localBookRelease.LocalBooks == null)
            {
                return Decision.Accept();
            }

            // Files already in the library being re-linked are not new imports.
            var incoming = localBookRelease.LocalBooks.Where(x => !x.ExistingFile).ToList();

            if (!incoming.Any())
            {
                return Decision.Accept();
            }

            // History records the path each import came from, so the same source file imported again can be recognised exactly.
            var imports = _historyService.GetByBook(edition.BookId, EntityHistoryEventType.BookFileImported) ?? new List<EntityHistory>();

            foreach (var localBook in incoming)
            {
                var earlier = imports.FirstOrDefault(h => h.Data != null &&
                                                           h.Data.TryGetValue("DroppedPath", out var dropped) &&
                                                           dropped.IsNotNullOrWhiteSpace() &&
                                                           dropped.PathEquals(localBook.Path));

                if (earlier != null)
                {
                    _logger.Debug("{0} was already imported at {1}", localBook.Path, earlier.Date);
                    return Decision.Reject("Already imported at {0}", earlier.Date);
                }
            }

            // A library file of the same size for this book is almost certainly the same file.
            var existing = edition.BookFiles?.Value;

            if (existing != null && existing.Any())
            {
                foreach (var localBook in incoming)
                {
                    var same = existing.FirstOrDefault(f => f.Size == localBook.Size);

                    if (same != null)
                    {
                        _logger.Debug("{0} has the same size as {1}", localBook.Path, same.Path);
                        return Decision.Reject("A file of the same size is already in the library: {0}", same.Path);
                    }
                }
            }

            return Decision.Accept();
        }
    }
}
