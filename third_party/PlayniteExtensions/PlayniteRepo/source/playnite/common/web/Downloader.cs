﻿using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Playnite.Common.Web
{
    public interface IDownloader
    {
        string DownloadString(IEnumerable<string> mirrors);

        string DownloadString(string url);

        string DownloadString(string url, Encoding encoding);

        string DownloadString(string url, List<Cookie> cookies);

        string DownloadString(string url, List<Cookie> cookies, Encoding encoding);

        void DownloadString(string url, string path);

        void DownloadString(string url, string path, Encoding encoding);

        byte[] DownloadData(string url);

        void DownloadFile(string url, string path);

        void DownloadFile(IEnumerable<string> mirrors, string path);

        Task DownloadFileAsync(string url, string path, Action<DownloadProgressChangedEventArgs> progressHandler);

        Task DownloadFileAsync(IEnumerable<string> mirrors, string path, Action<DownloadProgressChangedEventArgs> progressHandler);
    }

    public class Downloader : IDownloader
    {
        private static ILogger logger = LogManager.GetLogger();
        private static readonly string playniteUserAgent = $"Playnite 10";

        public Downloader()
        {
        }

        public string DownloadString(IEnumerable<string> mirrors)
        {
            logger.Debug($"Downloading string content from multiple mirrors.");
            foreach (var mirror in mirrors)
            {
                try
                {
                    return DownloadString(mirror);
                }
                catch (Exception e)
                {
                    logger.Error(e, $"Failed to download {mirror} string.");
                }
            }

            throw new Exception("Failed to download string from all mirrors.");
        }

        public string DownloadString(string url)
        {
            return DownloadString(url, Encoding.UTF8);
        }

        public string DownloadString(string url, CancellationToken cancelToken)
        {
            logger.Debug($"Downloading string content from {url} using UTF8 encoding.");

            try
            {
                using (var webClient = new WebClient { Encoding = Encoding.UTF8 })
                using (var registration = cancelToken.Register(() => webClient.CancelAsync()))
                {
                    webClient.Headers.Add("User-Agent", playniteUserAgent);
                    return Task.Run(async () => await webClient.DownloadStringTaskAsync(url)).GetAwaiter().GetResult();
                }
            }
            catch (WebException ex) when (ex.Status == WebExceptionStatus.RequestCanceled)
            {
                logger.Warn("Download canceled.");
                return null;
            }
        }

        public string DownloadString(string url, Encoding encoding)
        {
            logger.Debug($"Downloading string content from {url} using {encoding} encoding.");
            using (var webClient = new WebClient { Encoding = encoding })
            {
                webClient.Headers.Add("User-Agent", playniteUserAgent);
                return webClient.DownloadString(url);
            }
        }

        public string DownloadString(string url, List<Cookie> cookies)
        {
            return DownloadString(url, cookies, Encoding.UTF8);
        }

        public string DownloadString(string url, List<Cookie> cookies, Encoding encoding)
        {
            logger.Debug($"Downloading string content from {url} using cookies and {encoding} encoding.");
            using (var webClient = new WebClient { Encoding = encoding })
            {
                webClient.Headers.Add("User-Agent", playniteUserAgent);
                if (cookies?.Any() == true)
                {
                    var cookieString = string.Join(";", cookies.Select(a => $"{a.Name}={a.Value}"));
                    webClient.Headers.Add(HttpRequestHeader.Cookie, cookieString);
                }

                return webClient.DownloadString(url);
            }
        }

        public void DownloadString(string url, string path)
        {
            DownloadString(url, path, Encoding.UTF8);
        }

        public void DownloadString(string url, string path, Encoding encoding)
        {
            logger.Debug($"Downloading string content from {url} to {path} using {encoding} encoding.");
            using (var webClient = new WebClient { Encoding = encoding })
            {
                webClient.Headers.Add("User-Agent", playniteUserAgent);
                var data = webClient.DownloadString(url);
                File.WriteAllText(path, data);
            }
        }

        public byte[] DownloadData(string url)
        {
            logger.Debug($"Downloading data from {url}.");
            using (var webClient = new WebClient())
            {
                webClient.Headers.Add("User-Agent", playniteUserAgent);
                return webClient.DownloadData(url);
            }
        }

        public byte[] DownloadData(string url, CancellationToken cancelToken)
        {
            logger.Debug($"Downloading data from {url}.");

            try
            {
                using (var webClient = new WebClient())
                using (var registration = cancelToken.Register(() => webClient.CancelAsync()))
                {
                    webClient.Headers.Add("User-Agent", playniteUserAgent);
                    return webClient.DownloadData(url);
                    }
                }
            catch (WebException ex) when (ex.Status == WebExceptionStatus.RequestCanceled)
            {
                logger.Warn("Download canceled.");
                return new byte[0];
            }
        }

        public void DownloadFile(string url, string path)
        {
            logger.Debug($"Downloading data from {url} to {path}.");
            FileSystem.CreateDirectory(Path.GetDirectoryName(path));
            using (var webClient = new WebClient())
            {
                webClient.Headers.Add("User-Agent", playniteUserAgent);
                webClient.DownloadFile(url, path);
            }
        }

        public void DownloadFile(string url, string path, CancellationToken cancelToken)
        {
            logger.Debug($"Downloading data from {url} to {path}.");
            FileSystem.CreateDirectory(Path.GetDirectoryName(path));

            try
            {
                using (var webClient = new WebClient())
                using (var registration = cancelToken.Register(() => webClient.CancelAsync()))
                {
                    webClient.Headers.Add("User-Agent", playniteUserAgent);
                    Task.Run(async () => await webClient.DownloadFileTaskAsync(new Uri(url), path)).Wait();
                }
            }
            catch (WebException ex) when (ex.Status == WebExceptionStatus.RequestCanceled)
            {
                logger.Warn("Download canceled.");
            }
            catch (AggregateException ae) when (ae.InnerException is WebException we && we.Status == WebExceptionStatus.RequestCanceled)
            {
                logger.Warn("Download canceled.");
            }
        }

        public async Task DownloadFileAsync(string url, string path, Action<DownloadProgressChangedEventArgs> progressHandler)
        {
            logger.Debug($"Downloading data async from {url} to {path}.");
            FileSystem.CreateDirectory(Path.GetDirectoryName(path));
            using (var webClient = new WebClient())
            {
                webClient.Headers.Add("User-Agent", playniteUserAgent);
                webClient.DownloadProgressChanged += (s, e) => progressHandler(e);
                webClient.DownloadFileCompleted += (s, e) => webClient.Dispose();
                await webClient.DownloadFileTaskAsync(url, path);
            }
        }

        public async Task DownloadFileAsync(IEnumerable<string> mirrors, string path, Action<DownloadProgressChangedEventArgs> progressHandler)
        {
            logger.Debug($"Downloading data async from multiple mirrors.");
            foreach (var mirror in mirrors)
            {
                try
                {
                    await DownloadFileAsync(mirror, path, progressHandler);
                    return;
                }
                catch (Exception e)
                {
                    logger.Error(e, $"Failed to download {mirror} file.");
                }
            }

            throw new Exception("Failed to download file from all mirrors.");
        }

        public void DownloadFile(IEnumerable<string> mirrors, string path)
        {
            logger.Debug($"Downloading data from multiple mirrors.");
            foreach (var mirror in mirrors)
            {
                try
                {
                    DownloadFile(mirror, path);
                    return;
                }
                catch (Exception e)
                {
                    logger.Error(e, $"Failed to download {mirror} file.");
                }
            }

            throw new Exception("Failed to download file from all mirrors.");
        }
    }
}
