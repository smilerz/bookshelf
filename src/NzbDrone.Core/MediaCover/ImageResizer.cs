using System;
using System.IO;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using SkiaSharp;

namespace NzbDrone.Core.MediaCover
{
    public interface IImageResizer
    {
        void Resize(string source, string destination, int height);
    }

    public class ImageResizer : IImageResizer
    {
        // Thumbnails don't need super high quality
        private const int JpegQuality = 92;

        private readonly IDiskProvider _diskProvider;
        private readonly bool _enabled;

        public ImageResizer(IDiskProvider diskProvider, IPlatformInfo platformInfo)
        {
            _diskProvider = diskProvider;

            _enabled = true;
        }

        public void Resize(string source, string destination, int height)
        {
            if (!_enabled)
            {
                return;
            }

            try
            {
                var format = GetEncodedFormat(destination);

                using var original = SKBitmap.Decode(source);
                if (original == null || original.Height <= 0)
                {
                    throw new InvalidDataException("Unsupported or corrupt image: " + source);
                }

                var width = Math.Max(1, (int)Math.Round(original.Width * (double)height / original.Height));

                using var resized = original.Resize(new SKImageInfo(width, height), new SKSamplingOptions(SKCubicResampler.Mitchell));
                if (resized == null)
                {
                    throw new InvalidOperationException("Could not resize image: " + source);
                }

                using var image = SKImage.FromBitmap(resized);
                using var data = image.Encode(format, JpegQuality);
                using var output = File.Create(destination);
                data.SaveTo(output);
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

        // Covers are JPEG or PNG. Anything else throws, and MediaCoverService then serves the full size image.
        private static SKEncodedImageFormat GetEncodedFormat(string destination)
        {
            switch (Path.GetExtension(destination).ToLowerInvariant())
            {
                case ".jpg":
                case ".jpeg":
                    return SKEncodedImageFormat.Jpeg;
                case ".png":
                    return SKEncodedImageFormat.Png;
                default:
                    throw new NotSupportedException("Cannot write thumbnails as " + Path.GetExtension(destination) + ": " + destination);
            }
        }
    }
}
