using Newtonsoft.Json.Linq;

namespace cx_auto_sign
{
    public class UserConfig: BaseConfig
    {
        private readonly JToken _app;
        private readonly JToken _user;
        
        // Notification
        public readonly string ServerChanKey;
        public readonly string PushPlusToken;
        public readonly string BarkUrl;

        public readonly string TelegramBotToken;
        public readonly string TelegramBotChatId;

        // Email
        public readonly string Email;
        public readonly string SmtpHost;
        public readonly int    SmtpPort;
        public readonly string SmtpUsername;
        public readonly string SmtpPassword;
        public readonly bool   SmtpSecure;

        public static readonly JObject Default = new()
        {
            [nameof(ServerChanKey)] = "",
            [nameof(PushPlusToken)] = "",
            [nameof(BarkUrl)] = "",

            [nameof(TelegramBotToken)] = "",
            [nameof(TelegramBotChatId)] = "",

            [nameof(Email)] = "",
            [nameof(SmtpHost)] = "",
            [nameof(SmtpPort)] = 0,
            [nameof(SmtpUsername)] = "",
            [nameof(SmtpPassword)] = "",
            [nameof(SmtpSecure)] = false,
        };

        public UserConfig(BaseDataConfig app, BaseDataConfig user)
        {
            _app = app?.GetData();
            _user = user?.GetData();

            ServerChanKey = GetMustString(nameof(ServerChanKey));
            PushPlusToken = GetMustString(nameof(PushPlusToken));
            BarkUrl = GetMustString(nameof(BarkUrl));
            
            TelegramBotToken = GetMustString(nameof(TelegramBotToken));
            TelegramBotChatId = GetMustString(nameof(TelegramBotChatId));

            Email = GetMustString(nameof(Email));
            SmtpHost = GetString(nameof(SmtpHost));
            SmtpPort = GetInt(nameof(SmtpPort));
            SmtpUsername = GetString(nameof(SmtpUsername));
            SmtpPassword = GetString(nameof(SmtpPassword));
            SmtpSecure = GetBool(nameof(SmtpSecure));
        }

        protected override JToken Get(string key)
        {
            return Get(_user?[key]) ??
                   Get(_app?[key]) ??
                   Get(Default[key]);
        }
    }
}