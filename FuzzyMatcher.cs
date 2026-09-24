namespace Leap;

/// <summary>
/// Matcher de subsequência com pontuação, no estilo fzf: bônus para começo de
/// palavra, sequências consecutivas e ocorrência no nome final do caminho.
/// </summary>
public static class FuzzyMatcher
{
    private const string Boundaries = "\\/ ._-";

    /// <summary>Pré-filtro barato para varrer índices grandes: query (minúscula) é subsequência?</summary>
    public static bool IsSubsequence(string queryLower, string candidate)
    {
        int qi = 0;
        for (int ci = 0; ci < candidate.Length && qi < queryLower.Length; ci++)
            if (char.ToLowerInvariant(candidate[ci]) == queryLower[qi])
                qi++;
        return qi == queryLower.Length;
    }

    /// <summary>Pontuação (maior = melhor) ou null se a query não casa.</summary>
    public static int? Score(string query, string candidate)
    {
        if (query.Length == 0) return 0;

        int qi = 0, score = 0, streak = 0;
        for (int ci = 0; ci < candidate.Length && qi < query.Length; ci++)
        {
            var boundary = ci == 0 || Boundaries.IndexOf(candidate[ci - 1]) >= 0;
            if (char.ToLowerInvariant(candidate[ci]) == char.ToLowerInvariant(query[qi]))
            {
                qi++;
                streak++;
                score += 2 + streak * 3 + (boundary ? 12 : 0);
            }
            else
            {
                streak = 0;
            }
        }

        if (qi < query.Length) return null;

        // candidatos curtos vencem em caso de empate
        score -= candidate.Length / 8;

        // query aparecendo inteira no nome final (arquivo/pasta) vale muito
        var lastSep = candidate.LastIndexOfAny(new[] { '\\', '/' });
        if (candidate[(lastSep + 1)..].Contains(query, StringComparison.OrdinalIgnoreCase))
            score += 40;

        return score;
    }
}
