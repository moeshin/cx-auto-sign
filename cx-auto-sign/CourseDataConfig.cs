using Newtonsoft.Json.Linq;

namespace cx_auto_sign
{
    public class CourseDataConfig: BaseDataConfig
    {
        private readonly JToken _data;

        public readonly string CourseId = null;
        public readonly string ClassId = null;
        public string CourseName => GetString(nameof(CourseName));
        public string ClassName => GetString(nameof(ClassName));
        public readonly string ChatId = null;

        public CourseDataConfig(JToken data)
        {
            _data = data;
        }

        public override JToken GetData()
        {
            return _data;
        }

        public bool Set(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }
            if (GetString(key) == value)
            {
                return false;
            }
            _data[key] = value;
            return true;
        }
    }
}