using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using DriverLens.Core;

namespace DriverLens.Data;

public sealed class LocalCacheStore : ILocalCacheStore
{
    private readonly string _connectionString;

    public LocalCacheStore()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dirPath = Path.Combine(localAppData, "DriverLens");
        Directory.CreateDirectory(dirPath);
        var dbPath = Path.Combine(dirPath, "cache.db");
        _connectionString = $"Data Source={dbPath}";
        InitializeDatabase();
    }

    public LocalCacheStore(string connectionString)
    {
        _connectionString = connectionString;
        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS driver_candidates (
                id TEXT PRIMARY KEY, 
                hwids TEXT, 
                compatible_ids TEXT, 
                provider TEXT,
                version TEXT, 
                release_date TEXT, 
                min_os_build INTEGER, 
                supported_arch TEXT,
                source_url TEXT, 
                sha256 TEXT, 
                authenticode_publisher TEXT,
                risk_level TEXT, 
                known_good INTEGER
            );
            CREATE TABLE IF NOT EXISTS sync_meta (
                shard_key TEXT PRIMARY KEY, 
                etag TEXT, 
                last_synced_utc TEXT
            );";
        command.ExecuteNonQuery();
    }

    public async Task ReplaceAllCandidatesAsync(IEnumerable<DriverCandidate> candidates)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        using var transaction = connection.BeginTransaction();
        try
        {
            using (var deleteCmd = connection.CreateCommand())
            {
                deleteCmd.Transaction = transaction;
                deleteCmd.CommandText = "DELETE FROM driver_candidates;";
                await deleteCmd.ExecuteNonQueryAsync();
            }

            using (var insertCmd = connection.CreateCommand())
            {
                insertCmd.Transaction = transaction;
                insertCmd.CommandText = @"
                    INSERT INTO driver_candidates (
                        id, hwids, compatible_ids, provider, version, release_date,
                        min_os_build, supported_arch, source_url, sha256,
                        authenticode_publisher, risk_level, known_good
                    ) VALUES (
                        $id, $hwids, $compatible_ids, $provider, $version, $release_date,
                        $min_os_build, $supported_arch, $source_url, $sha256,
                        $authenticode_publisher, $risk_level, $known_good
                    );";

                var idParam = insertCmd.Parameters.Add("$id", SqliteType.Text);
                var hwidsParam = insertCmd.Parameters.Add("$hwids", SqliteType.Text);
                var compIdsParam = insertCmd.Parameters.Add("$compatible_ids", SqliteType.Text);
                var providerParam = insertCmd.Parameters.Add("$provider", SqliteType.Text);
                var versionParam = insertCmd.Parameters.Add("$version", SqliteType.Text);
                var releaseDateParam = insertCmd.Parameters.Add("$release_date", SqliteType.Text);
                var minOsBuildParam = insertCmd.Parameters.Add("$min_os_build", SqliteType.Integer);
                var supportedArchParam = insertCmd.Parameters.Add("$supported_arch", SqliteType.Text);
                var sourceUrlParam = insertCmd.Parameters.Add("$source_url", SqliteType.Text);
                var sha256Param = insertCmd.Parameters.Add("$sha256", SqliteType.Text);
                var authPublisherParam = insertCmd.Parameters.Add("$authenticode_publisher", SqliteType.Text);
                var riskLevelParam = insertCmd.Parameters.Add("$risk_level", SqliteType.Text);
                var knownGoodParam = insertCmd.Parameters.Add("$known_good", SqliteType.Integer);

                foreach (var c in candidates)
                {
                    idParam.Value = c.Id;
                    hwidsParam.Value = JsonSerializer.Serialize(c.Hwids);
                    compIdsParam.Value = JsonSerializer.Serialize(c.CompatibleIds);
                    providerParam.Value = c.Provider;
                    versionParam.Value = c.Version;
                    releaseDateParam.Value = c.ReleaseDate.ToString("yyyy-MM-dd");
                    minOsBuildParam.Value = c.MinOsBuild;
                    supportedArchParam.Value = JsonSerializer.Serialize(c.SupportedArch);
                    sourceUrlParam.Value = c.SourceUrl;
                    sha256Param.Value = c.Sha256;
                    authPublisherParam.Value = (object?)c.AuthenticodePublisher ?? DBNull.Value;
                    riskLevelParam.Value = c.RiskLevel.ToString();
                    knownGoodParam.Value = c.KnownGood ? 1 : 0;

                    await insertCmd.ExecuteNonQueryAsync();
                }
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<IReadOnlyList<DriverCandidate>> GetCachedCandidatesAsync()
    {
        var list = new List<DriverCandidate>();
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT id, hwids, compatible_ids, provider, version, release_date, 
                   min_os_build, supported_arch, source_url, sha256, 
                   authenticode_publisher, risk_level, known_good 
            FROM driver_candidates;";

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var id = reader.GetString(0);
            var hwidsJson = reader.GetString(1);
            var compIdsJson = reader.GetString(2);
            var provider = reader.GetString(3);
            var version = reader.GetString(4);
            var releaseDateStr = reader.GetString(5);
            var minOsBuild = reader.GetInt32(6);
            var supportedArchJson = reader.GetString(7);
            var sourceUrl = reader.GetString(8);
            var sha256 = reader.GetString(9);
            var authPublisher = reader.IsDBNull(10) ? null : reader.GetString(10);
            var riskLevelStr = reader.GetString(11);
            var knownGood = reader.GetInt32(12) != 0;

            var hwids = JsonSerializer.Deserialize<string[]>(hwidsJson) ?? Array.Empty<string>();
            var compIds = JsonSerializer.Deserialize<string[]>(compIdsJson) ?? Array.Empty<string>();
            var supportedArch = JsonSerializer.Deserialize<string[]>(supportedArchJson) ?? Array.Empty<string>();

            DateOnly releaseDate;
            if (!DateOnly.TryParse(releaseDateStr, out releaseDate))
            {
                releaseDate = DateOnly.FromDateTime(DateTime.MinValue);
            }

            Enum.TryParse<RiskLevel>(riskLevelStr, out var risk);

            list.Add(new DriverCandidate
            {
                Id = id,
                Hwids = hwids,
                CompatibleIds = compIds,
                Provider = provider,
                Version = version,
                ReleaseDate = releaseDate,
                MinOsBuild = minOsBuild,
                SupportedArch = supportedArch,
                SourceUrl = sourceUrl,
                Sha256 = sha256,
                AuthenticodePublisher = authPublisher,
                RiskLevel = risk,
                KnownGood = knownGood
            });
        }

        return list;
    }

    public async Task<(string? ETag, DateTime LastSyncedUtc)> GetSyncMetaAsync(string shardKey)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT etag, last_synced_utc FROM sync_meta WHERE shard_key = $shard_key;";
        command.Parameters.AddWithValue("$shard_key", shardKey);

        using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            var etag = reader.IsDBNull(0) ? null : reader.GetString(0);
            var dateStr = reader.GetString(1);
            if (DateTime.TryParse(dateStr, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsedDate))
            {
                return (etag, parsedDate.ToUniversalTime());
            }
            return (etag, DateTime.MinValue);
        }
        return (null, DateTime.MinValue);
    }

    public async Task SetSyncMetaAsync(string shardKey, string? etag, DateTime syncedUtc)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO sync_meta (shard_key, etag, last_synced_utc)
            VALUES ($shard_key, $etag, $last_synced_utc)
            ON CONFLICT(shard_key) DO UPDATE SET
                etag = excluded.etag,
                last_synced_utc = excluded.last_synced_utc;";

        command.Parameters.AddWithValue("$shard_key", shardKey);
        command.Parameters.AddWithValue("$etag", (object?)etag ?? DBNull.Value);
        command.Parameters.AddWithValue("$last_synced_utc", syncedUtc.ToString("o", System.Globalization.CultureInfo.InvariantCulture));

        await command.ExecuteNonQueryAsync();
    }
}
