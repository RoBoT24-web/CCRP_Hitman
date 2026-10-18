using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteDB;
using MySql.Data.MySqlClient;

namespace Hitman
{
    public interface IHitRepository : IDisposable
    {
        void Initialize();
        List<Hit> GetAll();
        void Add(Hit hit);
        void Remove(IEnumerable<int> ids);
    }

    public class LiteDbHitRepository : IHitRepository
    {
        private const string Collection = "hits";
        private readonly string path;
        private LiteDatabase db;

        public LiteDbHitRepository(string directory)
        {
            Directory.CreateDirectory(directory);
            path = Path.Combine(directory, "Hits.db");
        }

        public void Initialize()
        {
            db = new LiteDatabase("Filename=" + path + ";Connection=shared");
            db.GetCollection<Hit>(Collection).EnsureIndex(x => x.TargetId);
        }

        public List<Hit> GetAll()
        {
            var list = db.GetCollection<Hit>(Collection).FindAll().ToList();
            foreach (var h in list)
            {
                h.CreatedAt = h.CreatedAt.ToUniversalTime();
                h.ExpiresAt = h.ExpiresAt.ToUniversalTime();
            }
            return list;
        }

        public void Add(Hit hit) => hit.Id = db.GetCollection<Hit>(Collection).Insert(hit).AsInt32;

        public void Remove(IEnumerable<int> ids)
        {
            var col = db.GetCollection<Hit>(Collection);
            foreach (var id in ids)
                col.Delete(id);
        }

        public void Dispose() => db?.Dispose();
    }

    public class MySqlHitRepository : IHitRepository
    {
        private const string Table = "hitman_hits";
        private readonly string connectionString;

        public MySqlHitRepository(MySqlConfiguration config)
        {
            connectionString = !string.IsNullOrWhiteSpace(config.ConnectionString)
                ? config.ConnectionString
                : new MySqlConnectionStringBuilder
                {
                    Server = config.ServerAddress,
                    Port = (uint)config.ServerPort,
                    UserID = config.Username,
                    Password = config.Password,
                    Database = config.DatabaseName
                }.ConnectionString;
        }

        public void Initialize()
        {
            using (var conn = new MySqlConnection(connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "CREATE TABLE IF NOT EXISTS `" + Table + "` (" +
                        "`Id` INT NOT NULL AUTO_INCREMENT PRIMARY KEY," +
                        "`TargetId` BIGINT UNSIGNED NOT NULL," +
                        "`TargetName` VARCHAR(255) NOT NULL," +
                        "`HirerId` BIGINT UNSIGNED NOT NULL," +
                        "`HirerName` VARCHAR(255) NOT NULL," +
                        "`Bounty` DECIMAL(18,2) NOT NULL," +
                        "`CreatedAt` DATETIME NOT NULL," +
                        "`ExpiresAt` DATETIME NOT NULL," +
                        "INDEX (`TargetId`)) DEFAULT CHARSET=utf8mb4;";
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public List<Hit> GetAll()
        {
            var list = new List<Hit>();
            using (var conn = new MySqlConnection(connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT Id, TargetId, TargetName, HirerId, HirerName, Bounty, CreatedAt, ExpiresAt FROM `" + Table + "`;";
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            list.Add(new Hit
                            {
                                Id = r.GetInt32(0),
                                TargetId = r.GetUInt64(1),
                                TargetName = r.GetString(2),
                                HirerId = r.GetUInt64(3),
                                HirerName = r.GetString(4),
                                Bounty = r.GetDecimal(5),
                                CreatedAt = DateTime.SpecifyKind(r.GetDateTime(6), DateTimeKind.Utc),
                                ExpiresAt = DateTime.SpecifyKind(r.GetDateTime(7), DateTimeKind.Utc)
                            });
                        }
                    }
                }
            }
            return list;
        }

        public void Add(Hit hit)
        {
            using (var conn = new MySqlConnection(connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "INSERT INTO `" + Table + "` (TargetId, TargetName, HirerId, HirerName, Bounty, CreatedAt, ExpiresAt) " +
                        "VALUES (@t, @tn, @h, @hn, @b, @c, @e); SELECT LAST_INSERT_ID();";
                    cmd.Parameters.AddWithValue("@t", hit.TargetId);
                    cmd.Parameters.AddWithValue("@tn", hit.TargetName);
                    cmd.Parameters.AddWithValue("@h", hit.HirerId);
                    cmd.Parameters.AddWithValue("@hn", hit.HirerName);
                    cmd.Parameters.AddWithValue("@b", hit.Bounty);
                    cmd.Parameters.AddWithValue("@c", hit.CreatedAt);
                    cmd.Parameters.AddWithValue("@e", hit.ExpiresAt);
                    hit.Id = Convert.ToInt32(cmd.ExecuteScalar());
                }
            }
        }

        public void Remove(IEnumerable<int> ids)
        {
            var list = ids.ToList();
            if (list.Count == 0) return;
            using (var conn = new MySqlConnection(connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM `" + Table + "` WHERE Id IN (" + string.Join(",", list) + ");";
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public void Dispose() { }
    }
}
