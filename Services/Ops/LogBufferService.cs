using System.Threading.Channels;
using Application.Models;

namespace Application.Services.Ops
{
    public class LogBufferService
    {
        private readonly Channel<AppLogEntry> _channel = Channel.CreateBounded<AppLogEntry>(
            new BoundedChannelOptions(10_000)
            {
                FullMode = BoundedChannelFullMode.DropOldest
            });

        public ChannelReader<AppLogEntry> Reader => _channel.Reader;

        public void Enqueue(AppLogEntry entry)
        {
            _channel.Writer.TryWrite(entry);
        }
    }
}
