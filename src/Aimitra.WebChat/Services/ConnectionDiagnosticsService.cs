using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace METASYNAPSE.WebChat.Services
{
    public class ConnectionDiagnosticsService
    {
        private readonly ConcurrentDictionary<string, string> _connections = new(StringComparer.OrdinalIgnoreCase);

        public void Register(string connectionId, string? collection)
        {
            _connections[connectionId] = collection ?? string.Empty;
        }

        public void Unregister(string connectionId)
        {
            _connections.TryRemove(connectionId, out _);
        }

        public void SetCollection(string connectionId, string? collection)
        {
            if (connectionId == null) return;
            _connections[connectionId] = collection ?? string.Empty;
        }

        public bool TryGetCollection(string connectionId, out string? collection)
        {
            if (_connections.TryGetValue(connectionId, out var c))
            {
                collection = string.IsNullOrWhiteSpace(c) ? null : c;
                return true;
            }
            collection = null;
            return false;
        }

        public IReadOnlyDictionary<string, string> GetAllConnections()
        {
            return _connections.ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        public IReadOnlyDictionary<string, int> GetCountsByCollection()
        {
            return _connections.Values
                .Select(v => string.IsNullOrWhiteSpace(v) ? "(none)" : v)
                .GroupBy(v => v)
                .ToDictionary(g => g.Key, g => g.Count());
        }
    }
}
