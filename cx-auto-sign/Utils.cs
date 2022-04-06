using System;

namespace cx_auto_sign
{
    public static class Utils
    {
        private static readonly DateTime DateTime1970 = new(1970, 1, 1, 8, 0, 0);
        
        public static double GetTimestamp()
        {
            return (DateTime.Now - DateTime1970).TotalMilliseconds;
        }
    }
}