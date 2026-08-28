using System.Text.RegularExpressions;

namespace MultiTool.Modules.MongoDbBackup;

public static partial class MongoDbOutputRedactor
{
    public static string Redact(
        string? text,
        string connectionString,
        string? temporaryConfigPath = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var redacted = text.Replace(
            connectionString,
            "[REDACTED_MONGODB_URI]",
            StringComparison.Ordinal);
        redacted = MongoDbUriRegex().Replace(redacted, "[REDACTED_MONGODB_URI]");

        if (!string.IsNullOrWhiteSpace(temporaryConfigPath))
        {
            redacted = redacted.Replace(
                temporaryConfigPath,
                "[REDACTED_TEMP_CONFIG]",
                StringComparison.OrdinalIgnoreCase);
        }

        foreach (var secret in ExtractUserInfoParts(connectionString))
        {
            if (secret.Length >= 2)
            {
                redacted = redacted.Replace(secret, "[REDACTED]", StringComparison.Ordinal);
                try
                {
                    var decoded = Uri.UnescapeDataString(secret);
                    if (decoded.Length >= 2)
                    {
                        redacted = redacted.Replace(decoded, "[REDACTED]", StringComparison.Ordinal);
                    }
                }
                catch (UriFormatException)
                {
                    // The complete URI is already redacted. Invalid escaping is left to mongodump.
                }
            }
        }

        return redacted.Trim();
    }

    private static IEnumerable<string> ExtractUserInfoParts(string connectionString)
    {
        var schemeEnd = connectionString.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd < 0)
        {
            yield break;
        }

        var userInfoEnd = connectionString.IndexOf('@', schemeEnd + 3);
        if (userInfoEnd < 0)
        {
            yield break;
        }

        var userInfo = connectionString[(schemeEnd + 3)..userInfoEnd];
        foreach (var part in userInfo.Split(':', 2))
        {
            if (!string.IsNullOrEmpty(part))
            {
                yield return part;
            }
        }
    }

    [GeneratedRegex("mongodb(?:\\+srv)?://[^\\s\\\"'<>]+", RegexOptions.IgnoreCase)]
    private static partial Regex MongoDbUriRegex();
}
