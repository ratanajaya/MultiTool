namespace MultiTool.Modules.MongoDbBackup;

public static class MongoDbConnectionStringValidator
{
    private const string StandardScheme = "mongodb://";
    private const string SrvScheme = "mongodb+srv://";

    public static IReadOnlyList<string> Validate(string? connectionString)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            errors.Add("Enter a MongoDB connection string.");
            return errors;
        }

        if (!string.Equals(connectionString, connectionString.Trim(), StringComparison.Ordinal))
        {
            errors.Add("The connection string cannot start or end with whitespace.");
        }

        if (connectionString.IndexOfAny(['\r', '\n', '\0']) >= 0)
        {
            errors.Add("The connection string cannot contain line breaks or null characters.");
            return errors;
        }

        var schemeLength = connectionString.StartsWith(StandardScheme, StringComparison.OrdinalIgnoreCase)
            ? StandardScheme.Length
            : connectionString.StartsWith(SrvScheme, StringComparison.OrdinalIgnoreCase)
                ? SrvScheme.Length
                : 0;
        if (schemeLength == 0)
        {
            errors.Add("Use a mongodb:// or mongodb+srv:// connection string.");
            return errors;
        }

        if (connectionString.Contains('#'))
        {
            errors.Add("The connection string cannot contain a URI fragment.");
        }

        var authorityEnd = connectionString.IndexOfAny(['/', '?', '#'], schemeLength);
        if (authorityEnd < 0)
        {
            authorityEnd = connectionString.Length;
        }

        var authority = connectionString[schemeLength..authorityEnd];
        var hostStart = authority.LastIndexOf('@') + 1;
        if (hostStart >= authority.Length || string.IsNullOrWhiteSpace(authority[hostStart..]))
        {
            errors.Add("The connection string must identify a MongoDB host.");
        }

        var pathStart = connectionString.IndexOf('/', schemeLength);
        if (pathStart >= 0)
        {
            var pathEnd = connectionString.IndexOfAny(['?', '#'], pathStart);
            if (pathEnd < 0)
            {
                pathEnd = connectionString.Length;
            }

            if (pathEnd > pathStart + 1)
            {
                errors.Add(
                    "Remove the database name from the connection string. This module backs up the entire deployment.");
            }
        }

        if (connectionString.Any(char.IsWhiteSpace))
        {
            errors.Add("Encode whitespace characters in the connection string.");
        }

        return errors.Distinct(StringComparer.Ordinal).ToArray();
    }
}
