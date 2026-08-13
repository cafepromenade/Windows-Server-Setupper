using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Windows_Server_Tools
{
    internal enum UpdateAvailability
    {
        Current,
        Available
    }

    internal sealed class UpdateManifest
    {
        [JsonProperty("schemaVersion", Required = Required.Always)]
        public int SchemaVersion { get; set; }

        [JsonProperty("version", Required = Required.Always)]
        public string Version { get; set; }

        [JsonProperty("releaseNotesUrl", Required = Required.Always)]
        public string ReleaseNotesUrl { get; set; }

        [JsonProperty("assetUrl", Required = Required.Always)]
        public string AssetUrl { get; set; }

        [JsonProperty("sha256", Required = Required.Always)]
        public string Sha256 { get; set; }

        [JsonProperty("sizeBytes", Required = Required.Always)]
        public long SizeBytes { get; set; }

        [JsonIgnore]
        public Version ParsedVersion { get; set; }

        [JsonIgnore]
        public Uri ParsedReleaseNotesUri { get; set; }

        [JsonIgnore]
        public Uri ParsedAssetUri { get; set; }
    }

    internal sealed class UpdateCheckResult
    {
        public UpdateAvailability Availability { get; set; }

        public UpdateManifest Manifest { get; set; }
    }

    internal sealed class UpdateInstallState
    {
        [JsonProperty("schemaVersion", Required = Required.Always)]
        public int SchemaVersion { get; set; }

        [JsonProperty("currentVersion", Required = Required.Always)]
        public string CurrentVersion { get; set; }

        [JsonProperty("targetVersion", Required = Required.Always)]
        public string TargetVersion { get; set; }

        [JsonProperty("stagedPath", Required = Required.Always)]
        public string StagedPath { get; set; }

        [JsonProperty("sha256", Required = Required.Always)]
        public string Sha256 { get; set; }

        [JsonProperty("installerLaunched", Required = Required.Always)]
        public bool InstallerLaunched { get; set; }

        [JsonProperty("updatedAtUtc", Required = Required.Always)]
        public string UpdatedAtUtc { get; set; }
    }

    internal sealed class UpdateService : IDisposable
    {
        internal const int ManifestSizeLimitBytes = 128 * 1024;
        internal const long PackageSizeLimitBytes = 1024L * 1024L * 1024L;
        private readonly HttpClient _httpClient;
        private readonly Uri _manifestUri;
        private readonly string _stagingDirectory;
        private readonly string _statePath;
        private readonly bool _ownsClient;
        private readonly bool _protectedStorage;

        public UpdateService(Uri manifestUri, string stagingDirectory, string statePath)
            : this(CreateProductionClient(), manifestUri, stagingDirectory, statePath, true, true)
        {
        }

        internal UpdateService(
            HttpClient httpClient,
            Uri manifestUri,
            string stagingDirectory,
            string statePath,
            bool ownsClient = false,
            bool protectedStorage = true)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _manifestUri = RequireHttps(manifestUri, nameof(manifestUri));
            _stagingDirectory = stagingDirectory ?? throw new ArgumentNullException(nameof(stagingDirectory));
            _statePath = statePath ?? throw new ArgumentNullException(nameof(statePath));
            _ownsClient = ownsClient;
            _protectedStorage = protectedStorage;
        }

        public async Task<UpdateCheckResult> CheckAsync(Version currentVersion, CancellationToken cancellationToken)
        {
            if (currentVersion == null)
            {
                throw new ArgumentNullException(nameof(currentVersion));
            }

            using (HttpResponseMessage response = await _httpClient.GetAsync(
                _manifestUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false))
            {
                if (IsRedirect(response.StatusCode))
                {
                    throw new InvalidDataException("The update feed redirected. Redirects are refused.");
                }

                response.EnsureSuccessStatusCode();
                byte[] payload = await ReadBoundedAsync(
                    response.Content,
                    ManifestSizeLimitBytes,
                    cancellationToken).ConfigureAwait(false);
                UpdateManifest manifest = ParseAndValidateManifest(payload);
                return new UpdateCheckResult
                {
                    Availability = manifest.ParsedVersion > currentVersion
                        ? UpdateAvailability.Available
                        : UpdateAvailability.Current,
                    Manifest = manifest
                };
            }
        }

        public async Task<string> DownloadAndStageAsync(
            UpdateManifest manifest,
            IProgress<int> progress,
            CancellationToken cancellationToken)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            ValidateManifest(manifest);
            string targetPath = Path.Combine(
                _stagingDirectory,
                "WindowsServerTools-Setup-" + manifest.ParsedVersion + ".exe");
            string temporaryPath = targetPath + ".download-" + Guid.NewGuid().ToString("N");
            PrepareFilePath(targetPath);

            try
            {
                using (HttpResponseMessage response = await GetPackageResponseAsync(
                    manifest.ParsedAssetUri,
                    cancellationToken).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    if (response.Content.Headers.ContentLength.HasValue
                        && response.Content.Headers.ContentLength.Value != manifest.SizeBytes)
                    {
                        throw new InvalidDataException("The update package size does not match the manifest.");
                    }

                    long total = 0;
                    byte[] buffer = new byte[81920];
                    using (Stream input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var hash = SHA256.Create())
                    using (var output = new FileStream(
                        temporaryPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        buffer.Length,
                        FileOptions.WriteThrough | FileOptions.SequentialScan))
                    {
                        while (true)
                        {
                            int read = await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken)
                                .ConfigureAwait(false);
                            if (read == 0)
                            {
                                break;
                            }

                            total += read;
                            if (total > manifest.SizeBytes || total > PackageSizeLimitBytes)
                            {
                                throw new InvalidDataException("The update package exceeded its declared size limit.");
                            }

                            hash.TransformBlock(buffer, 0, read, null, 0);
                            await output.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                            progress?.Report((int)Math.Min(100, total * 100L / manifest.SizeBytes));
                        }

                        hash.TransformFinalBlock(new byte[0], 0, 0);
                        output.Flush(true);
                        if (total != manifest.SizeBytes)
                        {
                            throw new InvalidDataException("The update package ended before its declared size.");
                        }

                        string actual = ToLowerHex(hash.Hash);
                        if (!FixedTimeEqualsHex(actual, manifest.Sha256))
                        {
                            throw new InvalidDataException("The update package SHA-256 does not match the manifest.");
                        }
                    }
                }

                SecureFile(temporaryPath);
                if (File.Exists(targetPath))
                {
                    string backupPath = targetPath + ".previous-" + Guid.NewGuid().ToString("N");
                    try
                    {
                        File.Replace(temporaryPath, targetPath, backupPath, true);
                    }
                    finally
                    {
                        TryDelete(backupPath);
                    }
                }
                else
                {
                    File.Move(temporaryPath, targetPath);
                }
                SecureFile(targetPath);
                return targetPath;
            }
            catch
            {
                TryDelete(temporaryPath);
                throw;
            }
        }

        public void SaveReadyState(Version currentVersion, UpdateManifest manifest, string stagedPath)
        {
            if (currentVersion == null || manifest == null || string.IsNullOrWhiteSpace(stagedPath))
            {
                throw new ArgumentException("Complete update state is required.");
            }

            var state = new UpdateInstallState
            {
                SchemaVersion = 1,
                CurrentVersion = currentVersion.ToString(),
                TargetVersion = manifest.ParsedVersion.ToString(),
                StagedPath = stagedPath,
                Sha256 = manifest.Sha256,
                InstallerLaunched = false,
                UpdatedAtUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
            };
            try
            {
                WriteState(state);
            }
            catch
            {
                TryDelete(stagedPath);
                throw;
            }
        }

        public void MarkInstallerLaunched()
        {
            UpdateInstallState state = LoadState();
            if (state == null)
            {
                throw new InvalidOperationException("No staged update is available.");
            }

            state.InstallerLaunched = true;
            state.UpdatedAtUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            WriteState(state);
        }

        public UpdateInstallState LoadState()
        {
            if (!File.Exists(_statePath))
            {
                return null;
            }

            byte[] bytes = Encoding.UTF8.GetBytes(ReadText(_statePath));
            UpdateInstallState state = DeserializeStrict<UpdateInstallState>(bytes);
            if (state.SchemaVersion != 1
                || !Version.TryParse(state.CurrentVersion, out Version current)
                || !Version.TryParse(state.TargetVersion, out Version target)
                || target <= current
                || !IsValidSha256(state.Sha256)
                || string.IsNullOrWhiteSpace(state.StagedPath)
                || !Path.GetFullPath(state.StagedPath).StartsWith(
                    Path.GetFullPath(_stagingDirectory).TrimEnd(Path.DirectorySeparatorChar)
                        + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The staged update state is invalid.");
            }

            return state;
        }

        public bool ValidateStagedPackage(UpdateInstallState state)
        {
            if (state == null || !File.Exists(state.StagedPath))
            {
                return false;
            }

            using (var stream = new FileStream(state.StagedPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var hash = SHA256.Create())
            {
                return FixedTimeEqualsHex(ToLowerHex(hash.ComputeHash(stream)), state.Sha256);
            }
        }

        public void ClearStateAndStagedPackage()
        {
            UpdateInstallState state = null;
            try
            {
                state = LoadState();
            }
            catch (Exception ex) when (!RecoveryRunner.IsFatal(ex))
            {
                ErrorLog.Write("Read update rollback state", ex);
            }

            if (state != null)
            {
                TryDelete(state.StagedPath);
            }

            TryDelete(_statePath);
            try
            {
                if (Directory.Exists(_stagingDirectory))
                {
                    foreach (string path in Directory.GetFiles(
                        _stagingDirectory,
                        "WindowsServerTools-Setup-*.exe",
                        SearchOption.TopDirectoryOnly))
                    {
                        TryDelete(path);
                    }
                }
            }
            catch (Exception ex) when (!RecoveryRunner.IsFatal(ex))
            {
                ErrorLog.Write("Remove orphaned staged update", ex);
            }
        }

        public void Dispose()
        {
            if (_ownsClient)
            {
                _httpClient.Dispose();
            }
        }

        internal static UpdateManifest ParseAndValidateManifest(byte[] payload)
        {
            if (payload == null || payload.Length == 0 || payload.Length > ManifestSizeLimitBytes)
            {
                throw new InvalidDataException("The update manifest is empty or exceeds 128 KiB.");
            }

            UpdateManifest manifest = DeserializeStrict<UpdateManifest>(payload);
            ValidateManifest(manifest);
            return manifest;
        }

        private static T DeserializeStrict<T>(byte[] payload)
        {
            try
            {
                string json = new UTF8Encoding(false, true).GetString(payload);
                using (var reader = new JsonTextReader(new StringReader(json))
                {
                    DateParseHandling = DateParseHandling.None,
                    MaxDepth = 8
                })
                {
                    JObject token = JObject.Load(reader, new JsonLoadSettings
                    {
                        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                        CommentHandling = CommentHandling.Ignore,
                        LineInfoHandling = LineInfoHandling.Ignore
                    });
                    var serializer = JsonSerializer.Create(new JsonSerializerSettings
                    {
                        MissingMemberHandling = MissingMemberHandling.Error,
                        MaxDepth = 8
                    });
                    return token.ToObject<T>(serializer);
                }
            }
            catch (Exception ex) when (ex is JsonException || ex is DecoderFallbackException)
            {
                throw new InvalidDataException("The update metadata is malformed or unsupported.", ex);
            }
        }

        private static void ValidateManifest(UpdateManifest manifest)
        {
            if (manifest == null
                || manifest.SchemaVersion != 1
                || !Version.TryParse(manifest.Version, out Version version)
                || version.Major < 0
                || !IsValidSha256(manifest.Sha256)
                || manifest.SizeBytes <= 0
                || manifest.SizeBytes > PackageSizeLimitBytes)
            {
                throw new InvalidDataException("The update manifest contains unsupported values.");
            }

            manifest.ParsedVersion = version;
            manifest.ParsedReleaseNotesUri = RequireHttps(
                ParseAbsoluteUri(manifest.ReleaseNotesUrl, "release notes"),
                "releaseNotesUrl");
            manifest.ParsedAssetUri = RequireHttps(
                ParseAbsoluteUri(manifest.AssetUrl, "package"),
                "assetUrl");
        }

        private void WriteState(UpdateInstallState state)
        {
            string json = JsonConvert.SerializeObject(state, Formatting.None);
            WriteTextAtomic(_statePath, json);
        }

        private void PrepareFilePath(string path)
        {
            if (_protectedStorage)
            {
                ProtectedWorkflowState.PrepareProtectedFilePath(path);
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        }

        private void SecureFile(string path)
        {
            if (_protectedStorage)
            {
                ProtectedWorkflowState.SecureProtectedFile(path);
            }
        }

        private string ReadText(string path)
        {
            return _protectedStorage
                ? ProtectedWorkflowState.ReadAllText(path)
                : File.ReadAllText(path, Encoding.UTF8);
        }

        private void WriteTextAtomic(string path, string value)
        {
            if (_protectedStorage)
            {
                ProtectedWorkflowState.WriteAllTextAtomic(path, value);
                return;
            }

            PrepareFilePath(path);
            string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temporary, value, new UTF8Encoding(false));
                if (File.Exists(path))
                {
                    File.Replace(temporary, path, null, true);
                }
                else
                {
                    File.Move(temporary, path);
                }
            }
            finally
            {
                TryDelete(temporary);
            }
        }

        private static HttpClient CreateProductionClient()
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                UseCookies = false
            };
            return new HttpClient(handler, true)
            {
                Timeout = Timeout.InfiniteTimeSpan
            };
        }

        private static async Task<byte[]> ReadBoundedAsync(
            HttpContent content,
            int limit,
            CancellationToken cancellationToken)
        {
            if (content.Headers.ContentLength.HasValue && content.Headers.ContentLength.Value > limit)
            {
                throw new InvalidDataException("The update manifest exceeds 128 KiB.");
            }

            using (Stream input = await content.ReadAsStreamAsync().ConfigureAwait(false))
            using (var output = new MemoryStream())
            {
                byte[] buffer = new byte[8192];
                while (true)
                {
                    int read = await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken)
                        .ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    if (output.Length + read > limit)
                    {
                        throw new InvalidDataException("The update manifest exceeds 128 KiB.");
                    }

                    output.Write(buffer, 0, read);
                }

                return output.ToArray();
            }
        }

        private async Task<HttpResponseMessage> GetPackageResponseAsync(
            Uri initialUri,
            CancellationToken cancellationToken)
        {
            Uri current = initialUri;
            for (int redirectCount = 0; redirectCount <= 3; redirectCount++)
            {
                HttpResponseMessage response = await _httpClient.GetAsync(
                    current,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);
                if (!IsRedirect(response.StatusCode))
                {
                    return response;
                }

                Uri location = response.Headers.Location;
                response.Dispose();
                if (location == null || redirectCount == 3)
                {
                    throw new InvalidDataException("The update package exceeded its redirect limit.");
                }

                Uri next = location.IsAbsoluteUri ? location : new Uri(current, location);
                current = RequireHttps(next, "asset redirect");
            }

            throw new InvalidDataException("The update package exceeded its redirect limit.");
        }

        private static Uri ParseAbsoluteUri(string value, string label)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri uri))
            {
                throw new InvalidDataException("The update " + label + " URL is invalid.");
            }

            return uri;
        }

        private static Uri RequireHttps(Uri uri, string parameter)
        {
            if (uri == null
                || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || !string.IsNullOrEmpty(uri.UserInfo)
                || uri.IsLoopback)
            {
                throw new ArgumentException("A public HTTPS URL without embedded credentials is required.", parameter);
            }

            return uri;
        }

        private static bool IsRedirect(HttpStatusCode statusCode)
        {
            int value = (int)statusCode;
            return value >= 300 && value <= 399;
        }

        private static bool IsValidSha256(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.Length == 64
                && value.All(character =>
                    (character >= '0' && character <= '9')
                    || (character >= 'a' && character <= 'f')
                    || (character >= 'A' && character <= 'F'));
        }

        private static string ToLowerHex(byte[] value)
        {
            return string.Concat(value.Select(item => item.ToString("x2", CultureInfo.InvariantCulture)));
        }

        private static bool FixedTimeEqualsHex(string left, string right)
        {
            if (!IsValidSha256(left) || !IsValidSha256(right))
            {
                return false;
            }

            byte[] leftBytes = Encoding.ASCII.GetBytes(left.ToLowerInvariant());
            byte[] rightBytes = Encoding.ASCII.GetBytes(right.ToLowerInvariant());
            int difference = leftBytes.Length ^ rightBytes.Length;
            for (int index = 0; index < leftBytes.Length && index < rightBytes.Length; index++)
            {
                difference |= leftBytes[index] ^ rightBytes[index];
            }

            return difference == 0;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex) when (!RecoveryRunner.IsFatal(ex))
            {
                ErrorLog.Write("Remove incomplete update file", ex);
            }
        }
    }
}
