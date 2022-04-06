using System.Collections.Generic;

namespace cx_auto_sign
{
    public class SignCache                                                                      
    {
        public int Expire = 600;
        private double _recycleTime;
        private readonly Dictionary<string, double> _dict = new();

        private void Recycle()
        {
            var expire = Helper.GetTimestampS() - Expire;
            if (_recycleTime > expire)
            {
                return;
            }
            _recycleTime = Helper.GetTimestampS();
            foreach (var (k, v) in _dict)                   
            {                                               
                if (v > expire)                             
                {                                           
                    continue;                               
                }                                           
                _dict.Remove(k);                            
            }
        }

        public bool Has(string activeId)
        {
            lock (_dict)
            {
                Recycle();
                return _dict.ContainsKey(activeId);
            }
        }

        public bool Add(string activeId)
        {
            lock (_dict)
            {
                Recycle();
                return _dict.TryAdd(activeId, Helper.GetTimestampS());
            }
        }
    }
}