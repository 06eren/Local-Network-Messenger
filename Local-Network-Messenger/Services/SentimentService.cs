using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Local_Network_Messenger.Services
{
    public enum SentimentTone
    {
        Neutral,
        Positive,
        Negative
    }

    public sealed record SentimentResult(SentimentTone Tone, double Score);

    public interface ISentimentService
    {
        Task<SentimentResult> AnalyzeAsync(string text, CancellationToken cancellationToken);
    }

    public static class SentimentServiceFactory
    {
        public static ISentimentService Create()
        {
            if (PythonNetRuntime.TryInitialize(out _))
            {
                try
                {
                    return new PythonNetSentimentService();
                }
                catch (Exception)
                {
                }
            }

            return new HeuristicSentimentService();
        }
    }

    public sealed class HeuristicSentimentService : ISentimentService
    {
        private static readonly HashSet<string> PositiveWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "harika", "super", "guzel", "tesekkur", "mutlu", "sevindim", "mukemmel", "iyi", "hos"
        };

        private static readonly HashSet<string> NegativeWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "kotu", "sinirli", "uzgun", "berbat", "nefret", "kizgin", "sorun", "rezalet"
        };

        public Task<SentimentResult> AnalyzeAsync(string text, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return Task.FromResult(new SentimentResult(SentimentTone.Neutral, 0));
            }

            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var score = 0;
            foreach (var raw in words)
            {
                var word = raw.Trim().ToLowerInvariant();
                if (PositiveWords.Contains(word))
                {
                    score += 1;
                }
                else if (NegativeWords.Contains(word))
                {
                    score -= 1;
                }
            }

            var tone = score switch
            {
                > 0 => SentimentTone.Positive,
                < 0 => SentimentTone.Negative,
                _ => SentimentTone.Neutral
            };

            return Task.FromResult(new SentimentResult(tone, score));
        }
    }

    public sealed class PythonNetSentimentService : ISentimentService
    {
        private readonly PythonNetScope _scope;

        public PythonNetSentimentService()
        {
            if (!PythonNetRuntime.IsInitialized)
            {
                throw new InvalidOperationException("Python.NET baslatilamadi.");
            }

            using var _ = PythonNetRuntime.AcquireGIL();
            _scope = PythonNetRuntime.CreateScope()
                ?? throw new InvalidOperationException("Python scope olusturulamadi.");
            _scope.Exec(@"
import json
positive = {""harika"", ""super"", ""guzel"", ""tesekkur"", ""mutlu"", ""sevindim"", ""mukemmel"", ""iyi"", ""hos""}
negative = {""kotu"", ""sinirli"", ""uzgun"", ""berbat"", ""nefret"", ""kizgin"", ""sorun"", ""rezalet""}

def analyze(text):
    if text is None:
        return json.dumps({""tone"": ""neutral"", ""score"": 0.0})
    text = text.lower()
    score = 0
    for token in text.split():
        if token in positive:
            score += 1
        elif token in negative:
            score -= 1
    tone = ""neutral""
    if score > 0:
        tone = ""positive""
    elif score < 0:
        tone = ""negative""
    return json.dumps({""tone"": tone, ""score"": float(score)})
");
        }

        public Task<SentimentResult> AnalyzeAsync(string text, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return Task.FromResult(new SentimentResult(SentimentTone.Neutral, 0));
            }

            using var _ = PythonNetRuntime.AcquireGIL();
            _scope.Set("lnm_text", text);
            var json = _scope.EvalToString("analyze(lnm_text)");
            if (string.IsNullOrWhiteSpace(json))
            {
                return Task.FromResult(new SentimentResult(SentimentTone.Neutral, 0));
            }

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;
                var toneText = root.GetProperty("tone").GetString() ?? "neutral";
                var score = root.TryGetProperty("score", out var scoreEl) ? scoreEl.GetDouble() : 0;
                var tone = toneText switch
                {
                    "positive" => SentimentTone.Positive,
                    "negative" => SentimentTone.Negative,
                    _ => SentimentTone.Neutral
                };
                return Task.FromResult(new SentimentResult(tone, score));
            }
            catch (System.Text.Json.JsonException)
            {
                return Task.FromResult(new SentimentResult(SentimentTone.Neutral, 0));
            }
        }
    }
}
