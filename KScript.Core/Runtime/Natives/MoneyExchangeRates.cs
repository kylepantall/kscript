namespace KScript.Runtime
{
    internal static class MoneyExchangeRates
    {
        private const string HostKey = "KScript.Money.ExchangeRates";
        private const string SettingsPath = "money.exchange_rates";

        internal static void Set(KScriptHost host, string from, string to, object value)
        {
            string source = CurrencyType.Of(from).Text;
            string destination = CurrencyType.Of(to).Text;
            decimal rate = MoneyType.RequireRate(value);
            if (source == destination && rate != 1)
                throw new KScriptException($"The exchange rate from {source} to itself must be 1.", "Argument");
            lock (host.Items)
            {
                if (!host.Items.TryGetValue(HostKey, out object rates))
                    host.Items[HostKey] = rates = new Dictionary<(string, string), decimal>();
                ((Dictionary<(string, string), decimal>)rates)[(source, destination)] = rate;
            }
        }

        internal static bool TryGet(KScriptHost host, string directory, string from, string to, out decimal rate)
        {
            rate = default;
            if (host == null) return false;
            lock (host.Items)
            {
                if (host.Items.TryGetValue(HostKey, out object rates)
                    && ((Dictionary<(string, string), decimal>)rates).TryGetValue((from, to), out rate)) return true;
            }
            var config = ConfigValue.For(host, directory ?? host.WorkingDirectory);
            string path = $"{SettingsPath}.{from}.{to}";
            if (!ConfigLoader.TryGet(config.Values, path, out object value)) return false;
            rate = ReadSetting(path, value);
            return true;
        }

        internal static void ValidateSettings(Dictionary<string, object> settings)
        {
            if (!ConfigLoader.TryGet(settings, SettingsPath, out object table)) return;
            if (table is not Dictionary<string, object> sources)
                throw new KScriptException($"{SettingsPath} must be a map from source currencies to destination rate maps.", "Config");
            foreach (var (from, row) in sources)
            {
                string path = $"{SettingsPath}.{from}";
                ValidateCode(from, path);
                if (row is not Dictionary<string, object> destinations)
                    throw new KScriptException($"{path} must be a map of destination currencies to rates.", "Config");
                foreach (var (to, value) in destinations)
                {
                    string pair = $"{path}.{to}";
                    ValidateCode(to, pair);
                    if (ReadSetting(pair, value) != 1 && from.Equals(to, StringComparison.OrdinalIgnoreCase))
                        throw new KScriptException($"{pair} must be 1 for a currency converted to itself.", "Config");
                }
            }
        }

        private static void ValidateCode(string code, string path)
        {
            if (code.Length != 3 || CurrencyType.Find(code) == null)
                throw new KScriptException($"{path} needs an ISO 4217 currency code.", "Config");
        }

        private static decimal ReadSetting(string path, object value)
        {
            try { return MoneyType.RequireRate(value); }
            catch (KScriptException error) { throw new KScriptException($"{path}: {error.Message}", "Config"); }
        }
    }
}
