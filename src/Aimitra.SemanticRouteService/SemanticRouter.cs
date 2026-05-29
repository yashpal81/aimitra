using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aimitra.SemanticRouteService.Models;

#pragma warning disable SKEXP0001
using Microsoft.SemanticKernel.Embeddings;
#pragma warning restore SKEXP0001
namespace Aimitra.SemanticRouteService
{
    public class SemanticRouter
    {
        private readonly ITextEmbeddingGenerationService _embeddingService;
        private readonly List<SemanticRoute> _registeredRoutes = new();
        private readonly float _scoreThreshold;

        public SemanticRouter(ITextEmbeddingGenerationService embeddingService, float scoreThreshold = 0.75f)
        {
            _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
            _scoreThreshold = scoreThreshold;
        }

        public async Task RegisterRouteAsync(string name, List<string> utterances)
        {
            var route = new SemanticRoute { RouteName = name, Utterances = utterances };
            var vectors = await _embeddingService.GenerateEmbeddingsAsync(utterances).ConfigureAwait(false);
            route.EmbeddedUtterances.AddRange(vectors);
            _registeredRoutes.Add(route);
        }

        public async Task<string> RouteAsync(string userQuery)
        {
            var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(userQuery).ConfigureAwait(false);

            string bestRoute = "Fallback_Generic_Agent";
            float highestScore = 0f;

            foreach (var route in _registeredRoutes)
            {
                foreach (var utteranceVector in route.EmbeddedUtterances)
                {
                    float similarity = CalculateCosineSimilarity(queryEmbedding, utteranceVector);
                    if (similarity > highestScore)
                    {
                        highestScore = similarity;
                        bestRoute = route.RouteName;
                    }
                }
            }

            return highestScore >= _scoreThreshold ? bestRoute : "Fallback_Generic_Agent";
        }

        private static float CalculateCosineSimilarity(ReadOnlyMemory<float> vecA, ReadOnlyMemory<float> vecB)
        {
            var a = vecA.Span;
            var b = vecB.Span;

            if (a.Length != b.Length)
            {
                throw new ArgumentException($"Vector dimension mismatch! Vector A has {a.Length} dims, Vector B has {b.Length} dims.");
            }

            float dotProduct = 0f;
            float mA = 0f;
            float mB = 0f;

            for (int i = 0; i < a.Length; i++)
            {
                dotProduct += a[i] * b[i];
                mA += a[i] * a[i];
                mB += b[i] * b[i];
            }

            if (mA == 0 || mB == 0)
            {
                return 0f;
            }

            return dotProduct / (MathF.Sqrt(mA) * MathF.Sqrt(mB));
        }
    }
}
