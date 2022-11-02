using System;
using System.Text;
using System.Text.RegularExpressions;
using CxSignHelper;
using MailKit.Net.Smtp;
using MimeKit;
using Newtonsoft.Json.Linq;
using RestSharp;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace cx_auto_sign
{
    public class Notification : ILogEventSink
    {
        private const string Title = "cx-auto-sign 自动签到通知";
        private static readonly string StartTime = ((long) Helper.GetTimestampMs()).ToString();
        private readonly StringBuilder _stringBuilder = new();
        private readonly UserConfig _userConfig;
        private readonly Logger _log;

        private string _title;
        private bool? _ok;

        private Notification(Logger log, UserConfig userConfig)
        {
            _userConfig = userConfig;
            _log = log;
        }

        public void Emit(LogEvent logEvent)
        {
            if (logEvent.Level == LogEventLevel.Error)
            {
                bool? b;
                if ((b = GetBool(logEvent, nameof(Status))) != null)
                {
                    _ok = b;
                    return;
                }
                if ((b = GetBool(logEvent, nameof(Send))) != null && b == true)
                {
                    if (_stringBuilder.Length == 0)
                    {
                        return;
                    }
                    if (_ok != null)
                    {
                        var status = _ok == true ? '✔' : '✖';
                        _stringBuilder.Insert(0, "自动签到 " + status + "\n程序开始运行时间戳：" + StartTime + '\n');
                        _title = Title + ' ' + status;
                    }
                    var content = _stringBuilder.ToString();
                    NotifyByEmail(content);
                    NotifyByServerChan(content);
                    NotifyByPushPlus(content);
                    NotifyByTelegramBot(content);
                    NotifyByBark(content);
                    NotifyByWechatWorkApp(content);
                    return;
                }
            }
            _log.Write(logEvent);
            _stringBuilder
                .Append(logEvent.Timestamp.ToString("HH:mm:ss"))
                .Append(' ')
                .Append(logEvent.Level.ToString()[0])
                .Append(' ')
                .Append(logEvent.RenderMessage())
                .Append('\n');
            if (logEvent.Exception != null)
            {
                _stringBuilder.Append(logEvent.Exception).Append('\n');
            }
        }

        private string GetTitle()
        {
            return _title ?? Title;
        }

        private string GetContent(string content)
        {
            return GetTitle() + "\n" + content;
        }

        private static bool? GetBool(LogEvent logEvent, string name)
        {
            if (logEvent.Properties.TryGetValue(name, out var value)
                && bool.TryParse(value.ToString(), out var b))
            {
                return b;
            }
            return null;
        }

        private static void WriteBool(ILogger log, string name, bool b)
        {
            log.Write(new LogEvent(DateTimeOffset.Now, LogEventLevel.Error, null, MessageTemplate.Empty,
                new LogEventProperty[]
                {
                    new(name, new ScalarValue(b))
                })
            );
        }

        public static void Status(ILogger log, bool ok)
        {
            WriteBool(log, nameof(Status), ok);
        }

        public static void Send(ILogger log)
        {
            WriteBool(log, nameof(Send), true);
        }

        private static void NotifyByEmail(string subject, string text, string email, string host, int port,
            string user, string pass, bool secure = false)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("cx-auto-sign", user));
            message.To.Add(new MailboxAddress("cx-auto-sign", email));
            message.Subject = subject;
            message.Body = new TextPart("plain")
            {
                Text = text
            };
            using var client = new SmtpClient();
            client.Connect(host, port, secure);
            client.Authenticate(user, pass);
            client.Send(message);
            client.Disconnect(true);
        }

        private void NotifyByEmail(string content)
        {
            const string name = "邮件";
            if (string.IsNullOrEmpty(_userConfig.Email))
            {
                _log.Warning(
                    "由于 {Key} 为空，没有发送 {Name} 通知",
                    nameof(UserConfig.Email),
                    name
                );
                return;
            }
            if (string.IsNullOrEmpty(_userConfig.SmtpHost) ||
                string.IsNullOrEmpty(_userConfig.SmtpUsername) ||
                string.IsNullOrEmpty(_userConfig.SmtpPassword))
            {
                _log.Error("邮件配置不正确");
                return;
            }
            try
            {
                _log.Information("正在发送 {Name} 通知", name);
                NotifyByEmail(GetTitle(), content,
                    _userConfig.Email, _userConfig.SmtpHost, _userConfig.SmtpPort,
                    _userConfig.SmtpUsername, _userConfig.SmtpPassword, _userConfig.SmtpSecure);
                _log.Information("已发送 {Name} 通知", name);
            }
            catch (Exception e)
            {
                _log.Error(e, "发送 {Name} 通知失败!", name);
            }
        }

        private static void NotifyByServerChan(string key, string title, string text = null)
        {
            var client = new RestClient($"https://sctapi.ftqq.com/{key}.send?title={title}");
            var request = new RestRequest(Method.POST);
            request.AddHeader("Content-Type", "application/x-www-form-urlencoded");
            if (!string.IsNullOrEmpty(text))
            {
                request.AddParameter("desp", "```text\n" + text + "\n```");
            }
            var response = client.Execute(request);
            CxSignClient.TestResponseCode(response);
            var json = JObject.Parse(response.Content);
            if(json["data"]!["errno"]!.Value<int>() != 0)
            {
                throw new Exception(json["data"]!["error"]!.ToString());
            }
        }

        private void NotifyByServerChan(string content)
        {
            const string name = "ServerChan";
            if (string.IsNullOrEmpty(_userConfig.ServerChanKey))
            {
                _log.Warning(
                    "由于 {Key} 为空，没有发送 {Name} 通知",
                    nameof(UserConfig.ServerChanKey),
                    name
                );
                return;
            }
            try
            {
                _log.Information("正在发送 {Name} 通知", name);
                NotifyByServerChan(_userConfig.ServerChanKey, GetTitle(), content);
                _log.Information("已发送 {Name} 通知", name);
            }
            catch (Exception e)
            {
                _log.Error(e, "发送 {Name} 通知失败!", name);
            }
        }

        private static void NotifyByPushPlus(string token, string title, string text)
        {
            var client = new RestClient("https://www.pushplus.plus/send");
            var request = new RestRequest(Method.POST);
            request.AddJsonBody(new JObject
            {
                ["token"] = token,
                ["title"] = title,
                ["content"] = text,
                ["template"] = "txt"
            }.ToString());
            var response = client.Execute(request);
            CxSignClient.TestResponseCode(response);
            var json = JObject.Parse(response.Content);
            if(json["code"]?.Value<int>() != 200)
            {
                throw new Exception(json["msg"]?.ToString());
            }
        }

        private void NotifyByPushPlus(string content)
        {
            const string name = "Push Plus";
            if (string.IsNullOrEmpty(_userConfig.PushPlusToken))
            {
                _log.Warning(
                    "由于 {Key} 为空，没有发送 {Name} 通知",
                    nameof(UserConfig.PushPlusToken),
                    name
                );
                return;
            }
            try
            {
                _log.Information("正在发送 {Name} 通知", name);
                NotifyByPushPlus(_userConfig.PushPlusToken, GetTitle(), content);
                _log.Information("已发送 {Name} 通知", name);
            }
            catch (Exception e)
            {
                _log.Error(e, "发送 {Name} 通知失败!", name);
            }
        }

        private static void NotifyByTelegramBot(string token, string chatId, string text)
        {
            if (Regex.IsMatch(token, "^https?://"))
            {
                if (!token.EndsWith("/"))
                {
                    token += "/";
                }
            }
            else
            {
                token = $"https://api.telegram.org/bot{token}/";
            }
            token += "sendMessage";
            var client = new RestClient(token);
            var request = new RestRequest(Method.POST);
            request.AddJsonBody(new JObject
            {
                ["chat_id"] = chatId,
                ["text"] = text
            }.ToString());
            var response = client.Execute(request);
            CxSignClient.TestResponseCode(response);
            var json = JObject.Parse(response.Content);
            if (json["ok"]?.Value<bool>() != true)
            {
                throw new Exception(response.Content);
            }
        }

        private void NotifyByTelegramBot(string content)
        {
            const string name = "Telegram Bot";
            if (string.IsNullOrEmpty(_userConfig.TelegramBotToken))
            {
                _log.Warning(
                    "由于 {Key} 为空，没有发送 {Name} 通知",
                    nameof(UserConfig.PushPlusToken),
                    name
                );
                return;
            }
            if (string.IsNullOrEmpty(_userConfig.TelegramBotChatId))
            {
                _log.Warning(
                    "由于 {Key} 为空，没有发送 {Name} 通知",
                    nameof(UserConfig.TelegramBotChatId),
                    name
                );
                return;
            }
            try
            {
                _log.Information("正在发送 {Name} 通知", name);
                NotifyByTelegramBot(_userConfig.TelegramBotToken, _userConfig.TelegramBotChatId,
                    GetContent(content));
                _log.Information("已发送 {Name} 通知", name);
            }
            catch (Exception e)
            {
                _log.Error(e, "发送 {Name} 通知失败!", name);
            }
        }

        private static void NotifyBark(string title, string content, string url)
        {
            var client = new RestClient(url);
            var request = new RestRequest(Method.POST);
            request.AddParameter("title", title);
            request.AddParameter("body", content);
            request.AddParameter("category", Title);
            request.AddParameter("level", "timeSensitive");
            var response = client.Execute(request);
            CxSignClient.TestResponseCode(response);
            var json = JObject.Parse(response.Content);
            if (json["code"]?.Value<int>() != 200)
            {
                throw new Exception(json["message"]?.Value<string>() ?? response.Content);
            }
        }
        
        private void NotifyByBark(string content)
        {
            const string name = "Bark";
            if (string.IsNullOrEmpty(_userConfig.BarkUrl))
            {
                _log.Warning(
                    "由于 {Key} 为空，没有发送 {Name} 通知",
                    nameof(UserConfig.BarkUrl),
                    name
                );
                return;
            }
            try
            {
                _log.Information("正在发送 {Name} 通知", name);
                NotifyBark(GetTitle(), content, _userConfig.BarkUrl);
                _log.Information("已发送 {Name} 通知", name);
            }
            catch (Exception e)
            {
                _log.Error(e, "发送 {Name} 通知失败!", name);
            }
        }

        private static string GetWechatWorkAppToken(string comId, string comSecret)
        {
            var client = new RestClient("https://qyapi.weixin.qq.com/cgi-bin/gettoken");
            var request = new RestRequest(Method.GET);
            request.AddQueryParameter("corpid", comId);
            request.AddQueryParameter("corpsecret", comSecret);
            var response = client.Execute(request);
            CxSignClient.TestResponseCode(response);
            var json = JObject.Parse(response.Content);
            return json["access_token"]!.Value<string>();
        }

        private static void NotifyWechatWorkApp(string content, string token, int agentId, string toUser = "@all")
        {
            var client = new RestClient("https://qyapi.weixin.qq.com/cgi-bin/message/send");
            var request = new RestRequest(Method.POST);
            request.AddQueryParameter("access_token", token);
            // request.AddQueryParameter("debug", "1");
            request.AddJsonBody(new JObject
            {
                ["msgtype"] = "text",
                ["enable_duplicate_check"] = 1,
                ["agentid"] = agentId,
                ["touser"] = toUser,
                ["text"] = new JObject
                {
                    ["content"] = content
                }
            }.ToString());
            var response = client.Execute(request);
            CxSignClient.TestResponseCode(response);
            var json = JObject.Parse(response.Content);
            if (json["errcode"]?.Value<int>() != 0)
            {
                throw new Exception(response.Content);
            }
        }

        private void NotifyByWechatWorkApp(string content)
        {
            const string name = "企业微信";
            if (string.IsNullOrEmpty(_userConfig.WechatWorkAppComId)) {
                _log.Warning(
                    "由于 {Key} 为空，没有发送 {Name} 通知",
                    nameof(UserConfig.WechatWorkAppComId),
                    name
                );
                return;
            }
            if (string.IsNullOrEmpty(_userConfig.WechatWorkAppComSecret)) {
                _log.Warning(
                    "由于 {Key} 为空，没有发送 {Name} 通知",
                    nameof(UserConfig.WechatWorkAppComSecret),
                    name
                );
                return;
            }
            if (_userConfig.WechatWorkAppAgentId == 0) {
                _log.Warning(
                    "由于 {Key} 为空，没有发送 {Name} 通知",
                    nameof(UserConfig.WechatWorkAppAgentId),
                    name
                );
                return;
            }
            try
            {
                _log.Information("正在发送 {Name} 通知", name);
                var token
                    = GetWechatWorkAppToken(_userConfig.WechatWorkAppComId, _userConfig.WechatWorkAppComSecret);
                NotifyWechatWorkApp(GetContent(content), token,
                    _userConfig.WechatWorkAppAgentId, _userConfig.WechatWorkAppToUser);
                _log.Information("已发送 {Name} 通知", name);
            }
            catch (Exception e)
            {
                _log.Error(e, "发送 {Name} 通知失败!", name);
            }
        }

        public static Logger CreateLogger(UserConfig userConfig, double startTime)
        {
            var console = new LoggerConfiguration()
                .Enrich.WithProperty("StartTime", startTime)
                .WriteTo.Console(outputTemplate:
                    "[{Timestamp:HH:mm:ss} {Level:u3}] [{StartTime}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();
            return new LoggerConfiguration()
                .WriteTo.Sink(new Notification(console, userConfig))
                .CreateLogger();
        }
    }
}