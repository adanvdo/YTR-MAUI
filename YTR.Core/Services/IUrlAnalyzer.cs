using YTR.Core.Models;

namespace YTR.Core.Services;

/// <summary>
/// Analyzes URLs to determine their platform and extract identifiers.
/// </summary>
public interface IUrlAnalyzer
{
    UrlAnalysisResult Analyze(string input);
}
