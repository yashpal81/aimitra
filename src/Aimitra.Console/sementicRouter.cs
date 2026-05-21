using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.SemanticKernel.Embeddings;
using Microsoft.SemanticKernel.Connectors.Google; // Using Google AI Studio

namespace Aimitra.ConsoleApp.Routing
{
    // 1. Define the structure for a Semantic Route
    public class SemanticRoute
    {
        public string RouteName { get; set; } = string.Empty;
        public List<string> Utterances { get; set; } = new();
        public List<ReadOnlyMemory<float>> EmbeddedUtterances { get; set; } = new();
    }

    public class SemanticRouter
    {
        private readonly ITextEmbeddingGenerationService _embeddingService;
        private readonly List<SemanticRoute> _registeredRoutes = new();
        private readonly float _scoreThreshold;

        public SemanticRouter(ITextEmbeddingGenerationService embeddingService, float scoreThreshold = 0.75f)
        {
            _embeddingService = embeddingService;
            _scoreThreshold = scoreThreshold;
        }

        // 2. Register and pre-calculate vectors for your routes during startup
        public async Task RegisterRouteAsync(string name, List<string> utterances)
        {
            var route = new SemanticRoute { RouteName = name, Utterances = utterances };
            
            // Batch generate vectors to optimize network overhead
            var vectors = await _embeddingService.GenerateEmbeddingsAsync(utterances);
            route.EmbeddedUtterances.AddRange(vectors);

            _registeredRoutes.Add(route);
        }

        // 3. Evaluate an incoming user query against routes
        public async Task<string> RouteAsync(string userQuery)
        {
            // Vectorize incoming message
            var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(userQuery);

            string bestRoute = "Fallback_Generic_Agent";
            float highestScore = 0f;

            // Mathematical scan (Vector Search via Cosine Similarity)
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

            // Enforce confidence threshold to prevent false-positive matching
            return highestScore >= _scoreThreshold ? bestRoute : "Fallback_Generic_Agent";
        }

        // Fast Vector Dot-Product calculation
        // private static float CalculateCosineSimilarity(ReadOnlyMemory<float> vecA, ReadOnlyMemory<float> vecB)
        // {
        //     var a = vecA.Span;
        //     var b = vecB.Span;
            
        //     float dotProduct = 0f;
        //     float mA = 0f;
        //     float mB = 0f;

        //     for (int i = 0; i < a.Length; i++)
        //     {
        //         dotProduct += a[i] * b[i];
        //         mA += a[i] * a[i];
        //         mB += b[i] * b[i];
        //     }

        //     return dotProduct / (MathF.Sqrt(mA) * MathF.Sqrt(mB));
        // }

        private static float CalculateCosineSimilarity(ReadOnlyMemory<float> vecA, ReadOnlyMemory<float> vecB)
        {
            var a = vecA.Span;
            var b = vecB.Span;
            
            // SAFETY GUARD: Ensure both vectors are the exact same dimension
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

            if (mA == 0 || mB == 0) return 0f; // Prevent division by zero

            return dotProduct / (MathF.Sqrt(mA) * MathF.Sqrt(mB));
        }
    }
}