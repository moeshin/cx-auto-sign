using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CxSignHelper;
using Newtonsoft.Json.Linq;
using Serilog;

namespace cx_auto_sign
{
    public class UserDataConfig: BaseDataConfig
    {
        private const string Dir = "Configs";
        private const string KeyCourse = "Course";

        private readonly string _path;

        private readonly JObject _data;
        private readonly JObject _courses;

        public readonly string Username;
        public readonly string Password;
        public readonly string Fid;
        public readonly JToken WebApi;

        private UserDataConfig(FileSystemInfo file): this(file.FullName, null, null, null)
        {
            _path = file.FullName;
            Username = GetString(nameof(Username));
            Password = GetString(nameof(Password));
            Fid = GetString(nameof(Fid));
        }

        public UserDataConfig(string user, string pass = null, string fid = null)
            : this(GetPath(user), user, pass, fid) { }

        private UserDataConfig(string path, string user, string pass, string fid)
        {
            _path = path;
            if (File.Exists(_path))
            {
                _data = JObject.Parse(File.ReadAllText(_path));
                _courses = (JObject) (_data[KeyCourse] ?? (_data[KeyCourse] = new JObject()));
                if (user != null)
                {
                    SetAuth(nameof(Username), user);
                    SetAuth(nameof(Password), pass);
                    SetAuth(nameof(Fid), fid);
                }
                Username = GetString(nameof(Username));
                Password = GetString(nameof(Password));
                Fid = GetString(nameof(Fid));
                WebApi = Get(nameof(WebApi));
            }
            else
            {
                if (pass == null)
                {
                    throw new Exception("不存在该用户的配置");
                }
                _data = new JObject
                {
                    [nameof(Username)] = Username = user,
                    [nameof(Password)] = Password = pass,
                    [nameof(Fid)] = Fid = fid,
                    [nameof(WebApi)] = WebApi = false,
                    [KeyCourse] = _courses = new JObject()
                };
            }
        }

        public override JToken GetData()
        {
            return _data;
        }

        public CourseDataConfig GetCourse(string key)
        {
            var course = _courses?[key];
            return course == null ? null : new CourseDataConfig(course);
        }

        public CourseDataConfig GetCourseByChatId(string chatId)
        {
            var cfg = GetCourse(chatId);
            if (cfg != null)
            {
                return cfg;
            }
            JToken course = null;
            // ReSharper disable once InvertIf
            if (_courses != null)
            {
                foreach (var (_, value) in _courses)
                {
                    // ReSharper disable once InvertIf
                    if (value?[nameof(CourseDataConfig.ChatId)]?.Value<string>() == chatId)
                    {
                        course = value;
                        break;
                    }
                }
            }
            return course == null ? null : new CourseDataConfig(course);
        }

        public JObject AddCourse(string key)
        {
            var course = new JObject();
            _courses[key] = course;
            return course;
        }

        public void Save()
        {
            if (!Directory.Exists(Dir))
            {
                Log.Debug("没有用户配置文件夹，并创建：{Dir}", Dir);
                Directory.CreateDirectory(Dir);
                Log.Debug("已创建用户配置文件夹：{Dir}", Dir);
            }
            Log.Debug("保存用户配置中...");
            File.WriteAllText(_path, _data.ToString());
            Log.Debug("已保存用户配置");
        }

        private void SetAuth(string key, string val)
        {
            if (val == null)
            {
                return;
            }

            var token = Get(key);
            if (token == null || token.Type == JTokenType.String && token.Value<string>() != key)
            {
                _data[key] = val;
            }
        }

        private static string GetPath(string user)
        {
            return Dir + "/" + user + ".json5";
        }

        public async Task UpdateAsync(CxSignClient client = null)
        {
            if (client == null)
            {
                Log.Information("正在登录账号：{Username}", Username);
                client = await CxSignClient.LoginAsync(Username, Password, Fid);
                Log.Information("成功登录账号");
            }

            var regex = new Regex(@"^\d+$");
            var convertList = new List<(string, string)>();
            foreach (var (key, value) in _courses)
            {
                if (!regex.IsMatch(key) || value == null)
                {
                    continue;
                }
                var courseId = value[nameof(CourseDataConfig.CourseId)]?.Value<string>();
                var classId = value[nameof(CourseDataConfig.ClassId)]?.Value<string>();
                if (string.IsNullOrWhiteSpace(courseId) || string.IsNullOrWhiteSpace(classId))
                {
                    continue;
                }
                if (value[nameof(CourseDataConfig.ChatId)]?.Value<string>() == null)
                {
                    value[nameof(CourseDataConfig.ChatId)] = key;
                }
                var newKey = courseId + "-" + classId;
                Log.Information("兼容旧版，转换键名：{Key} -> {NewKey}", 
                    key, newKey);
                convertList.Add((key, newKey));
            }
            if (convertList.Count > 0)
            {
                foreach (var (key, newKey) in convertList)
                {
                    _courses[newKey] = _courses[key];
                    _courses.Remove(key);
                }
                Log.Information("保存转换结果");
                Save();
            }

            Log.Information("获取课程数据中...");
            await client.GetCoursesAsync(_courses);
            foreach (var (_, course) in _courses)
            {
                if (course == null)
                {
                    continue;
                }
                Log.Information("发现课程：{CourseName}-{ClassName} ({CourseId}, {ClassId})",
                    (string) course[nameof(CourseDataConfig.CourseName)],
                    (string) course[nameof(CourseDataConfig.ClassName)],
                    (string) course[nameof(CourseDataConfig.CourseId)],
                    (string) course[nameof(CourseDataConfig.ClassId)]
                );
            }
            Save();
        }

        public static async Task UpdateAsync(string user)
        {
            var path = GetPath(user);
            var file = new FileInfo(path);
            if (!file.Exists)
            {
                throw new Exception("不存在该用户的配置");
            }
            await new UserDataConfig(file).UpdateAsync();
        }

        public static async Task UpdateAllAsync()
        {
            var dir = new DirectoryInfo(Dir);
            if (!dir.Exists)
            {
                return;
            }
            var infos = dir.GetFiles();
            foreach (var file in infos)
            {
                try
                {
                    await new UserDataConfig(file).UpdateAsync();
                }
                catch (Exception e)
                {
                    Log.Error(e, "更新失败");
                }
            }
        }
    }
}