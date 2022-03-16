using McMaster.Extensions.CommandLineUtils;
using Newtonsoft.Json.Linq;
using RestSharp;
using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace cx_auto_sign
{
    [Command(
        Name = "cx-auto-sign",
        Description = "超星自动签到工具",
        ExtendedHelpText = @"
提示:
  本程序采用 MIT 协议开源(https://github.com/cyanray/cx-auto-sign).
  任何人可免费使用本程序并查看其源代码.
")]
    [VersionOptionFromMember("--version", MemberName = nameof(GetVersion))]
    [Subcommand(
        typeof(InitCommand),
        typeof(WorkCommand),
        typeof(UpdateCommand)
        )]
    internal class Program : CommandBase
    {
        private static async Task<int> Main(string[] args)
        {
            await CheckUpdate();
            return await CommandLineApplication.ExecuteAsync<Program>(args);
        }

        private static async Task CheckUpdate()
        {
            // System.Diagnostics.Debug.Assert(!CheckUpdate("2.1.3", "v0.0.0.1"));
            // System.Diagnostics.Debug.Assert(!CheckUpdate("2.1.3.2", "v2.1.3"));
            // System.Diagnostics.Debug.Assert(!CheckUpdate("2.1.3", "v2.1.3"));
            // System.Diagnostics.Debug.Assert(CheckUpdate("2.1.3", "v2.1.3.6"));
            // System.Diagnostics.Debug.Assert(CheckUpdate("2.1.3", "v2.1.5"));
            // System.Diagnostics.Debug.Assert(CheckUpdate("2.1.3", "v2.2.5"));
            // System.Diagnostics.Debug.Assert(!CheckUpdate("2.5.3", "2.2.5"));
            var ver = GetVersion();
            Console.WriteLine($"当前版本：{ver}");
            if (File.Exists(".noupdate"))
            {
                Console.WriteLine("已跳过检查更新");
                return;
            }
            try
            {
                Console.WriteLine("正在检查更新...");
                var (version, info) = await GetLatestVersion();
                if (CheckUpdate(ver, version))
                {
                    Console.WriteLine($"发现新版本: {version}");
                    Console.WriteLine(info);
                    Console.WriteLine("请前往 https://github.com/moeshin/cx-auto-sign/releases 下载更新，或者按任意键继续...");
                    Console.ReadKey();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine("获取版本信息失败，请访问 https://github.com/moeshin/cx-auto-sign/releases 检查是否有更新");
            }
        }

        private static bool CheckUpdate(string local, string remote)
        {
            if (remote == null || local == null)
            {
                return false;
            }
            var regex = new Regex(@"^v?(\d+(?:\.\d+)+)");
            var match = regex.Match(remote);
            if (!match.Success)
            {
                return false;
            }
            var r = match.Groups[1].Value.Split('.');
            var l =local.Split('.');
            var length = Math.Min(r.Length, l.Length);
            for (var i = 0; i < length; ++i)
            {
                var v = int.Parse(r[i]) - int.Parse(l[i]);
                if (v == 0)
                {
                    continue;
                }
                return v > 0;
            }
            return r.Length > l.Length;
        }

        private static string GetVersion()
            => typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        protected override async Task<int> OnExecuteAsync(CommandLineApplication app)
        {
            app.ShowHelp();
            return await base.OnExecuteAsync(app);
        }

        private static async Task<(string Version, string Info)> GetLatestVersion()
        {
            var client = new RestClient("https://api.github.com/repos/moeshin/cx-auto-sign/releases/latest");
            var response = await client.ExecuteGetAsync(new RestRequest());
            var json = JObject.Parse(response.Content);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                var message = string.Empty;
                if (json.ContainsKey("message"))
                {
                    message = json["message"]!.Value<string>();
                }
                throw new Exception($"获取最新版本失败: {message}");
            }
            var version = json["tag_name"]!.Value<string>();
            var info = json["body"]!.Value<string>();
            return (version, info);
        }

    }

}
