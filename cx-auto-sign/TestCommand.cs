using System;
using System.Threading.Tasks;
using CxSignHelper.Models;
using McMaster.Extensions.CommandLineUtils;
using Newtonsoft.Json.Linq;
using Serilog;

namespace cx_auto_sign
{
    [Command(Description = "测试配置")]
    public class TestCommand : CommandBase
    {
        // ReSharper disable UnassignedGetOnlyAutoProperty
        [Option("-u", Description = "指定用户名（学号）")]
        private string Username { get; }
        
        [Option("-i", Description = "配置中的 ChatId")]
        private string ChatId { get; }
        
        [Option("-t", Description = "签到类型，0 普通签到，1 拍照签到，2 二维码签到，3 手势签到，4 位置签到")]
        private SignType Type { get; }

        [Option("-d", Description = "日期时间，默认当前时间，用于拍照签到")]
        private DateTime Date { get; }
        // ReSharper restore UnassignedGetOnlyAutoProperty

        protected override async Task<int> OnExecuteAsync(CommandLineApplication app)
        {
            var appConfig = new AppDataConfig();
            var user = Username ?? appConfig.DefaultUsername;
            if (user == null)
            {
                Log.Error("没有设置用户，可以使用 -u 指定用户");
                return 1;
            }
            var userConfig = new UserDataConfig(user);
            var auConfig = new UserConfig(appConfig, userConfig);

            var work = new SignWork(auConfig);
            var log = work.Log;
            log.Information("这是一个测试");
            work.Start(Helper.GetTimestampS(), user, ChatId);
            var course = userConfig.GetCourse(ChatId ?? "");
            if (course == null)
            {
                log.Warning("该课程不在课程列表");
            }
            else
            {
                log.Information("课程：{Name}", course.CourseName);
            }
            work.SetCourseConfig(new CourseConfig(appConfig, userConfig, course));
            // ReSharper disable once InvertIf
            if (work.TestType(Type, new JObject()) && !work.TestSignSkip())
            {
                if (Type == SignType.Photo)
                {
                    await work.CourseConfig.GetImageIdAsync(null, log,
                        Date == DateTime.MinValue ? DateTime.Now : Date);
                }
                Notification.Status(log, true);
                Notification.Send(log);
                var options = work.SignOptions;
                Log.Information(@"签到信息
地址：{Address}
经纬度：{Longitude}, {Latitude}
IP：{ClientIp}", options.Address, options.Longitude, options.Latitude, options.ClientIp);
            }
            return 0;
        }
    }
}