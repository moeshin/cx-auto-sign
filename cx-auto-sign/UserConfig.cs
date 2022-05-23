﻿using Newtonsoft.Json.Linq;

namespace cx_auto_sign
{
    public class UserConfig: BaseConfig
    {
        private readonly JToken _app;
        private readonly JToken _user;
        
        // Notification
        public readonly string ServerChanKey;
        public readonly string PushPlusToken;

        // Email
        public readonly string Email;
        public readonly string SmtpHost;
        public readonly int    SmtpPort;
        public readonly string SmtpUsername;
        public readonly string SmtpPassword;
        public readonly bool   SmtpSecure;

        public readonly string TelegramBotToken;
        public readonly string TelegramBotChatId;

        public static readonly JObject Default = new()
        {
            [nameof(ServerChanKey)] = "",
            [nameof(PushPlusToken)] = "",

            [nameof(Email)] = "",
            [nameof(SmtpHost)] = "",
            [nameof(SmtpPort)] = 0,
            [nameof(SmtpUsername)] = "",
            [nameof(SmtpPassword)] = "",
            [nameof(SmtpSecure)] = false,
            
            [nameof(TelegramBotToken)] = "",
            [nameof(TelegramBotChatId)] = ""
        };

        public UserConfig(BaseDataConfig app, BaseDataConfig user)
        {
            _app = app?.GetData();
            _user = user?.GetData();

            ServerChanKey = GetMustString(nameof(ServerChanKey));
            PushPlusToken = GetMustString(nameof(PushPlusToken));

            Email = GetMustString(nameof(Email));
            SmtpHost = GetString(nameof(SmtpHost));
            SmtpPort = GetInt(nameof(SmtpPort));
            SmtpUsername = GetString(nameof(SmtpUsername));
            SmtpPassword = GetString(nameof(SmtpPassword));
            SmtpSecure = GetBool(nameof(SmtpSecure));

            TelegramBotToken = GetString(nameof(TelegramBotToken));
            TelegramBotChatId = GetString(nameof(TelegramBotChatId));
        }

        protected override JToken Get(string key)
        {
            return Get(_user?[key]) ??
                   Get(_app?[key]) ??
                   Get(Default[key]);
        }
    }
}