using Application.Models;
using Serilog.Core;
using Serilog.Events;
using System.Text.Json;

namespace Application.Services.Ops
{
    public class DatabaseLogSink : ILogEventSink
    {
        private readonly LogBufferService _buffer;

        public DatabaseLogSink(LogBufferService buffer)
        {
            _buffer = buffer;
        }

        public void Emit(LogEvent logEvent)
        {
            var entry = new AppLogEntry
            {
                Timestamp = logEvent.Timestamp.UtcDateTime,
                Level = logEvent.Level.ToString(),
                Category = logEvent.Properties.TryGetValue("SourceContext", out var src)
                    ? src.ToString().Trim('"')
                    : null,
                Message = logEvent.RenderMessage(),
                Exception = logEvent.Exception?.ToString()
            };

            if (logEvent.Properties.TryGetValue("RequestPath", out var path))
                entry.RequestPath = path.ToString().Trim('"');
            if (logEvent.Properties.TryGetValue("RequestMethod", out var method))
                entry.RequestMethod = method.ToString().Trim('"');
            if (logEvent.Properties.TryGetValue("StatusCode", out var status)
                && int.TryParse(status.ToString(), out var code))
                entry.StatusCode = code;
            if (logEvent.Properties.TryGetValue("Elapsed", out var elapsed))
            {
                var raw = elapsed.ToString().Trim('"').Replace(" ms", "");
                if (double.TryParse(raw, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var ms))
                    entry.DurationMs = (int)ms;
            }
            if (logEvent.Properties.TryGetValue("UserId", out var userId))
                entry.UserId = userId.ToString().Trim('"');

            var extras = logEvent.Properties
                .Where(p => p.Key is not ("SourceContext" or "RequestPath" or "RequestMethod"
                    or "StatusCode" or "Elapsed" or "UserId" or "RequestId"))
                .ToDictionary(p => p.Key, p => p.Value.ToString());

            if (extras.Count > 0)
                entry.PropertiesJson = JsonSerializer.Serialize(extras);

            _buffer.Enqueue(entry);
        }
    }
}
