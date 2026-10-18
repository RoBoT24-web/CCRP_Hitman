using System;
using System.Linq;
using System.Reflection;

namespace Hitman
{
    public interface IEconomy
    {
        string Name { get; }
        decimal GetBalance(string id);
        void Add(string id, decimal amount);
    }

    // Both providers are reached via reflection so neither plugin is a hard dependency.
    public static class EconomyResolver
    {
        public static IEconomy Resolve()
        {
            return AdvancedEconomy.TryCreate() ?? (IEconomy)Uconomy.TryCreate();
        }

        private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        private static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(fullName, false);
                if (t != null) return t;
            }
            return null;
        }

        private static object GetMember(object target, Type type, string name)
        {
            var p = type.GetProperty(name, All);
            if (p != null) return p.GetValue(target, null);
            var f = type.GetField(name, All);
            return f?.GetValue(target);
        }

        private class AdvancedEconomy : IEconomy
        {
            private readonly object database;
            private readonly object events;
            private readonly MethodInfo get;
            private readonly MethodInfo increase;
            private readonly MethodInfo trigger;

            private AdvancedEconomy(object db, object ev)
            {
                database = db;
                events = ev;
                var t = db.GetType();
                get = t.GetMethod("GetPlayerBalance", new[] { typeof(string) });
                increase = t.GetMethod("IncreasePlayerBalance", new[] { typeof(string), typeof(decimal), typeof(DateTime) });
                trigger = ev?.GetType().GetMethod("TriggerOnPlayerBalanceUpdated", new[] { typeof(string), typeof(decimal), typeof(decimal) });
            }

            public string Name => "AdvancedEconomy";

            public static IEconomy TryCreate()
            {
                var pluginType = FindType("RestoreMonarchy.AdvancedEconomy.AdvancedEconomyPlugin");
                var instance = pluginType == null ? null : GetMember(null, pluginType, "Instance");
                if (instance == null) return null;
                var db = GetMember(instance, pluginType, "Database");
                if (db == null) return null;
                var ev = GetMember(instance, pluginType, "Events");
                var eco = new AdvancedEconomy(db, ev);
                return eco.get != null && eco.increase != null ? eco : null;
            }

            public decimal GetBalance(string id) => (decimal)get.Invoke(database, new object[] { id });

            public void Add(string id, decimal amount)
            {
                var balance = (decimal)increase.Invoke(database, new object[] { id, amount, DateTime.UtcNow });
                try { trigger?.Invoke(events, new object[] { id, balance, amount }); }
                catch { }
            }
        }

        private class Uconomy : IEconomy
        {
            private readonly object database;
            private readonly MethodInfo get;
            private readonly MethodInfo increase;

            private Uconomy(object db)
            {
                database = db;
                var t = db.GetType();
                get = t.GetMethod("GetBalance", new[] { typeof(string) });
                increase = t.GetMethod("IncreaseBalance", new[] { typeof(string), typeof(decimal) });
            }

            public string Name => "Uconomy";

            public static IEconomy TryCreate()
            {
                var pluginType = FindType("fr34kyn01535.Uconomy.Uconomy");
                var instance = pluginType == null ? null : GetMember(null, pluginType, "Instance");
                if (instance == null) return null;
                var db = GetMember(instance, pluginType, "Database");
                if (db == null) return null;
                var eco = new Uconomy(db);
                return eco.get != null && eco.increase != null ? eco : null;
            }

            public decimal GetBalance(string id) => (decimal)get.Invoke(database, new object[] { id });

            public void Add(string id, decimal amount) => increase.Invoke(database, new object[] { id, amount });
        }
    }
}
