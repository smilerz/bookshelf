using System;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace NzbDrone.Core.MediaCover
{
    public interface IImageResizer
    {
        void Resize(string source, string destination, int height);
    }

    public class ImageResizer : IImageResizer
    {
        private readonly IDiskProvider _diskProvider;
        private readonly bool _enabled;

        public ImageResizer(IDiskProvider diskProvider, IPlatformInfo platformInfo)
        {
            _diskProvider = diskProvider;

            _enabled = true;

            // Thumbnails don't need super high quality
            SixLabors.ImageSharp.Configuration.Default.ImageFormatsManager.SetEncoder(JpegFormat.Instance, new JpegEncoder
            {
                Quality = 92
            });
        }

        public void Resize(string source, string destination, int height)
        {
            if (!_enabled)
            {
                return;
            }

            try
            {
                // Covers are plain JPEG/PNG/WebP files. TIFF is rejected and metadata (ICC profiles, EXIF) is not parsed,
                // which keeps the decoders behind the suppressed ImageSharp advisories out of reach (see Directory.Build.props).
                var format = Image.DetectFormat(source);
                if (string.Equals(format?.Name, "TIFF", StringComparison.OrdinalIgnoreCase))
                {
                    throw new NotSupportedException("TIFF images are not resized: " + source);
                }

                using var image = Image.Load(new DecoderOptions { SkipMetadata = true }, source);
                image.Mutate(x => x.Resize(0, height));
                image.Save(destination);
            }
            catch
            {
                if (_diskProvider.FileExists(destination))
                {
                    _diskProvider.DeleteFile(destination);
                }

                throw;
            }
        }
    }
}
