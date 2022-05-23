﻿using System;
using System.Text;
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
                        _stringBuilder.Insert(0, "自动签到 " + status + '\n');
                        _title = Title + ' ' + status;
                    }
                    var content = _stringBuilder.ToString();
                    NotifyByEmail(content);
                    NotifyByServerChan(content);
                    NotifyByPushPlus(content);
                    NotifyByTelegramBot(content);
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
            if (string.IsNullOrEmpty(_userConfig.Email))
            {
                _log.Warning("由于 {Name} 为空，没有发送邮件通知", nameof(UserConfig.Email));
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
                _log.Information("正在发送邮件通知");
                NotifyByEmail(GetTitle(), content,
                    _userConfig.Email, _userConfig.SmtpHost, _userConfig.SmtpPort,
                    _userConfig.SmtpUsername, _userConfig.SmtpPassword, _userConfig.SmtpSecure);
                _log.Information("已发送邮件通知");
            }
            catch (Exception e)
            {
                _log.Error(e, "发送邮件通知失败!");
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
            if (string.IsNullOrEmpty(_userConfig.ServerChanKey))
            {
                _log.Warning("由于 {Name} 为空，没有发送 ServerChan 通知", 
                    nameof(UserConfig.ServerChanKey));
                return;
            }
            try
            {
                _log.Information("正在发送 ServerChan 通知");
                NotifyByServerChan(_userConfig.ServerChanKey, GetTitle(), content);
                _log.Information("已发送 ServerChan 通知");
            }
            catch (Exception e)
            {
                _log.Error(e, "发送 ServerChan 通知失败!");
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
            if (string.IsNullOrEmpty(_userConfig.PushPlusToken))
            {
                _log.Warning("由于 {Name} 为空，没有发送 PushPlus 通知",
                    nameof(UserConfig.PushPlusToken));
                return;
            }
            try
            {
                _log.Information("正在发送 PushPlus 通知");
                NotifyByPushPlus(_userConfig.PushPlusToken, GetTitle(), content);
                _log.Information("已发送 PushPlus 通知");
            }
            catch (Exception e)
            {
                _log.Error(e, "发送 PushPlus 通知失败!");
            }
        }

        private static void NotifyByTelegramBot(string token, string chatId, string text)
        {
            var client = new RestClient($"https://api.telegram.org/bot{token}/sendMessage");
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
            if (string.IsNullOrEmpty(_userConfig.TelegramBotToken))
            {
                _log.Warning("由于 {Name} 为空，没有发送 Telegram Bot 通知",
                    nameof(UserConfig.PushPlusToken));
                return;
            }
            if (string.IsNullOrEmpty(_userConfig.TelegramBotChatId))
            {
                _log.Warning("由于 {Name} 为空，没有发送 Telegram Bot 通知",
                    nameof(UserConfig.TelegramBotChatId));
                return;
            }
            try
            {
                _log.Information("正在发送 Telegram Bot 通知");
                NotifyByTelegramBot(_userConfig.TelegramBotToken, _userConfig.TelegramBotChatId,
                    GetTitle() + "\n" + content);
                _log.Information("已发送 Telegram Bot 通知");
            }
            catch (Exception e)
            {
                _log.Error(e, "发送 Telegram Bot 通知失败!");
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