using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace METASYNAPSE.WebChat.Services
{
    /// <summary>
    /// Stores mappings between external session ids (from LWC / external hosts)
    /// and internal session collection names used by the kernel / SignalR layer.
    /// </summary>
    public class ExternalSessionService
    {
        private readonly ConcurrentDictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase);

        public void Register(string externalSessionId, string? collection)
        {
            if (string.IsNullOrWhiteSpace(externalSessionId)) return;
            _map[externalSessionId] = collection ?? string.Empty;
        }

        public bool TryGetCollection(string externalSessionId, out string? collection)
        {
            if (_map.TryGetValue(externalSessionId, out var c))
            {
                collection = string.IsNullOrWhiteSpace(c) ? null : c;
                return true;
            }
            collection = null;
            return false;
        }

        public bool Unregister(string externalSessionId)
        {
            return _map.TryRemove(externalSessionId, out _);
        }

        public IReadOnlyDictionary<string, string> GetAll() => _map.ToDictionary(kv => kv.Key, kv => kv.Value);
    }
}
