using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Windows_Server_Tools
{
    internal sealed class LogoSettings
    {
        [JsonProperty("schemaVersion", Required = Required.Always)]
        public int SchemaVersion { get; set; }

        [JsonProperty("preset", Required = Required.Always)]
        public string Preset { get; set; }

        [JsonProperty("fit", Required = Required.Always)]
        public string Fit { get; set; }

        [JsonProperty("background", Required = Required.Always)]
        public string Background { get; set; }

        [JsonProperty("focalX", Required = Required.Always)]
        public double FocalX { get; set; }

        [JsonProperty("focalY", Required = Required.Always)]
        public double FocalY { get; set; }

        [JsonProperty("displaySha256", Required = Required.Always)]
        public string DisplaySha256 { get; set; }
    }

    internal sealed class LogoService
    {
        internal const int MaximumSourceBytes = 5 * 1024 * 1024;
        internal const int MaximumDimension = 4096;
        internal const long MaximumPixels = 16L * 1024L * 1024L;
        private readonly string _directory;
        private readonly string _settingsPath;
        private readonly string _sourcePath;
        private readonly string _masterPath;
        private readonly string _displayPath;
        private readonly bool _protectedStorage;

        public LogoService(string directory)
            : this(directory, true)
        {
        }

        internal LogoService(string directory, bool protectedStorage)
        {
            _directory = directory ?? throw new ArgumentNullException(nameof(directory));
            _settingsPath = Path.Combine(_directory, "logo-settings.json");
            _sourcePath = Path.Combine(_directory, "custom-logo-source.bin");
            _masterPath = Path.Combine(_directory, "custom-logo-256.png");
            _displayPath = Path.Combine(_directory, "custom-logo-48.png");
            _protectedStorage = protectedStorage;
        }

        public static LogoService CreateDefault()
        {
            string marker = ProtectedWorkflowState.GetPath("Branding", "logo.marker");
            return new LogoService(Path.GetDirectoryName(marker));
        }

        public LogoSettings LoadSettings()
        {
            if (!File.Exists(_settingsPath))
            {
                return DefaultSettings();
            }

            try
            {
                LogoSettings settings = DeserializeStrict<LogoSettings>(ReadAllBytes(_settingsPath));
                ValidateSettings(settings);
                if (string.Equals(settings.Preset, "custom", StringComparison.Ordinal)
                    && !ValidateDisplayCache(settings))
                {
                    return DefaultSettings();
                }

                return settings;
            }
            catch (Exception ex) when (!RecoveryRunner.IsFatal(ex))
            {
                ErrorLog.Write("Load custom application logo", ex);
                return DefaultSettings();
            }
        }

        public BitmapSource LoadCustomDisplay(LogoSettings settings)
        {
            if (settings == null
                || !string.Equals(settings.Preset, "custom", StringComparison.Ordinal)
                || !ValidateDisplayCache(settings))
            {
                return null;
            }

            return DecodeVerifiedPng(ReadAllBytes(_displayPath), 48, 48);
        }

        public ImageSource LoadPresentationSource(LogoSettings settings)
        {
            if (settings != null && string.Equals(settings.Preset, "custom", StringComparison.Ordinal))
            {
                BitmapSource custom = LoadCustomDisplay(settings);
                if (custom != null)
                {
                    return custom;
                }
            }

            string asset = settings != null && string.Equals(settings.Preset, "icon", StringComparison.Ordinal)
                ? "Assets/windows-server-setupper.ico"
                : "Assets/windows-server-setupper-logo-master.png";
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri("pack://application:,,,/" + asset, UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }

        public LogoSettings ApplyPreset(string preset)
        {
            if (!string.Equals(preset, "master", StringComparison.Ordinal)
                && !string.Equals(preset, "icon", StringComparison.Ordinal))
            {
                throw new ArgumentException("The selected logo preset is unsupported.", nameof(preset));
            }

            var settings = DefaultSettings();
            settings.Preset = preset;
            WriteSettings(settings);
            return settings;
        }

        public LogoSettings ImportCustom(
            byte[] sourceBytes,
            string fit,
            string background,
            double focalX,
            double focalY)
        {
            BitmapSource source = DecodeBoundedSource(sourceBytes);
            var settings = new LogoSettings
            {
                SchemaVersion = 1,
                Preset = "custom",
                Fit = NormalizeFit(fit),
                Background = NormalizeBackground(background),
                FocalX = ClampUnit(focalX),
                FocalY = ClampUnit(focalY),
                DisplaySha256 = string.Empty
            };
            byte[] master = RenderPng(source, 256, settings);
            byte[] display = RenderPng(source, 48, settings);
            DecodeVerifiedPng(master, 256, 256);
            DecodeVerifiedPng(display, 48, 48);
            settings.DisplaySha256 = Sha256(display);

            WriteBytesAtomic(_sourcePath, sourceBytes);
            WriteBytesAtomic(_masterPath, master);
            WriteBytesAtomic(_displayPath, display);
            WriteSettings(settings);
            return settings;
        }

        public LogoSettings UpdateCustomRendering(
            string fit,
            string background,
            double focalX,
            double focalY)
        {
            if (!File.Exists(_sourcePath))
            {
                throw new InvalidOperationException("Choose a valid custom image before changing its rendering.");
            }

            return ImportCustom(ReadAllBytes(_sourcePath), fit, background, focalX, focalY);
        }

        public void Reset()
        {
            DeleteIfExists(_settingsPath);
            DeleteIfExists(_sourcePath);
            DeleteIfExists(_masterPath);
            DeleteIfExists(_displayPath);
        }

        internal static BitmapSource DecodeBoundedSource(byte[] sourceBytes)
        {
            if (sourceBytes == null || sourceBytes.Length == 0 || sourceBytes.Length > MaximumSourceBytes)
            {
                throw new InvalidDataException("The logo source must be between 1 byte and 5 MiB.");
            }

            if (!HasAllowedSignature(sourceBytes))
            {
                throw new InvalidDataException("The logo source must contain PNG, JPEG, or BMP bytes.");
            }

            try
            {
                using (var input = new MemoryStream(sourceBytes, false))
                {
                    BitmapDecoder decoder = BitmapDecoder.Create(
                        input,
                        BitmapCreateOptions.PreservePixelFormat,
                        BitmapCacheOption.OnLoad);
                    if (decoder.Frames.Count != 1)
                    {
                        throw new InvalidDataException("Animated or multi-frame logo images are not supported.");
                    }

                    BitmapFrame frame = decoder.Frames[0];
                    if (frame.PixelWidth <= 0
                        || frame.PixelHeight <= 0
                        || frame.PixelWidth > MaximumDimension
                        || frame.PixelHeight > MaximumDimension
                        || (long)frame.PixelWidth * frame.PixelHeight > MaximumPixels)
                    {
                        throw new InvalidDataException("The decoded logo exceeds 4096 pixels or 16 megapixels.");
                    }

                    frame.Freeze();
                    return frame;
                }
            }
            catch (Exception ex) when (ex is NotSupportedException || ex is FileFormatException)
            {
                throw new InvalidDataException("The logo image could not be decoded safely.", ex);
            }
        }

        private static byte[] RenderPng(BitmapSource source, int size, LogoSettings settings)
        {
            Color background = ParseBackground(settings.Background);
            double scale = string.Equals(settings.Fit, "fill", StringComparison.Ordinal)
                ? Math.Max((double)size / source.PixelWidth, (double)size / source.PixelHeight)
                : Math.Min((double)size / source.PixelWidth, (double)size / source.PixelHeight);
            double width = source.PixelWidth * scale;
            double height = source.PixelHeight * scale;
            double x = (size - width) * (string.Equals(settings.Fit, "fill", StringComparison.Ordinal)
                ? settings.FocalX
                : 0.5);
            double y = (size - height) * (string.Equals(settings.Fit, "fill", StringComparison.Ordinal)
                ? settings.FocalY
                : 0.5);

            var visual = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
            using (DrawingContext context = visual.RenderOpen())
            {
                if (background.A > 0)
                {
                    context.DrawRectangle(new SolidColorBrush(background), null, new Rect(0, 0, size, size));
                }

                context.DrawImage(source, new Rect(x, y, width, height));
            }

            var rendered = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            rendered.Render(visual);
            rendered.Freeze();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rendered));
            using (var output = new MemoryStream())
            {
                encoder.Save(output);
                return output.ToArray();
            }
        }

        private bool ValidateDisplayCache(LogoSettings settings)
        {
            if (!File.Exists(_displayPath) || string.IsNullOrWhiteSpace(settings.DisplaySha256))
            {
                return false;
            }

            try
            {
                byte[] bytes = ReadAllBytes(_displayPath);
                return FixedTimeEquals(Sha256(bytes), settings.DisplaySha256)
                    && DecodeVerifiedPng(bytes, 48, 48) != null;
            }
            catch (Exception ex) when (!RecoveryRunner.IsFatal(ex))
            {
                ErrorLog.Write("Validate custom application logo cache", ex);
                return false;
            }
        }

        private static BitmapSource DecodeVerifiedPng(byte[] bytes, int width, int height)
        {
            if (bytes == null
                || bytes.Length < 24
                || !bytes.Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            {
                throw new InvalidDataException("A generated logo derivative is not a PNG.");
            }

            using (var input = new MemoryStream(bytes, false))
            {
                BitmapDecoder decoder = new PngBitmapDecoder(
                    input,
                    BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad);
                BitmapFrame frame = decoder.Frames.Single();
                if (frame.PixelWidth != width || frame.PixelHeight != height)
                {
                    throw new InvalidDataException("A generated logo derivative has the wrong dimensions.");
                }

                frame.Freeze();
                return frame;
            }
        }

        private static bool HasAllowedSignature(byte[] bytes)
        {
            bool png = bytes.Length >= 8
                && bytes.Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
            bool jpeg = bytes.Length >= 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff;
            bool bmp = bytes.Length >= 2 && bytes[0] == (byte)'B' && bytes[1] == (byte)'M';
            return png || jpeg || bmp;
        }

        private void WriteSettings(LogoSettings settings)
        {
            ValidateSettings(settings);
            WriteBytesAtomic(
                _settingsPath,
                new UTF8Encoding(false).GetBytes(JsonConvert.SerializeObject(settings, Formatting.None)));
        }

        private static void ValidateSettings(LogoSettings settings)
        {
            if (settings == null
                || settings.SchemaVersion != 1
                || !(new[] { "master", "icon", "custom" }).Contains(settings.Preset)
                || !(new[] { "contain", "fill" }).Contains(settings.Fit)
                || settings.FocalX < 0
                || settings.FocalX > 1
                || settings.FocalY < 0
                || settings.FocalY > 1)
            {
                throw new InvalidDataException("The application-logo settings are invalid.");
            }

            NormalizeBackground(settings.Background);
            if (string.Equals(settings.Preset, "custom", StringComparison.Ordinal)
                && (settings.DisplaySha256 == null || settings.DisplaySha256.Length != 64))
            {
                throw new InvalidDataException("The custom-logo cache identity is invalid.");
            }
        }

        private static LogoSettings DefaultSettings()
        {
            return new LogoSettings
            {
                SchemaVersion = 1,
                Preset = "master",
                Fit = "contain",
                Background = "transparent",
                FocalX = 0.5,
                FocalY = 0.5,
                DisplaySha256 = string.Empty
            };
        }

        private static string NormalizeFit(string fit)
        {
            string value = (fit ?? string.Empty).Trim().ToLowerInvariant();
            if (value != "contain" && value != "fill")
            {
                throw new InvalidDataException("Logo fit must be contain or fill.");
            }

            return value;
        }

        private static string NormalizeBackground(string background)
        {
            string value = (background ?? string.Empty).Trim();
            if (string.Equals(value, "transparent", StringComparison.OrdinalIgnoreCase))
            {
                return "transparent";
            }

            if (value.Length != 9 || value[0] != '#'
                || !value.Skip(1).All(character => Uri.IsHexDigit(character)))
            {
                throw new InvalidDataException("Logo background must be transparent or #AARRGGBB.");
            }

            return value.ToUpperInvariant();
        }

        private static Color ParseBackground(string value)
        {
            string normalized = NormalizeBackground(value);
            if (normalized == "transparent")
            {
                return Colors.Transparent;
            }

            return Color.FromArgb(
                byte.Parse(normalized.Substring(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(normalized.Substring(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(normalized.Substring(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(normalized.Substring(7, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        }

        private void WriteBytesAtomic(string path, byte[] bytes)
        {
            PrepareFile(path);
            string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllBytes(temporary, bytes);
                SecureFile(temporary);
                if (File.Exists(path))
                {
                    string backup = path + ".previous-" + Guid.NewGuid().ToString("N");
                    try
                    {
                        File.Replace(temporary, path, backup, true);
                    }
                    finally
                    {
                        TryDelete(backup);
                    }
                }
                else
                {
                    File.Move(temporary, path);
                }

                SecureFile(path);
            }
            finally
            {
                TryDelete(temporary);
            }
        }

        private byte[] ReadAllBytes(string path)
        {
            if (_protectedStorage)
            {
                ProtectedWorkflowState.PrepareProtectedFilePath(path);
            }
            return File.ReadAllBytes(path);
        }

        private void PrepareFile(string path)
        {
            if (_protectedStorage)
            {
                ProtectedWorkflowState.PrepareProtectedFilePath(path);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            }
        }

        private void SecureFile(string path)
        {
            if (_protectedStorage)
            {
                ProtectedWorkflowState.SecureProtectedFile(path);
            }
        }

        private static T DeserializeStrict<T>(byte[] bytes)
        {
            try
            {
                string json = new UTF8Encoding(false, true).GetString(bytes);
                using (var reader = new JsonTextReader(new StringReader(json))
                {
                    DateParseHandling = DateParseHandling.None,
                    MaxDepth = 8
                })
                {
                    JObject token = JObject.Load(reader, new JsonLoadSettings
                    {
                        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
                    });
                    return token.ToObject<T>(JsonSerializer.Create(new JsonSerializerSettings
                    {
                        MissingMemberHandling = MissingMemberHandling.Error,
                        MaxDepth = 8
                    }));
                }
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("The application-logo settings are malformed.", ex);
            }
        }

        private static double ClampUnit(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new InvalidDataException("The logo focal point is invalid.");
            }
            return Math.Max(0, Math.Min(1, value));
        }

        private static string Sha256(byte[] bytes)
        {
            using (var hash = SHA256.Create())
            {
                return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static bool FixedTimeEquals(string left, string right)
        {
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }
            int difference = 0;
            for (int index = 0; index < left.Length; index++)
            {
                difference |= left[index] ^ right[index];
            }
            return difference == 0;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
