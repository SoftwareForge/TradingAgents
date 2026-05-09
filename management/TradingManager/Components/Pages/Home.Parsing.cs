using TradingManager.Components.Models;

namespace TradingManager.Components.Pages;

public partial class Home
{
    private void ExtractMetricsFromText(string text, string ts, string eventType, string section, string team)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var confidence = ComputeConfidence(eventType, section, team);

        // Price / valuation
        TryCapture(text, ts, "Current Price", @"(?i)(?:\bcurrent(?:\s+stock)?\s+price\b|\blast\s+price\b|\bprice\s+now\b|\btrading\s+at\b|\bcurrently\s+at\b|\bclosed\s+at\b|\bshares?\s+at\b)\D{0,12}\$?\s*([0-9]+(?:[.,][0-9]+)?)", confidence);
        TryCapture(text, ts, "Target Price", @"(?i)(?:target price|price target)\D{0,12}\$?\s*([0-9]+(?:[.,][0-9]+)?)", confidence);
        TryCapture(text, ts, "P/E Ratio", @"(?i)(?:p\/e|pe ratio|price[- ]to[- ]earnings)\D{0,12}([0-9]+(?:[.,][0-9]+)?)", confidence);
        TryCapture(text, ts, "Forward P/E", @"(?i)(?:forward p\/e|forward pe)\D{0,12}([0-9]+(?:[.,][0-9]+)?)", confidence);
        TryCapture(text, ts, "PEG Ratio", @"(?i)(?:peg ratio|peg)\D{0,12}([0-9]+(?:[.,][0-9]+)?)", confidence);
        TryCapture(text, ts, "P/S Ratio", @"(?i)(?:p\/s|price[- ]to[- ]sales)\D{0,12}([0-9]+(?:[.,][0-9]+)?)", confidence);
        TryCapture(text, ts, "P/B Ratio", @"(?i)(?:p\/b|price[- ]to[- ]book)\D{0,12}([0-9]+(?:[.,][0-9]+)?)", confidence);

        // Fundamental performance
        TryCapture(text, ts, "Revenue", @"(?i)(?:revenue|sales)\D{0,20}\$?\s*([0-9]+(?:[.,][0-9]+)?\s*(?:trillion|billion|million|tn|bn|m|k)?)", confidence);
        TryCapture(text, ts, "Revenue Growth", @"(?i)(?:revenue growth|sales growth)\D{0,12}([+-]?[0-9]+(?:[.,][0-9]+)?\s*%)", confidence);
        TryCapture(text, ts, "EPS", @"(?i)(?:eps|earnings per share)\D{0,12}\$?\s*([0-9]+(?:[.,][0-9]+)?)", confidence);
        TryCapture(text, ts, "EPS Growth", @"(?i)(?:eps growth)\D{0,12}([+-]?[0-9]+(?:[.,][0-9]+)?\s*%)", confidence);
        TryCapture(text, ts, "Gross Margin", @"(?i)(?:gross margin)\D{0,12}([0-9]+(?:[.,][0-9]+)?\s*%)", confidence);
        TryCapture(text, ts, "Operating Margin", @"(?i)(?:operating margin)\D{0,12}([0-9]+(?:[.,][0-9]+)?\s*%)", confidence);
        TryCapture(text, ts, "Net Margin", @"(?i)(?:net margin)\D{0,12}([0-9]+(?:[.,][0-9]+)?\s*%)", confidence);
        TryCapture(text, ts, "ROE", @"(?i)(?:roe|return on equity)\D{0,12}([0-9]+(?:[.,][0-9]+)?\s*%)", confidence);
        TryCapture(text, ts, "ROA", @"(?i)(?:roa|return on assets)\D{0,12}([0-9]+(?:[.,][0-9]+)?\s*%)", confidence);

        // Balance sheet / cash flow
        TryCapture(text, ts, "Market Cap", @"(?i)(?:market cap|market capitalization)\D{0,20}\$?\s*([0-9]+(?:[.,][0-9]+)?\s*(?:trillion|billion|million|tn|bn|m)?)", confidence);
        TryCapture(text, ts, "Debt/Equity", @"(?i)(?:debt\/equity|debt-to-equity)\D{0,12}([0-9]+(?:[.,][0-9]+)?)", confidence);
        TryCapture(text, ts, "Current Ratio", @"(?i)(?:current ratio)\D{0,12}([0-9]+(?:[.,][0-9]+)?)", confidence);
        TryCapture(text, ts, "Free Cash Flow", @"(?i)(?:free cash flow|fcf)\D{0,20}\$?\s*([0-9]+(?:[.,][0-9]+)?\s*(?:trillion|billion|million|tn|bn|m|k)?)", confidence);
        TryCapture(text, ts, "FCF Margin", @"(?i)(?:fcf margin|free cash flow margin)\D{0,12}([0-9]+(?:[.,][0-9]+)?\s*%)", confidence);

