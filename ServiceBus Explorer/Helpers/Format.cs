using System;
using System.Xml;
using ServiceBusExplorer.Localization;

namespace ServiceBusExplorer.Helpers
{
    /// <summary>Display of the Azure values: ISO 8601 durations, sizes and dates.</summary>
    internal static class Format
    {
        /// <summary>Beyond this, Service Bus means « never » (its default is TimeSpan.MaxValue, 10675199 days).</summary>
        private const double UnlimitedDays = 3650000;

        public static string Duration(string iso)
        {
            if (string.IsNullOrEmpty(iso)) return string.Empty;

            TimeSpan span;
            try
            {
                span = XmlConvert.ToTimeSpan(iso);
            }
            catch (Exception error) when (error is FormatException || error is OverflowException)
            {
                return iso;
            }

            if (span.TotalDays >= UnlimitedDays) return Strings.Unlimited;
            if (span.TotalDays >= 1) return Strings.Days(span.TotalDays);
            if (span.TotalHours >= 1) return Strings.Hours(span.TotalHours);
            if (span.TotalMinutes >= 1) return Strings.Minutes(span.TotalMinutes);
            return Strings.Seconds(span.TotalSeconds);
        }

        public static string Size(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024d:0.#} KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / 1048576d:0.#} MB";
            return $"{bytes / 1073741824d:0.##} GB";
        }

        public static string Date(DateTime? utc) => utc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? string.Empty;

        public static string YesNo(bool value) => value ? Strings.Yes : Strings.No;
    }
}
