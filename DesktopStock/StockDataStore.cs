using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DesktopStock
{
    /// <summary>
    /// 应用设置
    /// </summary>
    public class AppSettings
    {
        public List<string> StockCodes { get; set; }
        public double Opacity { get; set; }
        public bool TopMost { get; set; }
        public int WindowWidth { get; set; }
        public int WindowHeight { get; set; }
        public int WindowLeft { get; set; }
        public int WindowTop { get; set; }
        public int RefreshInterval { get; set; }

        public AppSettings()
        {
            StockCodes = new List<string>();
            Opacity = 0.90;
            TopMost = false;
            WindowWidth = 320;
            WindowHeight = 240;
            WindowLeft = 100;
            WindowTop = 100;
            RefreshInterval = 5;
        }

        /// <summary>
        /// 从 AppSettings 创建副本（深拷贝）
        /// </summary>
        public AppSettings Clone()
        {
            return new AppSettings
            {
                StockCodes = new List<string>(this.StockCodes),
                Opacity = this.Opacity,
                TopMost = this.TopMost,
                WindowWidth = this.WindowWidth,
                WindowHeight = this.WindowHeight,
                WindowLeft = this.WindowLeft,
                WindowTop = this.WindowTop,
                RefreshInterval = this.RefreshInterval
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

                if (settings.Opacity < 0.3 || settings.Opacity > 1.0)
                    settings.Opacity = 0.90;
                if (settings.RefreshInterval < 2 || settings.RefreshInterval > 60)
                    settings.RefreshInterval = 5;

                // 解析股票代码数组
                settings.StockCodes = GetStringList(json, "StockCodes");
                if (settings.StockCodes == null)
                    settings.StockCodes = new List<string>();
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

                    // 股票代码列表
                    sb.Append("\"StockCodes\":[");
                    if (settings.StockCodes != null)
                    {
                        for (int i = 0; i < settings.StockCodes.Count; i++)
                        {
                            if (i > 0) sb.Append(",");
                            sb.Append("\"").Append(EscapeJson(settings.StockCodes[i])).Append("\"");
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

        private static string EscapeJson(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        #endregion
    }
}
