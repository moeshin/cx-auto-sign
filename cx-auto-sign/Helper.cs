using System;
using System.Text.RegularExpressions;

namespace cx_auto_sign
{
    public static class Helper
    {
        private static readonly DateTime DateTime1970 = new(1970, 1, 1, 8, 0, 0);

        private static TimeSpan GetTimestamp()
        {
            return DateTime.Now - DateTime1970;
        }
        public static double GetTimestampMs()
        {
            return GetTimestamp().TotalMilliseconds;
        }

        public static double GetTimestampS()
        {
            return GetTimestamp().TotalSeconds;
        }

        public static bool CheckUpdate(string local, string remote)
        {
            if (remote == null || local == null)
            {
                return false;
            }
            var regex = new Regex(@"^v?(\d+(?:\.\d+)+)");
            var match = regex.Match(remote);
            if (!match.Success)
            {
                return false;
            }
            var r = match.Groups[1].Value.Split('.');
            var l =local.Split('.');
            var length = Math.Min(r.Length, l.Length);
            for (var i = 0; i < length; ++i)
            {
                var v = int.Parse(r[i]) - int.Parse(l[i]);
                if (v == 0)
                {
                    continue;
                }
                return v > 0;
            }
            return r.Length > l.Length;
        }
    }
}