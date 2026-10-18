using Rocket.API;

namespace Hitman
{
    public enum DatabaseType
    {
        LiteDB,
        MySQL
    }

    public class HitmanPluginConfiguration : IRocketPluginConfiguration
    {
        public DatabaseType DatabaseType { get; set; }
        public MySqlConfiguration MySqlConfiguration { get; set; }
        public HitsConfiguration HitsConfiguration { get; set; }
        public UIConfiguration UIConfiguration { get; set; }

        public void LoadDefaults()
        {
            DatabaseType = DatabaseType.LiteDB;
            MySqlConfiguration = new MySqlConfiguration
            {
                ServerAddress = "127.0.0.1",
                ServerPort = 3306,
                Username = "root",
                Password = "password",
                DatabaseName = "unturned",
                ConnectionString = string.Empty
            };
            HitsConfiguration = new HitsConfiguration
            {
                DurationInDays = 1,
                CheckIntervalInSeconds = 30,
                CanPlaceOnSelf = false,
                MinimumBounty = 100,
                RefundExpired = false,
                Placed = new HitPlacedConfiguration { Announce = true, TellTarget = true },
                Completed = new HitCompletedConfiguration { Announce = true, TellTarget = true },
                Expired = new HitExpiredConfiguration { TellTarget = true, TellHirer = true }
            };
            UIConfiguration = new UIConfiguration
            {
                AutoDisplay = true,
                MaxHitsShown = 10,
                EffectGuid = "48d317b1241a49159dd63f0d0eb0ad2f"
            };
        }
    }

    public class MySqlConfiguration
    {
        public string ServerAddress { get; set; }
        public int ServerPort { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string DatabaseName { get; set; }
        public string ConnectionString { get; set; }
    }

    public class HitsConfiguration
    {
        public double DurationInDays { get; set; }
        public int CheckIntervalInSeconds { get; set; }
        public bool CanPlaceOnSelf { get; set; }
        public decimal MinimumBounty { get; set; }
        public bool RefundExpired { get; set; }
        public HitPlacedConfiguration Placed { get; set; }
        public HitCompletedConfiguration Completed { get; set; }
        public HitExpiredConfiguration Expired { get; set; }
    }

    public class HitPlacedConfiguration
    {
        public bool Announce { get; set; }
        public bool TellTarget { get; set; }
    }

    public class HitCompletedConfiguration
    {
        public bool Announce { get; set; }
        public bool TellTarget { get; set; }
    }

    public class HitExpiredConfiguration
    {
        public bool TellTarget { get; set; }
        public bool TellHirer { get; set; }
    }

    public class UIConfiguration
    {
        public bool AutoDisplay { get; set; }
        public int MaxHitsShown { get; set; }
        public string EffectGuid { get; set; }
    }
}
