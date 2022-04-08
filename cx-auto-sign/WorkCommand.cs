using CxSignHelper;
using CxSignHelper.Models;
using McMaster.Extensions.CommandLineUtils;
using Newtonsoft.Json.Linq;
using Serilog;
using System;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Serilog.Core;
using Websocket.Client;

namespace cx_auto_sign
{
    [Command(Description = "工作模式, 监听签到任务并自动签到")]
    public class WorkCommand : CommandBase
    {

        // ReSharper disable UnassignedGetOnlyAutoProperty
        [Option("-u", Description = "指定用户名（学号）")]
        private string Username { get; }
        // ReSharper restore UnassignedGetOnlyAutoProperty

        private WebsocketClient _ws;

        protected override async Task<int> OnExecuteAsync(CommandLineApplication app)
        {
            await Program.CheckUpdate();
            var appConfig = new AppDataConfig();
            var user = Username ?? appConfig.DefaultUsername;
            if (user == null)
            {
                Log.Error("没有设置用户，可以使用 -u 指定用户");
                return 1;
            }

            var userConfig = new UserDataConfig(user);
            var auConfig = new UserConfig(appConfig, userConfig);
            var username = userConfig.Username;
            var password = userConfig.Password;
            var fid = userConfig.Fid;
            var signCache = new SignCache();

            Log.Information("正在登录账号：{Username}", username);
            var client = await CxSignClient.LoginAsync(username, password, fid);
            Log.Information("成功登录账号");
            var (imToken, uid) = await client.GetImTokenAsync();

            var enableWeiApi = false;
            var webApi = userConfig.WebApi;
            if (webApi != null)
            {
                // ReSharper disable once ConvertIfStatementToSwitchStatement
                if (webApi.Type == JTokenType.Boolean)
                {
                    if (webApi.Value<bool>())
                    {
                        enableWeiApi = true;
                    }
                }
                else if (webApi.Type == JTokenType.String)
                {
                    enableWeiApi = true;
                    WebApi.Startup.Rule = webApi.Value<string>();
                }

                if (enableWeiApi)
                {
                    // 启用 WebApi 服务
                    Log.Information("启用 WebApi 服务");
                    WebApi.IntervalData.Status = new WebApi.Status
                    {
                        Username = username,
                        CxAutoSignEnabled = true
                    };
                    _ = Task.Run(() => { WebApi.Program.Main(null); });
                }
            }

            // 创建 Websocket 对象，监听消息
            var exitEvent = new ManualResetEvent(false);
            var url = new Uri("wss://im-api-vip6-v2.easemob.com/ws/032/xvrhfd2j/websocket");
            using (_ws = new WebsocketClient(url, () => new ClientWebSocket
            {
                Options =
                {
                    KeepAliveInterval = Timeout.InfiniteTimeSpan
                }
            }))
            {
                _ws.ReconnectionHappened.Subscribe(info =>
                    Log.Warning("CXIM: Reconnection happened, type: {Type}", info.Type));
                _ws.DisconnectionHappened.Subscribe(info => Log.Error(
                    info.Exception,
                    "CXIM: Disconnection happened: {Type} {Status}",
                    info.Type,
                    info.CloseStatus
                ));

                async void OnMessageReceived(ResponseMessage msg)
                {
                    var startTime = Helper.GetTimestampMs();
                    try
                    {
                        Log.Information(
                            "CXIM: Message received: {Size} {Message}",
                            msg.Text.Length,
                            msg.Text
                        );
                        if (msg.Text.StartsWith("o"))
                        {
                            Log.Information("CXIM 登录");
                            WsSend(Cxim.BuildLoginPackage(uid, imToken));
                            return;
                        }

                        if (!msg.Text.StartsWith("a"))
                        {
                            return;
                        }
                        var arrMsg = JArray.Parse(msg.Text[1..]);
                        foreach (var message in arrMsg)
                        {
                            var pkgBytes = Convert.FromBase64String(message.Value<string>());
                            if (pkgBytes.Length <= 5)
                            {
                                continue;
                            }

                            var header = new byte[5];
                            Array.Copy(pkgBytes, header, 5);
                            if (header.SequenceEqual(new byte[] { 0x08, 0x00, 0x40, 0x02, 0x4a }))
                            {
                                if (Cxim.GetChatId(pkgBytes) == null)
                                {
                                    Log.Information("不是课程消息");
                                    continue;
                                }
                                Log.Information("接收到课程消息并请求获取活动信息");
                                var bytes = (byte[]) pkgBytes.Clone();
                                bytes[3] = 0x00;
                                bytes[6] = 0x1a;
                                WsSend(Cxim.Pack(bytes.Concat(new byte[] {0x58, 0x00}).ToArray()));
                                continue;
                            }
                            
                            if (!header.SequenceEqual(Cxim.BytesCourseHeader))
                            {
                                continue;
                            }

                            Log.Information("接收到课程消息");

                            string chatId;
                            try
                            {
                                chatId = Cxim.GetChatId(pkgBytes);
                            }
                            catch (Exception e)
                            {
                                throw new Exception("解析失败，无法获取 ChatId", e);
                            }

                            var sessionEnd = 11;

                            while (true)
                            {
                                Logger log = null;
                                try
                                {
                                    var index = sessionEnd;
                                    if (pkgBytes[index++] != 0x22)
                                    {
                                        break;
                                    }
                                    if ((sessionEnd = Cxim.ReadEnd2(pkgBytes, ref index)) < 0
                                        || pkgBytes[index++] != 0x08)
                                    {
                                        Log.Error("解析 Session 失败");
                                        break;
                                    }
                                    Log.Information("释放 Session");
                                    WsSend(Cxim.BuildReleaseSession(chatId, pkgBytes[index..(index += 9)]));
                                    ++index;
                                    var att = Cxim.GetAttachment(pkgBytes, ref index, sessionEnd);
                                    if (att == null)
                                    {
                                        Log.Error("解析失败，无法获取 Attachment");
                                        continue;
                                    }

                                    var attType = att["attachmentType"]?.Value<int>();
                                    if (attType != 15)
                                    {
                                        Log.Error("解析失败，attachmentType != 15");
                                        Log.Error("{V}", att.ToString());
                                        continue;
                                    }

                                    var attCourse = att["att_chat_course"];
                                    if (attCourse == null)
                                    {
                                        Log.Error("解析失败，无法获取 att_chat_course");
                                        Log.Error("{V}", att.ToString());
                                        continue;
                                    }

                                    log = Notification.CreateLogger(auConfig, Helper.GetTimestampMs());
                                    log.Information("用户：{User}", user);
                                    log.Information("消息时间：{Time}", startTime);
                                    log.Information("ChatId: {ChatId}", chatId);
                                    
                                    var activeId = attCourse["aid"]?.Value<string>();
                                    if (activeId is null or "0")
                                    {
                                        log.Error("解析失败，未找到 ActiveId");
                                        log.Error("{V}", att.ToString());
                                        log = null;
                                        continue;
                                    }
                                    log.Information("ActiveId: {ActiveId}", activeId);
                                    if (signCache.Has(activeId))
                                    {
                                        log.Warning("跳过：已签到");
                                        continue;
                                    }

                                    var courseInfo = attCourse["courseInfo"];
                                    if (courseInfo == null)
                                    {
                                        log.Error("解析失败，未找到 courseInfo");
                                        log.Error("{V}", att.ToString());
                                        log = null;
                                        continue;
                                    }
                                    var courseName = courseInfo?["coursename"]?.Value<string>();
                                    log.Information("收到来自课程 {Name} 的活动：{Type} - {Title}",
                                        courseName,
                                        attCourse["atypeName"]?.Value<string>(),
                                        attCourse["title"]?.Value<string>()
                                    );

                                    var attActiveType = attCourse["activeType"]?.Value<int>();
                                    log.Information("attActiveType: {V}", attActiveType);
                                    if (attActiveType != null && attActiveType != 0 && attActiveType != 2)
                                    {
                                        log.Error("不是签到活动");
                                        log = null;
                                        continue;
                                    }

                                    var aType = attCourse["atype"]?.Value<int>();
                                    log.Information("aType: {V}", aType);
                                    if (aType != 0 && aType != 2)
                                    {
                                        /*
                                        0: 签到
                                        2: 签到
                                        4: 抢答
                                        11: 选人
                                         */
                                        log.Error("不是签到活动");
                                        log = null;
                                        continue;
                                    }

                                    var course = userConfig.GetCourse(chatId);
                                    if (course == null)
                                    {
                                        log.Information("该课程不在课程列表");
                                        var json = userConfig.AddCourse(chatId);
                                        json[nameof(CourseDataConfig.CourseName)] = courseName;
                                        json[nameof(CourseDataConfig.CourseId)]
                                            = courseInfo?["courseid"]?.Value<string>();
                                        json[nameof(CourseDataConfig.ClassId)] 
                                            = courseInfo?["classid"]?.Value<string>();
                                        course = new CourseDataConfig(json);
                                        userConfig.Save();
                                    }

                                    var courseConfig = new CourseConfig(appConfig, userConfig, course);
                                    var data = await client.GetActiveDetailAsync(activeId);

                                    var activeType = data["activeType"]?.Value<int>();
                                    log.Information("activeType: {V}", activeType);
                                    if (activeType != 2)
                                    {
                                        log.Error("不是签到活动，activeType：{V}", activeType);
                                        log = null;
                                        continue;
                                    }

                                    var signType = GetSignType(data);
                                    log.Information("签到类型：{Type}",
                                        GetSignTypeName(signType));
                                    // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
                                    switch (signType)
                                    {
                                        case SignType.Gesture:
                                            log.Information("手势：{Code}",
                                                data["signCode"]?.Value<string>());
                                            break;
                                        case SignType.Qr:
                                            log.Warning("暂时无法二维码签到");
                                            continue;
                                    }

                                    if (enableWeiApi && !WebApi.IntervalData.Status.CxAutoSignEnabled)
                                    {
                                        log.Information("因 WebApi 设置，跳过签到");
                                        continue;
                                    }

                                    if (!courseConfig.SignEnable)
                                    {
                                        log.Information("因用户配置，跳过签到");
                                        continue;
                                    }

                                    var signOptions = courseConfig.GetSignOptions(signType);
                                    if (signOptions == null)
                                    {
                                        log.Warning("因用户课程配置，跳过签到");
                                        continue;
                                    }

                                    // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
                                    switch (signType)
                                    {
                                        case SignType.Photo:
                                            var iid = signOptions.ImageId
                                                = await courseConfig.GetImageIdAsync(client, log);
                                            log.Information(
                                                "预览：{Url}",
                                                $"https://p.ananas.chaoxing.com/star3/170_220c/{iid}"
                                            );
                                            break;
                                        case SignType.Location:
                                            if (data["ifopenAddress"].Value<int>() != 0)
                                            {
                                                signOptions.Address = data["locationText"].Value<string>();
                                                signOptions.Longitude = data["locationLongitude"].Value<string>();
                                                signOptions.Latitude = data["locationLatitude"].Value<string>();
                                                log.Information(
                                                    "教师指定签到地点：{Text} ({X}, {Y}) ±{Range}米",
                                                    signOptions.Address,
                                                    signOptions.Longitude,
                                                    signOptions.Latitude,
                                                    data["locationRange"].Value<string>()
                                                );
                                            }
                                            break;
                                    }

                                    var taskTime = data["starttime"]!.Value<long>();
                                    log.Information("任务时间: {Time}", taskTime);
                                    log.Information("签到准备完毕，耗时：{Time}ms",
                                        Helper.GetTimestampMs() - startTime);
                                    var takenTime = Helper.GetTimestampMs() - taskTime;
                                    log.Information("签到已发布：{Time}ms", takenTime);
                                    var delay = courseConfig.SignDelay;
                                    log.Information("用户配置延迟签到：{Time}s", delay);
                                    if (delay > 0)
                                    {
                                        delay = (int) (delay * 1000 - takenTime);
                                        if (delay > 0)
                                        {
                                            log.Information("将等待：{Delay}ms", delay);
                                            await Task.Delay(delay);
                                        }
                                    }

                                    log.Information("开始签到");
                                    var ok = false;
                                    var content = await client.SignAsync(activeId, signOptions);
                                    switch (content)
                                    {
                                        case  "success":
                                            content = "签到完成";
                                            ok = true;
                                            break;
                                        case "您已签到过了":
                                            ok = true;
                                            break;
                                        default:
                                            log.Error("签到失败");
                                            break;
                                    }
                                    log.Information("{V}", content);
                                    Notification.Status(log, ok);
                                    if (ok)
                                    {
                                        signCache.Add(activeId);
                                    }
                                }
                                catch (Exception e)
                                {
                                    (log ?? Log.Logger).Error(e, "CXIM 接收到课程消息时出错");
                                }
                                finally
                                {
                                    if (log != null)
                                    {
                                        Notification.Send(log);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Log.Error(e, "CXIM 接收到消息处理时出错");
                    }
                }

                _ws.MessageReceived.Subscribe(OnMessageReceived);
                await _ws.Start();
                exitEvent.WaitOne();
            }

            Console.ReadKey();

            return 0;
        }

        private static SignType GetSignType(JToken data)
        {
            var otherId = data["otherId"].Value<int>();
            switch (otherId)
            {
                case 2:
                    return SignType.Qr;
                case 3:
                    return SignType.Gesture;
                case 4:
                    return SignType.Location;
                default:
                    var token = data["ifphoto"];
                    return token?.Type == JTokenType.Integer && token.Value<int>() != 0
                        ? SignType.Photo
                        : SignType.Normal;
            }
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

        private void WsSend(string msg)
        {
            Log.Information("CXIM: Message send: {Message}", msg);
            _ws.Send(msg);
        }
    }
}
