using Playnite.SDK;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace GogOssLibraryNS
{
    public class RetryHandler : DelegatingHandler
    {
        private readonly int _maxRetries = 3;
        private readonly int _baseDelayMs = 500;
        private ILogger logger = LogManager.GetLogger();

        public RetryHandler(HttpMessageHandler innerHandler) : base(innerHandler) { }

        public RetryHandler(HttpMessageHandler innerHandler, int maxRetries, int baseDelayMs = 500) : base(innerHandler)
        {
            _maxRetries = maxRetries;
            _baseDelayMs = baseDelayMs;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken token)
        {
            HttpResponseMessage response = null;
            for (var i = 0; i < _maxRetries; i++)
            {
                try
                {
                    response = await base.SendAsync(request, token);
                    if ((int)response.StatusCode >= 500 && (int)response.StatusCode < 600)
                    {
                        var errorBody = await response.Content.ReadAsStringAsync();
                        throw new HttpRequestException($"Server error: {(int)response.StatusCode} {response.ReasonPhrase}. Body: {errorBody}");
                    }
                    else
                    {
                        return response;
                    }
                }
                catch when (!token.IsCancellationRequested)
                {
                    if (i < _maxRetries - 1)
                    {
                        int delay = (int)(_baseDelayMs * Math.Pow(2, i));
                        logger.Debug($"Retrying request.... . Attempts left: {_maxRetries - i - 1}");
                        await Task.Delay(delay, token);
                    }
                    else
                    {
                        throw;
                    }
                }
            }
            return response;
        }
    }
}
