using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.SkyHook;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.TrackImport.Identification
{
    public interface ICandidateService
    {
        List<CandidateAlbumRelease> GetDbCandidatesFromTags(LocalAlbumRelease localAlbumRelease, IdentificationOverrides idOverrides, bool includeExisting);
        List<CandidateAlbumRelease> GetRemoteCandidates(LocalAlbumRelease localAlbumRelease);
    }

    public class CandidateService : ICandidateService
    {
        private readonly ISearchForNewBook _albumSearchService;
        private readonly IArtistService _artistService;
        private readonly IAlbumService _albumService;
        private readonly IMediaFileService _mediaFileService;
        private readonly Logger _logger;

        public CandidateService(ISearchForNewBook albumSearchService,
                                IArtistService artistService,
                                IAlbumService albumService,
                                IMediaFileService mediaFileService,
                                Logger logger)
        {
            _albumSearchService = albumSearchService;
            _artistService = artistService;
            _albumService = albumService;
            _mediaFileService = mediaFileService;
            _logger = logger;
        }

        public List<CandidateAlbumRelease> GetDbCandidatesFromTags(LocalAlbumRelease localAlbumRelease, IdentificationOverrides idOverrides, bool includeExisting)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();

            // Generally artist, album and release are null.  But if they're not then limit candidates appropriately.
            // We've tried to make sure that tracks are all for a single release.
            List<CandidateAlbumRelease> candidateReleases;

            // if we have a Book ID, use that
            Book tagMbidRelease = null;
            List<CandidateAlbumRelease> tagCandidate = null;

            // TODO: select by ISBN?
            // var releaseIds = localAlbumRelease.LocalTracks.Select(x => x.FileTrackInfo.ReleaseMBId).Distinct().ToList();
            // if (releaseIds.Count == 1 && releaseIds[0].IsNotNullOrWhiteSpace())
            // {
            //     _logger.Debug("Selecting release from consensus ForeignReleaseId [{0}]", releaseIds[0]);
            //     tagMbidRelease = _releaseService.GetReleaseByForeignReleaseId(releaseIds[0], true);

            //     if (tagMbidRelease != null)
            //     {
            //         tagCandidate = GetDbCandidatesByRelease(new List<AlbumRelease> { tagMbidRelease }, includeExisting);
            //     }
            // }
            if (idOverrides?.Album != null)
            {
                // use the release from file tags if it exists and agrees with the specified album
                if (tagMbidRelease?.Id == idOverrides.Album.Id)
                {
                    candidateReleases = tagCandidate;
                }
                else
                {
                    candidateReleases = GetDbCandidatesByAlbum(idOverrides.Album, includeExisting);
                }
            }
            else if (idOverrides?.Artist != null)
            {
                // use the release from file tags if it exists and agrees with the specified album
                if (tagMbidRelease?.AuthorMetadataId == idOverrides.Artist.AuthorMetadataId)
                {
                    candidateReleases = tagCandidate;
                }
                else
                {
                    candidateReleases = GetDbCandidatesByArtist(localAlbumRelease, idOverrides.Artist, includeExisting);
                }
            }
            else
            {
                if (tagMbidRelease != null)
                {
                    candidateReleases = tagCandidate;
                }
                else
                {
                    candidateReleases = GetDbCandidates(localAlbumRelease, includeExisting);
                }
            }

            watch.Stop();
            _logger.Debug($"Getting {candidateReleases.Count} candidates from tags for {localAlbumRelease.LocalTracks.Count} tracks took {watch.ElapsedMilliseconds}ms");

            return candidateReleases;
        }

        private List<CandidateAlbumRelease> GetDbCandidatesByAlbum(Book album, bool includeExisting)
        {
            return new List<CandidateAlbumRelease>
            {
                new CandidateAlbumRelease
                {
                    Book = album,
                    ExistingTracks = includeExisting ? _mediaFileService.GetFilesByAlbum(album.Id) : new List<BookFile>()
                }
            };
        }

        private List<CandidateAlbumRelease> GetDbCandidatesByArtist(LocalAlbumRelease localAlbumRelease, Author artist, bool includeExisting)
        {
            _logger.Trace("Getting candidates for {0}", artist);
            var candidateReleases = new List<CandidateAlbumRelease>();

            var albumTag = localAlbumRelease.LocalTracks.MostCommon(x => x.FileTrackInfo.AlbumTitle) ?? "";
            if (albumTag.IsNotNullOrWhiteSpace())
            {
                var possibleAlbums = _albumService.GetCandidates(artist.AuthorMetadataId, albumTag);
                foreach (var album in possibleAlbums)
                {
                    candidateReleases.AddRange(GetDbCandidatesByAlbum(album, includeExisting));
                }
            }

            return candidateReleases;
        }

        private List<CandidateAlbumRelease> GetDbCandidates(LocalAlbumRelease localAlbumRelease, bool includeExisting)
        {
            // most general version, nothing has been specified.
            // get all plausible artists, then all plausible albums, then get releases for each of these.
            var candidateReleases = new List<CandidateAlbumRelease>();

            // check if it looks like VA.
            if (TrackGroupingService.IsVariousArtists(localAlbumRelease.LocalTracks))
            {
                var va = _artistService.FindById(DistanceCalculator.VariousAuthorIds[0]);
                if (va != null)
                {
                    candidateReleases.AddRange(GetDbCandidatesByArtist(localAlbumRelease, va, includeExisting));
                }
            }

            var artistTag = localAlbumRelease.LocalTracks.MostCommon(x => x.FileTrackInfo.ArtistTitle) ?? "";
            if (artistTag.IsNotNullOrWhiteSpace())
            {
                var possibleArtists = _artistService.GetCandidates(artistTag);
                foreach (var artist in possibleArtists)
                {
                    candidateReleases.AddRange(GetDbCandidatesByArtist(localAlbumRelease, artist, includeExisting));
                }
            }

            return candidateReleases;
        }

        public List<CandidateAlbumRelease> GetRemoteCandidates(LocalAlbumRelease localAlbumRelease)
        {
            // Gets candidate album releases from the metadata server.
            // Will eventually need adding locally if we find a match
            var watch = System.Diagnostics.Stopwatch.StartNew();

            List<Book> remoteAlbums = null;
            var candidates = new List<CandidateAlbumRelease>();

            var isbns = localAlbumRelease.LocalTracks.Select(x => x.FileTrackInfo.Isbn).Distinct().ToList();
            var asins = localAlbumRelease.LocalTracks.Select(x => x.FileTrackInfo.Asin).Distinct().ToList();

            try
            {
                if (isbns.Count == 1 && isbns[0].IsNotNullOrWhiteSpace())
                {
                    // Use isbn in tags if set
                    _logger.Trace($"Searching by isbn {isbns[0]}");
                    remoteAlbums = _albumSearchService.SearchByIsbn(isbns[0]);
                }

                if (asins.Count == 1 && asins[0].IsNotNullOrWhiteSpace() && (remoteAlbums == null || !remoteAlbums.Any()))
                {
                    // Try asin if no result
                    _logger.Trace($"Searching by asin {asins[0]}");
                    remoteAlbums = _albumSearchService.SearchForNewBook(asins[0], null);
                }

                // if no asin/isbn or no result, fall back to text search
                if (remoteAlbums == null || !remoteAlbums.Any())
                {
                    // fall back to artist / album name search
                    string artistTag;

                    if (TrackGroupingService.IsVariousArtists(localAlbumRelease.LocalTracks))
                    {
                        artistTag = "Various Artists";
                    }
                    else
                    {
                        artistTag = localAlbumRelease.LocalTracks.MostCommon(x => x.FileTrackInfo.ArtistTitle) ?? "";
                    }

                    var albumTag = localAlbumRelease.LocalTracks.MostCommon(x => x.FileTrackInfo.AlbumTitle) ?? "";

                    if (artistTag.IsNullOrWhiteSpace() || albumTag.IsNullOrWhiteSpace())
                    {
                        return candidates;
                    }

                    remoteAlbums = _albumSearchService.SearchForNewBook(albumTag, artistTag);
                }
            }
            catch (SkyHookException e)
            {
                _logger.Info(e, "Skipping album due to SkyHook error");
                remoteAlbums = new List<Book>();
            }

            foreach (var album in remoteAlbums)
            {
                candidates.Add(new CandidateAlbumRelease
                {
                    Book = album,
                    ExistingTracks = new List<BookFile>()
                });
            }

            watch.Stop();
            _logger.Debug($"Getting {candidates.Count} remote candidates from tags for {localAlbumRelease.LocalTracks.Count} tracks took {watch.ElapsedMilliseconds}ms");

            return candidates;
        }
    }
}