        // Technicals / trading
        TryCapture(text, ts, "Volume", @"(?i)(?:volume)\D{0,12}([0-9]+(?:[.,][0-9]+)?\s*(?:million|billion|m|bn)?)", confidence);
        TryCapture(text, ts, "Avg Volume", @"(?i)(?:average volume|avg volume)\D{0,12}([0-9]+(?:[.,][0-9]+)?\s*(?:million|billion|m|bn)?)", confidence);
        TryCapture(text, ts, "RSI", @"(?i)\brsi\D{0,8}([0-9]+(?:[.,][0-9]+)?)", confidence);
        TryCapture(text, ts, "MACD", @"(?i)\bmacd\D{0,12}([+-]?[0-9]+(?:[.,][0-9]+)?)", confidence);
        TryCapture(text, ts, "SMA 50", @"(?i)(?:sma\s*50|50[- ]day sma)\D{0,12}([0-9]+(?:[.,][0-9]+)?)", confidence);
        TryCapture(text, ts, "SMA 200", @"(?i)(?:sma\s*200|200[- ]day sma)\D{0,12}([0-9]+(?:[.,][0-9]+)?)", confidence);
        TryCapture(text, ts, "EMA 20", @"(?i)(?:ema\s*20|20[- ]day ema)\D{0,12}([0-9]+(?:[.,][0-9]+)?)", confidence);
        TryCapture(text, ts, "52W High", @"(?i)(?:52w high|52-week high)\D{0,12}([0-9]+(?:[.,][0-9]+)?)", confidence);
        TryCapture(text, ts, "52W Low", @"(?i)(?:52w low|52-week low)\D{0,12}([0-9]+(?:[.,][0-9]+)?)", confidence);

        // Risk / volatility
        TryCapture(text, ts, "Beta", @"(?i)\bbeta\D{0,8}([0-9]+(?:[.,][0-9]+)?)", confidence);
        TryCapture(text, ts, "ATR", @"(?i)\batr\D{0,8}([0-9]+(?:[.,][0-9]+)?)", confidence);
        TryCapture(text, ts, "Volatility", @"(?i)(?:volatility)\D{0,12}([0-9]+(?:[.,][0-9]+)?\s*%)", confidence);
    }

    private void TryCapture(string text, string ts, string name, string pattern, int confidence)
    {
        var match = System.Text.RegularExpressions.Regex.Match(text, pattern);
        if (!match.Success || match.Groups.Count < 2)
        {
            return;
        }

        var value = match.Groups[1].Value.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (_keyMetrics.TryGetValue(name, out var existing) && existing.Confidence > confidence)
        {
            return;
        }

        _keyMetrics[name] = new MetricValue
        {
            Value = value,
            UpdatedAt = ts,
            Confidence = confidence,
        };
    }

    private static int ComputeConfidence(string eventType, string section, string team)
    {
        var score = 1;
        var type = eventType?.ToLowerInvariant() ?? "";
        var sec = section?.ToLowerInvariant() ?? "";
        var t = team?.ToLowerInvariant() ?? "";

        if (type == "section")
        {
            score += 3;
        }
        if (sec.Contains("fundamentals") || sec.Contains("market_report") || sec.Contains("news_report"))
        {
            score += 3;
        }
        if (t.Contains("analyst") || t.Contains("research"))
        {
            score += 2;
        }

        return score;
    }

    private void ExtractSignals(string text, string team, string section, string ts)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var patterns = new (string signal, string pattern)[]
        {
            ("BUY", @"(?i)\b(strong\s+buy|buy)\b"),
            ("SELL", @"(?i)\b(strong\s+sell|sell)\b"),
            ("HOLD", @"(?i)\b(hold|neutral|wait)\b"),
        };

        foreach (var (signal, pattern) in patterns)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(text, pattern))
            {
                continue;
            }

            var source = !string.IsNullOrWhiteSpace(section) ? $"{team} / {section}" : team;
            var context = text.Length > 220 ? text[..220] + "..." : text;
            if (_signals.Any(s => s.Signal == signal && s.Source == source && s.Context == context))
            {
                continue;
            }

            _signals.Add(new SignalHit
            {
                Signal = signal,
                Source = source,
                Context = context,
                UpdatedAt = ParseTimestamp(ts),
            });
        }
    }

    private static DateTime ParseTimestamp(string ts)
    {
        if (DateTime.TryParse(ts, out var dt))
        {
            return dt.ToUniversalTime();
        }
        return DateTime.UtcNow;
    }
}
