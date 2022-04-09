using System;
using System.Linq;
using System.Threading.Tasks;
using CxSignHelper;
using CxSignHelper.Models;
using Newtonsoft.Json.Linq;
using Serilog;

namespace cx_auto_sign
{
    public class SignWork
    {
        private const string ImageNoneId = "041ed4756ca9fdf1f9b6dde7a83f8794";

        private SignType _signType;

        public ILogger Log;
        public CourseConfig CourseConfig { get; private set; }
        public SignOptions SignOptions { get; private set; }

        public SignWork(UserConfig auConfig)
        {

            Log = Notification.CreateLogger(auConfig, Helper.GetTimestampMs());
        }

        public void Start(object time, string user, string chatId)
        {
            Log.Information("消息时间：{Time}", time);
            Log.Information("用户：{User}", user);
            Log.Information("ChatId：{ChatId}", chatId);
        }

        public void SetCourseConfig(CourseConfig config)
        {
            CourseConfig = config;
        }

        public bool TestType(SignType type, JToken data)
        {
            _signType = type;
            Log.Information("签到类型：{Type}", GetSignTypeName(type));
            // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
            switch (type)
            {
                case SignType.Gesture:
                    Log.Information("手势：{Code}", data["signCode"]?.Value<string>());
                    break;
                case SignType.Qr:
                    Log.Warning("暂时无法二维码签到");
                    return false;
            }
            return true;
        }

        public bool TestSignSkip()
        {
            if (!CourseConfig.SignEnable)
            {
                Log.Information("因用户配置，跳过签到");
                return true;
            }
            SignOptions = CourseConfig.GetSignOptions(_signType);
            // ReSharper disable once InvertIf
            if (SignOptions == null)
            {
                Log.Warning("因用户课程配置，跳过签到");
                return true;
            }
            return false;
        }

        public string GetImagePath(DateTime now)
        {
            var array = CourseConfig.GetImageSet(now).ToArray();
            var length = array.Length;
            if (length == 0)
            {
                return null;
            }
            string path;
            if (length == 1)
            {
                path = array[0];
            }
            else
            {
                Log.Information("将从这些图片中随机选择一张进行图片签到：{Array}", array);
                path = array[new Random().Next(length)];
            }
            if (!string.IsNullOrEmpty(path))
            {
                Log.Information("将使用这张照片进行图片签到：{Path}", path);
            }
            return path;
        }

        public async Task<string> GetImageIdAsync(CxSignClient client, DateTime now)
        {
            var path = GetImagePath(now);
            if (client != null)
            {
                try
                {
                    return await client.UploadImageAsync(path);
                }
                catch (Exception e)
                {
                    Log.Error(e, "上传图片失败");
                }
            }
            Log.Information("将使用一张黑图进行图片签到");
            return ImageNoneId;
        }

        private static string GetSignTypeName(SignType type)
        {
            return type switch
            {
                SignType.Normal => "普通签到",
                SignType.Photo => "图片签到",
                SignType.Qr => "二维码签到",
                SignType.Gesture => "手势签到",
                SignType.Location => "位置签到",
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
            };
        }
    }
}