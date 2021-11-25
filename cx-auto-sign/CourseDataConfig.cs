using Newtonsoft.Json.Linq;

namespace cx_auto_sign
{
    public class CourseDataConfig: BaseDataConfig
    {
        private readonly JToken _data;

        public string CourseId;
        public string ClassId;
        public string CourseName;

        public CourseDataConfig(JToken data)
        {
            _data = data;
            CourseId = GetString(nameof(CourseId));
            ClassId = GetString(nameof(ClassId));
            CourseName = GetString(nameof(CourseName));
        }

        public override JToken GetData()
        {
            return _data;
        }
    }
}