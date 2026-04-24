using System.Globalization;
using MySqlConnector;

namespace TaskDashboard.Api.Infrastructure.Data;

public static class JawsDbUrlParser
{
    /// <summary>
    /// JawsDB (and similar) provide <c>JAWSDB_URL</c> as
    /// <c>mysql://user:password@host:port/database</c> (password may be percent-encoded).
    /// </summary>
    public static string? ToMySqlConnectionString(string? jawsDbUrl)
    {
        if (string.IsNullOrWhiteSpace(jawsDbUrl) ||
            !jawsdbUrlStartsWithMySql(jawsDbUrl, out var rest))
        {
            return null;
        }

        var at = rest.LastIndexOf('@');
        if (at <= 0)
        {
            return null;
        }

        var userInfo = rest[..at];
        var hostAndDb = rest[(at + 1)..];

        var firstColon = userInfo.IndexOf(':');
        var user = firstColon < 0
            ? Uri.UnescapeDataString(userInfo)
            : Uri.UnescapeDataString(userInfo[..firstColon]);
        var password = firstColon < 0
            ? string.Empty
            : Uri.UnescapeDataString(userInfo[(firstColon + 1)..]);

        var slash = hostAndDb.IndexOf('/');
        if (slash < 0)
        {
            return null;
        }

        var hostPort = hostAndDb[..slash];
        var database = hostAndDb[(slash + 1)..].Split('?', 2, StringSplitOptions.None)[0];

        var port = 3306;
        var host = hostPort;
        var portColon = hostPort.LastIndexOf(':');
        if (portColon > 0
            && int.TryParse(
                hostPort[(portColon + 1)..],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var parsed))
        {
            port = parsed;
            host = hostPort[..portColon];
        }

        var b = new MySqlConnectionStringBuilder
        {
            Server = host,
            Port = (uint)port,
            UserID = user,
            Password = password,
            Database = database
        };
        return b.ConnectionString;
    }

    private static bool jawsdbUrlStartsWithMySql(string url, out string rest)
    {
        const string prefix = "mysql://";
        if (url.Length > prefix.Length && url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            rest = url[prefix.Length..];
            return true;
        }

        rest = string.Empty;
        return false;
    }
}
