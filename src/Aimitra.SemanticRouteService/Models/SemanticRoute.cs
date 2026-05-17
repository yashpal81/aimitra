using System;
using System.Collections.Generic;

namespace Aimitra.SemanticRouteService.Models
{
    public class SemanticRoute
    {
        public string RouteName { get; set; } = string.Empty;
        public List<string> Utterances { get; set; } = new();
        public List<ReadOnlyMemory<float>> EmbeddedUtterances { get; set; } = new();
    }
}
