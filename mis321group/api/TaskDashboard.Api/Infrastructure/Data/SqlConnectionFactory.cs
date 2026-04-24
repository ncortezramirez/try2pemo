using MySqlConnector;

namespace TaskDashboard.Api.Infrastructure.Data;

public sealed class SqlConnectionFactory(IConfiguration configuration)
{
    private readonly string _connectionString = ResolveConnectionString(configuration);

    private static string ResolveConnectionString(IConfiguration configuration)
    {
        var explicitCs = configuration.GetConnectionString("MySql");
        if (!string.IsNullOrWhiteSpace(explicitCs))
        {
            return explicitCs;
        }

        var jawsUrl = configuration["JAWSDB_URL"] ?? Environment.GetEnvironmentVariable("JAWSDB_URL");
        var fromJaws = JawsDbUrlParser.ToMySqlConnectionString(jawsUrl);
        if (!string.IsNullOrWhiteSpace(fromJaws))
        {
            return fromJaws;
        }

        return "server=localhost;port=3306;database=taskdashboard;user=root;password=changeme;";
    }

    public MySqlConnection CreateConnection()
    {
        return new MySqlConnection(_connectionString);
    }
}
