using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Primitives;

namespace MockServer.Services
{
    public class DynamicActionDescriptorChangeProvider : IActionDescriptorChangeProvider
    {
        public static DynamicActionDescriptorChangeProvider Instance { get; } = new();

        private CancellationTokenSource _tokenSource = new();
        public CancellationTokenSource TokenSource
        {
            get => _tokenSource;
            private set => _tokenSource = value;
        }

        public IChangeToken GetChangeToken()
        {
            return new CancellationChangeToken(_tokenSource.Token);
        }   

        public void NotifyChanges()
        {
            var previous = Interlocked.Exchange(ref _tokenSource, new CancellationTokenSource());
            previous.Cancel();
            previous.Dispose();
        }
    }
}
