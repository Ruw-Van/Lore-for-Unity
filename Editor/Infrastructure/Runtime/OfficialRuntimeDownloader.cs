using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Lore.Unity.Core.Errors;
using Lore.Unity.Core.Paths;
using Lore.Unity.Core.Results;

namespace Lore.Unity.Infrastructure.Runtime
{
    // Only an explicit Install action invokes this class. No credentials are
    // attached, and redirects stay on HTTPS GitHub release asset hosts.
    public sealed class OfficialRuntimeDownloader : IRuntimeDownloader
    {
        private readonly RuntimeLayout _layout;
        private readonly Func<HttpMessageHandler> _handlerFactory;
        public OfficialRuntimeDownloader(RuntimeLayout layout, Func<HttpMessageHandler> handlerFactory = null)
        {
            _layout = layout ?? throw new ArgumentNullException(nameof(layout));
            _handlerFactory = handlerFactory ?? (() => new HttpClientHandler
                { AllowAutoRedirect = false, UseCookies = false });
        }

        public async Task<Result<AbsolutePath>> DownloadAsync(ValidatedRuntimeArtifact artifact,
            CancellationToken cancellationToken, IProgress<RuntimeInstallProgress> progress = null)
        {
            if (artifact == null) throw new ArgumentNullException(nameof(artifact));
            if (!IsOfficial(artifact.OfficialArtifactUrl))
                return Failure("Runtime manifest does not point to an official Lore release.");
            progress?.Report(new RuntimeInstallProgress(RuntimeInstallStage.Downloading, 0, artifact.DownloadSize));
            var file = Path.Combine(_layout.Root.Value, ".download-" + Guid.NewGuid().ToString("N"));
            var completed = false;
            try
            {
                Directory.CreateDirectory(_layout.Root.Value);
                using (var handler = _handlerFactory())
                using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) })
                {
                    var url = artifact.OfficialArtifactUrl;
                    for (var i = 0; i < 5; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead,
                            cancellationToken))
                        {
                            if (response.StatusCode == HttpStatusCode.MovedPermanently ||
                                response.StatusCode == HttpStatusCode.Redirect ||
                                response.StatusCode == HttpStatusCode.TemporaryRedirect ||
                                (int)response.StatusCode == 307 ||
                                (int)response.StatusCode == 308)
                            {
                                var location = response.Headers.Location;
                                if (location == null) return Failure("Official Lore release redirect is missing.");
                                url = new Uri(url, location);
                                if (!IsTrustedRedirect(url)) return Failure("Lore release redirected to an untrusted host.");
                                continue;
                            }
                            if (!response.IsSuccessStatusCode) return Failure("Official Lore release could not be downloaded.");
                            if (response.Content.Headers.ContentLength.HasValue &&
                                response.Content.Headers.ContentLength.Value != artifact.DownloadSize)
                                return Failure("Official Lore archive size differs from the manifest.");
                            using (var source = await response.Content.ReadAsStreamAsync())
                            using (var target = new FileStream(file, FileMode.CreateNew, FileAccess.Write,
                                FileShare.None, 81920, FileOptions.WriteThrough))
                            {
                                var buffer = new byte[81920];
                                long copied = 0;
                                int count;
                                while ((count = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                                {
                                    copied += count;
                                    if (copied > artifact.DownloadSize) return Failure("Lore archive exceeded the pinned size.");
                                    await target.WriteAsync(buffer, 0, count, cancellationToken);
                                    progress?.Report(new RuntimeInstallProgress(RuntimeInstallStage.Downloading,
                                        copied, artifact.DownloadSize));
                                }
                                await target.FlushAsync(cancellationToken);
                                if (copied != artifact.DownloadSize) return Failure("Lore archive download was incomplete.");
                            }
                            completed = true;
                            return Result<AbsolutePath>.Success(new AbsolutePath(file));
                        }
                    }
                    return Failure("Lore release redirected too many times.");
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { return Failure("Official Lore archive download timed out."); }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) when (e is HttpRequestException || e is IOException ||
                                      e is UnauthorizedAccessException || e is UriFormatException)
            { return Failure("Official Lore archive download failed."); }
            finally
            {
                // On success the manager now owns the private archive; on error
                // clean up the incomplete download here.
                if (!completed)
                    try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        private static bool IsOfficial(Uri url) => url != null && url.Scheme == Uri.UriSchemeHttps &&
            url.Host == "github.com" && url.Port == 443 &&
            url.AbsolutePath.StartsWith("/EpicGames/lore/releases/download/v", StringComparison.Ordinal) &&
            string.IsNullOrEmpty(url.UserInfo) && string.IsNullOrEmpty(url.Fragment);

        private static bool IsTrustedRedirect(Uri url) => url != null && url.Scheme == Uri.UriSchemeHttps &&
            url.Port == 443 && string.IsNullOrEmpty(url.UserInfo) && string.IsNullOrEmpty(url.Fragment) &&
            (url.Host == "release-assets.githubusercontent.com" || url.Host == "objects.githubusercontent.com" ||
             IsOfficial(url));

        private static Result<AbsolutePath> Failure(string message) => Result<AbsolutePath>.Failure(
            new LoreError(ErrorCode.NetworkUnavailable, message));
    }
}
