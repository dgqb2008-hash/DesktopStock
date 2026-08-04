using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DesktopStock
{
    /// <summary>
    /// 单只股票配置（代码、成本价、数量）
    /// </summary>
    public class StockConfig
    {
        public string Code { get; set; }
        public decimal CostPrice { get; set; }
        public int Quantity { get; set; }

        public StockConfig()
        {
            Code = "";
            CostPrice = 0;
            Quantity = 0;
        }

        public StockConfig(string code, decimal costPrice, int quantity)
        {
            Code = code;
            CostPrice = costPrice;
            Quantity = quantity;
        }
    }

    /// <summary>
    /// 应用设置
    /// </summary>
    public class AppSettings
    {
        public List<StockConfig> Stocks { get; set; }
        public List<int> ColumnWidths { get; set; }
        public List<bool> ColumnVisible { get; set; }
        public double Opacity { get; set; }
        public bool TopMost { get; set; }
        public int WindowWidth { get; set; }
        public int WindowHeight { get; set; }
        public int WindowLeft { get; set; }
        public int WindowTop { get; set; }
        public int RefreshInterval { get; set; }
        public bool ShowFloatingBall { get; set; }
        public int FloatingBallX { get; set; }
        public int FloatingBallY { get; set; }

        // 向后兼容：获取股票代码列表
        public List<string> StockCodes
        {
            get { return Stocks.Select(s => s.Code).ToList(); }
        }

        public AppSettings()
        {
            Stocks = new List<StockConfig>();
            ColumnWidths = new List<int>();
            ColumnVisible = new List<bool>();
            Opacity = 0.90;
            TopMost = false;
            WindowWidth = 320;
            WindowHeight = 240;
            WindowLeft = 100;
            WindowTop = 100;
            RefreshInterval = 5;
            ShowFloatingBall = false;
            FloatingBallX = -1;
            FloatingBallY = -1;
        }

        /// <summary>
        /// 从 AppSettings 创建副本（深拷贝）
        /// </summary>
        public AppSettings Clone()
        {
            return new AppSettings
            {
                Stocks = new List<StockConfig>(this.Stocks.Select(s => new StockConfig(s.Code, s.CostPrice, s.Quantity))),
                ColumnWidths = new List<int>(this.ColumnWidths),
                ColumnVisible = new List<bool>(this.ColumnVisible),
                Opacity = this.Opacity,
                TopMost = this.TopMost,
                WindowWidth = this.WindowWidth,
                WindowHeight = this.WindowHeight,
                WindowLeft = this.WindowLeft,
                WindowTop = this.WindowTop,
                RefreshInterval = this.RefreshInterval,
                ShowFloatingBall = this.ShowFloatingBall,
                FloatingBallX = this.FloatingBallX,
                FloatingBallY = this.FloatingBallY
            };
        }
    }

    /// <summary>
    /// 数据持久化服务（手动JSON读写，不依赖第三方序列化库）
    /// </summary>
    public static class StockDataStore
    {
        private static readonly string SettingsFile;
        private static readonly object _lock = new object();

        static StockDataStore()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DesktopStock");
            try { if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); } catch { }
            SettingsFile = Path.Combine(dir, "settings.json");
        }

        /// <summary>
        /// 加载设置
        /// </summary>
        public static AppSettings Load()
        {
            var settings = new AppSettings();
            try
            {
                if (!File.Exists(SettingsFile)) return settings;

                string json = File.ReadAllText(SettingsFile, Encoding.UTF8);

                settings.WindowWidth = GetInt(json, "WindowWidth", 320);
                settings.WindowHeight = GetInt(json, "WindowHeight", 240);
                settings.WindowLeft = GetInt(json, "WindowLeft", 100);
                settings.WindowTop = GetInt(json, "WindowTop", 100);
                settings.Opacity = GetDouble(json, "Opacity", 0.90);
                settings.TopMost = GetBool(json, "TopMost", false);
                settings.RefreshInterval = GetInt(json, "RefreshInterval", 5);
                settings.ShowFloatingBall = GetBool(json, "ShowFloatingBall", false);
                settings.FloatingBallX = GetInt(json, "FloatingBallX", -1);
                settings.FloatingBallY = GetInt(json, "FloatingBallY", -1);

                if (settings.Opacity < 0.3 || settings.Opacity > 1.0)
                    settings.Opacity = 0.90;
                if (settings.RefreshInterval < 2 || settings.RefreshInterval > 60)
                    settings.RefreshInterval = 5;

                // 先尝试解析新格式的 Stocks 数组
                var stocks = GetStockConfigList(json, "Stocks");
                if (stocks != null && stocks.Count > 0)
                {
                    settings.Stocks = stocks;
                }
                else
                {
                    // 向后兼容：尝试解析旧格式的 StockCodes 字符串数组
                    var oldCodes = GetStringList(json, "StockCodes");
                    if (oldCodes != null && oldCodes.Count > 0)
                    {
                        settings.Stocks = oldCodes.Select(c => new StockConfig(c, 0, 0)).ToList();
                    }
                    else
                    {
                        settings.Stocks = new List<StockConfig>();
                    }
                }

                // 解析列宽
                var columnWidths = GetIntList(json, "ColumnWidths");
                if (columnWidths != null && columnWidths.Count > 0)
                {
                    settings.ColumnWidths = columnWidths;
                }

                // 解析列可见性
                var columnVisible = GetBoolList(json, "ColumnVisible");
                if (columnVisible != null && columnVisible.Count > 0)
                {
                    settings.ColumnVisible = columnVisible;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Load error: " + ex.Message);
                return new AppSettings();
            }

            return settings;
        }

        /// <summary>
        /// 保存设置（线程安全）
        /// </summary>
        public static void Save(AppSettings settings)
        {
            if (settings == null) return;
            lock (_lock)
            {
                try
                {
                    var sb = new StringBuilder();
                    sb.Append("{");
                    sb.Append("\"WindowWidth\":").Append(settings.WindowWidth).Append(",");
                    sb.Append("\"WindowHeight\":").Append(settings.WindowHeight).Append(",");
                    sb.Append("\"WindowLeft\":").Append(settings.WindowLeft).Append(",");
                    sb.Append("\"WindowTop\":").Append(settings.WindowTop).Append(",");
                    sb.Append("\"Opacity\":").Append(settings.Opacity.ToString("F2")).Append(",");
                    sb.Append("\"TopMost\":").Append(settings.TopMost.ToString().ToLower()).Append(",");
                    sb.Append("\"RefreshInterval\":").Append(settings.RefreshInterval).Append(",");
                    sb.Append("\"ShowFloatingBall\":").Append(settings.ShowFloatingBall.ToString().ToLower()).Append(",");
                    sb.Append("\"FloatingBallX\":").Append(settings.FloatingBallX).Append(",");
                    sb.Append("\"FloatingBallY\":").Append(settings.FloatingBallY).Append(",");

                    // 股票配置列表（包含代码、成本价、数量）
                    sb.Append("\"Stocks\":[");
                    if (settings.Stocks != null)
                    {
                        for (int i = 0; i < settings.Stocks.Count; i++)
                        {
                            if (i > 0) sb.Append(",");
                            var s = settings.Stocks[i];
                            sb.Append("{\"Code\":\"").Append(EscapeJson(s.Code)).Append("\"");
                            sb.Append(",\"CostPrice\":").Append(s.CostPrice.ToString("F2"));
                            sb.Append(",\"Quantity\":").Append(s.Quantity);
                            sb.Append("}");
                        }
                    }
                    sb.Append("],");

                    // 列宽
                    sb.Append("\"ColumnWidths\":[");
                    if (settings.ColumnWidths != null)
                    {
                        for (int i = 0; i < settings.ColumnWidths.Count; i++)
                        {
                            if (i > 0) sb.Append(",");
                            sb.Append(settings.ColumnWidths[i]);
                        }
                    }
                    sb.Append("],");

                    // 列可见性
                    sb.Append("\"ColumnVisible\":[");
                    if (settings.ColumnVisible != null)
                    {
                        for (int i = 0; i < settings.ColumnVisible.Count; i++)
                        {
                            if (i > 0) sb.Append(",");
                            sb.Append(settings.ColumnVisible[i] ? "true" : "false");
                        }
                    }
                    sb.Append("]");
                    sb.Append("}");

                    // 先写临时文件再替换，防止写入中断导致损坏
                    string tmpFile = SettingsFile + ".tmp";
                    File.WriteAllText(tmpFile, sb.ToString(), Encoding.UTF8);
                    if (File.Exists(SettingsFile))
                        File.Delete(SettingsFile);
                    File.Move(tmpFile, SettingsFile);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Save error: " + ex.Message);
                }
            }
        }

        #region 手动JSON解析辅助方法

        private static int GetInt(string json, string key, int defaultValue)
        {
            string pattern = "\"" + key + "\":";
            int idx = json.IndexOf(pattern, StringComparison.Ordinal);
            if (idx < 0) return defaultValue;

            idx += pattern.Length;
            // 跳过空白
            while (idx < json.Length && (json[idx] == ' ' || json[idx] == '\t')) idx++;
            if (idx >= json.Length) return defaultValue;

            int end = idx;
            while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '-'))
                end++;

            int parsedInt;
            if (int.TryParse(json.Substring(idx, end - idx), out parsedInt))
                return parsedInt;
            return defaultValue;
        }

        private static double GetDouble(string json, string key, double defaultValue)
        {
            string pattern = "\"" + key + "\":";
            int idx = json.IndexOf(pattern, StringComparison.Ordinal);
            if (idx < 0) return defaultValue;

            idx += pattern.Length;
            while (idx < json.Length && (json[idx] == ' ' || json[idx] == '\t')) idx++;
            if (idx >= json.Length) return defaultValue;

            int end = idx;
            while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '.' || json[end] == '-'))
                end++;

            double parsedDouble;
            if (double.TryParse(
                json.Substring(idx, end - idx),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out parsedDouble))
                return parsedDouble;
            return defaultValue;
        }

        private static bool GetBool(string json, string key, bool defaultValue)
        {
            string pattern = "\"" + key + "\":";
            int idx = json.IndexOf(pattern, StringComparison.Ordinal);
            if (idx < 0) return defaultValue;

            idx += pattern.Length;
            while (idx < json.Length && (json[idx] == ' ' || json[idx] == '\t')) idx++;
            if (idx >= json.Length) return defaultValue;

            if (json.Length - idx >= 4 && json.Substring(idx, 4).ToLower() == "true")
                return true;
            return false;
        }

        private static List<string> GetStringList(string json, string key)
        {
            var list = new List<string>();
            string pattern = "\"" + key + "\":";
            int idx = json.IndexOf(pattern, StringComparison.Ordinal);
            if (idx < 0) return list;

            idx += pattern.Length;
            while (idx < json.Length && (json[idx] == ' ' || json[idx] == '\t' || json[idx] == '\r' || json[idx] == '\n'))
                idx++;
            if (idx >= json.Length || json[idx] != '[') return list;

            idx++; // skip '['
            int endArray = json.IndexOf(']', idx);
            if (endArray < 0) return list;

            // 逐个提取引号内的字符串
            while (idx < endArray)
            {
                // 找到下一个引号
                int q1 = json.IndexOf('"', idx);
                if (q1 < 0 || q1 >= endArray) break;
                int q2 = json.IndexOf('"', q1 + 1);
                if (q2 < 0 || q2 >= endArray) break;

                string val = json.Substring(q1 + 1, q2 - q1 - 1);
                if (!string.IsNullOrWhiteSpace(val))
                    list.Add(val);

                idx = q2 + 1;
            }

            return list;
        }

        /// <summary>
        /// 解析整数数组
        /// </summary>
        private static List<int> GetIntList(string json, string key)
        {
            var list = new List<int>();
            string pattern = "\"" + key + "\":";
            int idx = json.IndexOf(pattern, StringComparison.Ordinal);
            if (idx < 0) return list;

            idx += pattern.Length;
            while (idx < json.Length && (json[idx] == ' ' || json[idx] == '\t' || json[idx] == '\r' || json[idx] == '\n'))
                idx++;
            if (idx >= json.Length || json[idx] != '[') return list;

            idx++; // skip '['
            int endArray = json.IndexOf(']', idx);
            if (endArray < 0) return list;

            // 提取数组内容
            string arrayContent = json.Substring(idx, endArray - idx);
            string[] parts = arrayContent.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                string trimmed = part.Trim();
                int val;
                if (int.TryParse(trimmed, out val))
                    list.Add(val);
            }

            return list;
        }

        /// <summary>
        /// 解析布尔数组
        /// </summary>
        private static List<bool> GetBoolList(string json, string key)
        {
            var list = new List<bool>();
            string pattern = "\"" + key + "\":";
            int idx = json.IndexOf(pattern, StringComparison.Ordinal);
            if (idx < 0) return list;

            idx += pattern.Length;
            while (idx < json.Length && (json[idx] == ' ' || json[idx] == '\t' || json[idx] == '\r' || json[idx] == '\n'))
                idx++;
            if (idx >= json.Length || json[idx] != '[') return list;

            idx++; // skip '['
            int endArray = json.IndexOf(']', idx);
            if (endArray < 0) return list;

            // 提取数组内容
            string arrayContent = json.Substring(idx, endArray - idx);
            string[] parts = arrayContent.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                string trimmed = part.Trim().ToLower();
                list.Add(trimmed == "true" || trimmed == "1");
            }

            return list;
        }

        /// <summary>
        /// 解析 StockConfig 对象数组
        /// </summary>
        private static List<StockConfig> GetStockConfigList(string json, string key)
        {
            var list = new List<StockConfig>();
            string pattern = "\"" + key + "\":";
            int idx = json.IndexOf(pattern, StringComparison.Ordinal);
            if (idx < 0) return list;

            idx += pattern.Length;
            while (idx < json.Length && (json[idx] == ' ' || json[idx] == '\t' || json[idx] == '\r' || json[idx] == '\n'))
                idx++;
            if (idx >= json.Length || json[idx] != '[') return list;

            idx++; // skip '['

            // 找到匹配的 ']'
            int bracketDepth = 1;
            int endArray = idx;
            bool inString = false;
            for (int i = idx; i < json.Length; i++)
            {
                char ch = json[i];
                if (ch == '\\' && inString) { i++; continue; }
                if (ch == '"') { inString = !inString; continue; }
                if (inString) continue;
                if (ch == '[') bracketDepth++;
                else if (ch == ']')
                {
                    bracketDepth--;
                    if (bracketDepth == 0) { endArray = i; break; }
                }
            }

            if (endArray <= idx) return list;

            // 解析每个对象 {Code:"xxx",CostPrice:xx,Quantity:xx}
            int objStart = idx;
            while (objStart < endArray)
            {
                // 找到 '{'
                int braceStart = json.IndexOf('{', objStart);
                if (braceStart < 0 || braceStart >= endArray) break;

                // 找到匹配的 '}'
                int braceDepth = 1;
                int braceEnd = braceStart + 1;
                bool inStr = false;
                for (int i = braceStart + 1; i < json.Length && i <= endArray; i++)
                {
                    char ch = json[i];
                    if (ch == '\\' && inStr) { i++; continue; }
                    if (ch == '"') { inStr = !inStr; continue; }
                    if (inStr) continue;
                    if (ch == '{') braceDepth++;
                    else if (ch == '}')
                    {
                        braceDepth--;
                        if (braceDepth == 0) { braceEnd = i; break; }
                    }
                }

                if (braceEnd > braceStart)
                {
                    string objJson = json.Substring(braceStart, braceEnd - braceStart + 1);
                    var config = ParseStockConfig(objJson);
                    if (config != null && !string.IsNullOrWhiteSpace(config.Code))
                    {
                        list.Add(config);
                    }
                }

                objStart = braceEnd + 1;
            }

            return list;
        }

        /// <summary>
        /// 解析单个 StockConfig 对象的 JSON
        /// </summary>
        private static StockConfig ParseStockConfig(string objJson)
        {
            try
            {
                string code = GetStringValue(objJson, "Code");
                decimal costPrice = GetDecimalValue(objJson, "CostPrice", 0);
                int quantity = GetIntValue(objJson, "Quantity", 0);
                return new StockConfig(code, costPrice, quantity);
            }
            catch
            {
                return null;
            }
        }

        private static string GetStringValue(string json, string key)
        {
            string pattern = "\"" + key + "\":\"";
            int idx = json.IndexOf(pattern, StringComparison.Ordinal);
            if (idx < 0) return "";

            idx += pattern.Length;
            int end = json.IndexOf('"', idx);
            if (end < 0) return "";

            return json.Substring(idx, end - idx);
        }

        private static decimal GetDecimalValue(string json, string key, decimal defaultValue)
        {
            string pattern = "\"" + key + "\":";
            int idx = json.IndexOf(pattern, StringComparison.Ordinal);
            if (idx < 0) return defaultValue;

            idx += pattern.Length;
            while (idx < json.Length && (json[idx] == ' ' || json[idx] == '\t')) idx++;
            if (idx >= json.Length) return defaultValue;

            int end = idx;
            while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '.' || json[end] == '-'))
                end++;

            decimal val;
            if (decimal.TryParse(json.Substring(idx, end - idx),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out val))
                return val;
            return defaultValue;
        }

        private static int GetIntValue(string json, string key, int defaultValue)
        {
            string pattern = "\"" + key + "\":";
            int idx = json.IndexOf(pattern, StringComparison.Ordinal);
            if (idx < 0) return defaultValue;

            idx += pattern.Length;
            while (idx < json.Length && (json[idx] == ' ' || json[idx] == '\t')) idx++;
            if (idx >= json.Length) return defaultValue;

            int end = idx;
            while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '-'))
                end++;

            int val;
            if (int.TryParse(json.Substring(idx, end - idx), out val))
                return val;
            return defaultValue;
        }

        private static string EscapeJson(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        #endregion
    }
}
