using System.IO.Abstractions;
using System.Linq;
using NLog;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.MediaFiles.Azw;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using VersOne.Epub;

namespace NzbDrone.Core.MediaFiles
{
    public interface IEBookTagService
    {
        ParsedTrackInfo ReadTags(IFileInfo file);
    }

    public class EBookTagService : IEBookTagService
    {
        private readonly Logger _logger;

        public EBookTagService(Logger logger)
        {
            _logger = logger;
        }

        public ParsedTrackInfo ReadTags(IFileInfo file)
        {
            var extension = file.Extension.ToLower();
            _logger.Trace($"Got extension '{extension}'");

            switch (extension)
            {
                case ".epub":
                    return ReadEpub(file.FullName);
                case ".azw3":
                case ".mobi":
                    return ReadAzw3(file.FullName);
                default:
                    return Parser.Parser.ParseMusicTitle(file.FullName);
            }
        }

        private ParsedTrackInfo ReadEpub(string file)
        {
            _logger.Trace($"Reading {file}");
            var result = new ParsedTrackInfo();

            using (var bookRef = EpubReader.OpenBook(file))
            {
                result.ArtistTitle = bookRef.AuthorList.FirstOrDefault();
                result.AlbumTitle = bookRef.Title;

                var meta = bookRef.Schema.Package.Metadata;
                result.Isbn = meta?.Identifiers?.FirstOrDefault(x => x.Scheme?.ToLower().Contains("isbn") ?? false)?.Identifier;
                result.Asin = meta?.Identifiers?.FirstOrDefault(x => x.Scheme?.ToLower().Contains("asin") ?? false)?.Identifier;
                result.Language = meta?.Languages?.FirstOrDefault();

                result.Quality = new QualityModel
                {
                    Quality = Quality.EPUB,
                    QualityDetectionSource = QualityDetectionSource.TagLib
                };
            }

            _logger.Trace($"Got {result.ToJson()}");

            return result;
        }

        private ParsedTrackInfo ReadAzw3(string file)
        {
            _logger.Trace($"Reading {file}");
            var result = new ParsedTrackInfo();

            var book = new Azw3File(file);
            result.ArtistTitle = book.Author;
            result.AlbumTitle = book.Title;
            result.Isbn = book.Isbn;
            result.Asin = book.Asin;
            result.Language = book.Language;

            result.Quality = new QualityModel
            {
                Quality = book.Version == 6 ? Quality.MOBI : Quality.AZW3,
                QualityDetectionSource = QualityDetectionSource.TagLib
            };

            _logger.Trace($"Got {result.ToJson()}");

            return result;
        }
    }
}
