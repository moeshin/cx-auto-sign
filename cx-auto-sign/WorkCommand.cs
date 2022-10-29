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

        private readonly System.Timers.Timer _heartTimer = new();

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
                {
                    if (info.Type == ReconnectionType.Initial)
                    {
                        Log.Information("CXIM 已连接");
                        _heartTimer.Interval = 60000;
                        _heartTimer.Elapsed += (_, _) =>
                        {
                            Log.Error("CXIM: 60s 内没有接收到心跳包");
                        };
                        _heartTimer.Start();
                        return;
                    }
                    Log.Warning("CXIM 重新连接，类型：{Type}", info.Type);
                });
                _ws.DisconnectionHappened.Subscribe(info => Log.Error(
                    info.Exception,
                    "CXIM 断开连接，类型：{Type}，状态：{Status}",
                    info.Type,
                    info.CloseStatus
                ));

                async void OnMessageReceived(ResponseMessage msg)
                {
                    var startTime = Helper.GetTimestampMs();
                    try
                    {
                        // 对心跳包进行屏蔽，这部分导致日志膨胀且意义不大
                        if(msg.Text.Length == 1 && msg.Text == "h"){
                            _heartTimer.Stop();
                            _heartTimer.Start();
                        }
                        else
                        {
                            Log.Information(
                                "CXIM 接收到消息 {Size}: {Message}",
                                msg.Text.Length,
                                msg.Text
                            );
                        }
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
                                ILogger log = null;
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
                                        Log.Information("ChatId: {ChatId}", chatId);
                                        if (Cxim.BytesIndexOf(pkgBytes, Cxim.CmdCourseChatFeedback.BytesCmd) != -1)
                                        {
                                            Log.Information(
                                                "收到来自《{Name}》的群聊：{State}",
                                                userConfig.GetCourseByChatId(chatId)?.CourseName,
                                                Cxim.CmdCourseChatFeedback.GetStateString(pkgBytes)
                                            );
                                            Log.Information(
                                                "ActiveId: {ActiveId}",
                                                Cxim.CmdCourseChatFeedback.GetActiveId(pkgBytes)
                                            );
                                        }
                                        else
                                        {
                                            Log.Error("解析失败，无法获取 Attachment");
                                        }
                                        continue;
                                    }

                                    var attType = att["attachmentType"]?.Value<int>();
                                    if (attType != 15)
                                    {
                                        Log.Information("ChatId: {ChatId}", chatId);
                                        switch (attType){
                                            case 1:
                                            {
                                                // 如果编辑后再发布，接收到的内容还是原始内容
                                                // 首次发布，并没有 title，只有 content
                                                // 编辑后再发布，content 将变为 title
                                                var topic = att["att_topic"];
                                                Log.Information(
                                                    "收到来自《{Name}》的主题讨论：{Content}",
                                                    topic?["att_group"]?["name"]?.Value<string>(),
                                                    (topic?["content"] ?? topic?["title"])?.Value<string>()
                                                );
                                                break;
                                            }
                                            default:
                                                Log.Error("解析失败，attachmentType != 15");
                                                Log.Error("{V}", att.ToString());
                                                break;
                                        }
                                        continue;
                                    }

                                    var attCourse = att["att_chat_course"];
                                    if (attCourse == null)
                                    {
                                        Log.Information("ChatId: {ChatId}", chatId);
                                        Log.Error("解析失败，无法获取 att_chat_course");
                                        Log.Error("{V}", att.ToString());
                                        continue;
                                    }

                                    var work = new SignWork(auConfig);
                                    log = work.Log;
                                    work.Start(startTime, user, chatId);
                                    
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

                                    var aType = attCourse["atype"]?.Value<int>();
                                    log.Information("aType: {V}", aType);
                                    var courseName = courseInfo?["coursename"]?.Value<string>();
                                    var courseId = courseInfo?["courseid"]?.Value<string>();
                                    var classId = courseInfo?["classId"]?.Value<string>();

                                    {
                                        string type;
                                        if (aType == null && attCourse["type"]?.Value<int>() == 4)
                                        {
                                            type = "直播";
                                        }
                                        else
                                        {
                                            type = attCourse["atypeName"]?.Value<string>();
                                            if (aType is not 35 or 17)
                                            {
                                                type += "活动";
                                            }
                                        }
                                        log.Information("收到来自《{Name}》的{Type}：{Title}",
                                            courseName,
                                            type,
                                            attCourse["title"]?.Value<string>()
                                        );
                                    }

                                    var attActiveType = attCourse["activeType"]?.Value<int>();
                                    log.Information("attActiveType: {V}", attActiveType);
                                    if (attActiveType != null && attActiveType != 0 && attActiveType != 2)
                                    {
                                        log.Error("不是签到活动");
                                        log = null;
                                        continue;
                                    }

                                    if (aType != 0 && aType != 2)
                                    {
                                        /*
                                        aType:
                                        0: 签到
                                        2: 签到
                                        4: 抢答
                                        11: 选人
                                        14: 问卷
                                        17: 直播
                                        23: 评分
                                        35: 分组任务
                                        42: 随堂练习
                                        43: 投票
                                        49: 白板

                                        没有通知：计时器 47
                                        没有测试：腾讯会议

                                        type: 4: 直播
                                         */
                                        log.Error("不是签到活动");
                                        // log.Warning("{V}", att.ToString());
                                        log = null;
                                        continue;
                                    }

                                    var courseKey = courseId + "-" + classId;
                                    var course = userConfig.GetCourse(courseKey) ??
                                                 userConfig.GetCourseByChatId(chatId);
                                    if (course == null)
                                    {
                                        log.Warning("该课程不在课程列表");
                                        course = new CourseDataConfig(userConfig.AddCourse(courseKey));
                                    }

                                    var needSave = false;
                                    needSave |= course.Set(nameof(CourseDataConfig.ChatId), chatId);
                                    needSave |= course.Set(nameof(CourseDataConfig.CourseName), courseName);
                                    needSave |= course.Set(nameof(CourseDataConfig.CourseId), courseId);
                                    needSave |= course.Set(nameof(CourseDataConfig.ClassId), classId);
                                    if (needSave)
                                    {
                                        userConfig.Save();
                                    }

                                    var courseConfig = new CourseConfig(appConfig, userConfig, course);
                                    work.SetCourseConfig(courseConfig);
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
                                    if (!work.TestType(signType, data))
                                    {
                                        continue;
                                    }

                                    if (enableWeiApi && !WebApi.IntervalData.Status.CxAutoSignEnabled)
                                    {
                                        log.Information("因 WebApi 设置，跳过签到");
                                        continue;
                                    }

                                    if (work.TestSignSkip())
                                    {
                                        continue;
                                    }
                                    var signOptions = work.SignOptions;

                                    // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
                                    switch (signType)
                                    {
                                        case SignType.Photo:
                                            var iid = signOptions.ImageId
                                                = await work.GetImageIdAsync(client, DateTime.Now);
                                            log.Information(
                                                "预览：{Url}",
                                                Helper.GetSignPhotoUrl(iid)
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
                                    
                                    log.Information("签到准备中");
                                    await client.PreSignAsync(activeId);

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
            var id = data["otherId"].Value<int>();
            switch (id)
            {
                case < 0 or (int)SignType.Photo or >= (int)SignType.Length:
                    return SignType.Unknown;
                case 0:
                {
                    var token = data["ifphoto"];
                    if (token?.Type == JTokenType.Integer && token.Value<int>() != 0)
                    {
                        return SignType.Photo;
                    }
                    break;
                }
            }
            return (SignType)id;
        }

        private void WsSend(string msg)
        {
            Log.Information("CXIM 发送消息 {Size}: {Message}", msg.Length, msg);
            _ws.Send(msg);
        }
    }
}
