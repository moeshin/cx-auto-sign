using System;
using System.Text.RegularExpressions;
using Serilog;

namespace cx_auto_sign
{
    public static class Helper
    {
        private static readonly DateTime DateTime1970 = new(1970, 1, 1, 8, 0, 0);
        private static readonly TimeSpan TimeNoon = TimeSpan.FromHours(12);

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

        private static void RulePhotoSignError(string rule)
        {
            Log.Error("拍照签到参数错误：{Rule}", rule);
        }

        private static bool RulePhotoSignDay(string rule, DateTime time)
        {
            if (rule == "")
            {
                return true;
            }
            var day = (int) time.DayOfWeek;
            if (day == 0)
            {
                day = 7;
            }
            var sDay = day.ToString();
            foreach (var s in rule.Split(','))
            {
                var arr = s.Split('-');
                var len = arr.Length;
                if (len < 2)
                {
                    if (len == 1 && sDay == arr[0])
                    {
                        return true;
                    }
                    continue;
                }
                if (!int.TryParse(arr[0], out var start) || !int.TryParse(arr[1], out var end))
                {
                    RulePhotoSignError(rule);
                    continue;
                }
                for (var i = start; i <= end; i++)
                {
                    if (i == day)
                    {
                        return true;
                    }
                }
            }
            return false;
        }
        
        private static bool RulePhotoSignTime(string rule, DateTime time)
        {
            if (rule == "")
            {
                return true;
            }
            var ts = time.TimeOfDay;
            foreach (var s in rule.Split(','))
            {
                // ReSharper disable once ConvertIfStatementToSwitchStatement
                if (s == "am")
                {
                    if (ts < TimeNoon)
                    {
                        return true;
                    }
                    continue;
                }
                if (s == "pm")
                {
                    if (ts >= TimeNoon)
                    {
                        return true;
                    }
                    continue;
                }
                var arr = s.Split('-');
                var len = arr.Length;
                if (len < 2)
                {
                    if (len == 1)
                    {
                        RulePhotoSignError(rule);
                    }
                    continue;
                }
                if (!DateTime.TryParse(arr[0], out var start)
                    || !DateTime.TryParse(arr[1], out var end))
                {
                    RulePhotoSignError(rule);
                    continue;
                }
                if (start.TimeOfDay <= ts && end.TimeOfDay >= ts)
                {
                    return true;
                }
            }
            return false;
        }

        public static bool RulePhotoSign(DateTime time, string key)
        {
            if (key == "")
            {
                return true;
            }
            var rules = key.Split("|");
            if (rules.Length != 0)
            {
                return RulePhotoSignDay(rules[0], time) && RulePhotoSignTime(rules[1], time);
            }
            RulePhotoSignError(key);
            return false;
        }

        public static string GetSignPhotoUrl(string iid, bool src = false)
        {
            return (src
                ? "https://p.ananas.chaoxing.com/star3/origin/"
                : "https://p.ananas.chaoxing.com/star3/170_220c/") + iid;
        }
    }
}