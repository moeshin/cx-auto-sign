﻿using CxSignHelper.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
using System;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CxSignHelper
{
    public class CxSignClient
    {
        private readonly CookieContainer _cookie;

        private string Fid { get; set; }

        private string PUid { get; set; }

        private CxSignClient(CookieContainer cookieContainer)
        {
            _cookie = cookieContainer;
            ParseCookies();
        }

        public static async Task<CxSignClient> LoginAsync(string username, string password, string fid = null)
        {
            RestClient client;
            IRestResponse response;
            if (string.IsNullOrEmpty(fid))
            {
                client = new RestClient("https://passport2-api.chaoxing.com")
                {
                    CookieContainer = new CookieContainer()
                };
                var request = new RestRequest("v11/loginregister");
                request.AddParameter("uname", username);
                request.AddParameter("code", password);
                response = await client.ExecuteGetAsync(request);
            }
            else
            {
                client = new RestClient($"https://passport2-api.chaoxing.com/v6/idNumberLogin?fid={fid}&idNumber={username}")
                {
                    CookieContainer = new CookieContainer()
                };
                var request = new RestRequest(Method.POST);
                request.AddHeader("Content-Type", "application/x-www-form-urlencoded");
                request.AddParameter("pwd", password);
                request.AddParameter("t", "0");
                response = await client.ExecutePostAsync(request);
            }
            TestResponseCode(response);
            var loginObject = JsonConvert.DeserializeObject<LoginObject>(response.Content);
            if (loginObject.Status != true)
            {
                throw new Exception(loginObject.Message);
            }
            return new CxSignClient(client.CookieContainer);
        }

        private async Task<string> GetPanTokenAsync()
        {
            var client = new RestClient("https://pan-yz.chaoxing.com")
            {
                CookieContainer = _cookie
            };
            var request = new RestRequest("api/token/uservalid");
            var response = await client.ExecuteGetAsync(request);
            TestResponseCode(response);
            var tokenObject = JsonConvert.DeserializeObject<TokenObject>(response.Content);
            if (tokenObject.Result != true)
            {
                throw new Exception("获取 token 失败");
            }
            return tokenObject.Token;
        }

        public async Task<JToken> GetActiveDetailAsync(string activeId)
        {
            var client = new RestClient("https://mobilelearn.chaoxing.com")
            {
                CookieContainer = _cookie
            };
            var request = new RestRequest("v2/apis/active/getPPTActiveInfo");
            request.AddParameter("activeId", activeId);
            var response = await client.ExecuteGetAsync(request);
            TestResponseCode(response);
            var json = JObject.Parse(response.Content);
            TestResult(json);
            return json["data"];
        }

        public async Task<string> SignAsync(string activeId, SignOptions signOptions)
        {
            var client = new RestClient("https://mobilelearn.chaoxing.com/pptSign/stuSignajax")
            {
                CookieContainer = _cookie
            };
            var request = new RestRequest(Method.GET);
            // ?activeId=292002019&appType=15&ifTiJiao=1&latitude=-1&longitude=-1&clientip=1.1.1.1&address=中国&objectId=3194679e88dbc9c60a4c6e31da7fa905
            request.AddParameter("activeId", activeId);
            request.AddParameter("appType", "15");
            request.AddParameter("ifTiJiao", "1");
            request.AddParameter("latitude", signOptions.Latitude);
            request.AddParameter("longitude", signOptions.Longitude);
            request.AddParameter("clientip", signOptions.ClientIp);
            request.AddParameter("address", signOptions.Address);
            request.AddParameter("objectId", signOptions.ImageId);
            var response = await client.ExecuteGetAsync(request);
            TestResponseCode(response);
            return response.Content;
        }

        public async Task<(string ImToken, string TUid)> GetImTokenAsync()
        {
            var client = new RestClient("https://im.chaoxing.com/webim/me")
            {
                CookieContainer = _cookie
            };
            var response = await client.ExecuteGetAsync(new RestRequest());
            TestResponseCode(response);
            var regex = new Regex(@"loginByToken\('(\d+?)', '([^']+?)'\);");
            var match = regex.Match(response.Content);
            if (!match.Success)
            {
                throw new Exception("获取 ImToken 失败");
            }
            return (match.Groups[2].Value, match.Groups[1].Value);
        }

        public async Task GetCoursesAsync(JToken course)
        {
            var client = new RestClient("https://mooc2-ans.chaoxing.com/visit/courses/list?rss=1&catalogId=0&searchname=")
            {
                CookieContainer = _cookie
            };
            var response = await client.ExecuteGetAsync(new RestRequest());
            TestResponseCode(response);
            var regex = new Regex(@"<a href=""https://mooc1\.chaoxing\.com/visit/stucoursemiddle\?courseid=(\d+?)&clazzid=(\d+)&cpi=\d+[""&]");
            var matches = regex.Matches(response.Content);
            foreach (Match match in matches)
            {
                if (match.Groups.Count <= 2)
                {
                    continue;
                }
                var courseId = match.Groups[1].Value;
                var classId = match.Groups[2].Value;
                var (courseName, className) = await GetClassDetailAsync(courseId, classId);
                var key = courseId + "-" + classId;
                var obj = course[key];
                if (obj is not { Type: JTokenType.Object })
                {
                    obj = new JObject();
                    course[key] = obj;
                }
                obj["CourseId"] = courseId;
                obj["ClassId"] = classId;
                obj["CourseName"] = courseName;
                obj["ClassName"] = className;
            }
        }

        private async Task<(string CourseName, string ClassName)> GetClassDetailAsync(string courseId, string classId)
        {
            var client = new RestClient($"https://mobilelearn.chaoxing.com/v2/apis/class/getClassDetail?fid={Fid}&courseId={courseId}&classId={classId}")
            {
                CookieContainer = _cookie
            };
            var response = await client.ExecuteGetAsync(new RestRequest());
            TestResponseCode(response);
            var json = JObject.Parse(response.Content);
            if (json["result"]!.Value<int>() != 1)
            {
                throw new Exception(json["msg"]?.Value<string>());
            }
            var data = json["data"];
            var courseName = data!["course"]!["data"]![0]!["name"]!.Value<string>();
            var className = data["name"]!.Value<string>();
            return (courseName, className);
        }

        public async Task<string> UploadImageAsync(string path)
        {
            var client = new RestClient("https://pan-yz.chaoxing.com/upload")
            {
                CookieContainer = _cookie
            };
            var request = new RestRequest(Method.POST);
            request.AddParameter("puid", PUid);
            request.AddParameter("_token", await GetPanTokenAsync());
            request.AddFile("file", path);
            var response = await client.ExecutePostAsync(request);
            TestResponseCode(response);
            var json = JObject.Parse(response.Content);
            if (json["result"]!.Value<bool>() != true)
            {
                throw new Exception(json["msg"]?.Value<string>());
            }
            return json["objectId"]!.Value<string>();
        }

        public async Task PreSignAsync(string activeId)
        {
            var client = new RestClient($"https://mobilelearn.chaoxing.com/newsign/preSign?activePrimaryId={activeId}")
            {
                CookieContainer = _cookie
            };
            var request = new RestRequest(Method.GET);
            var response = await client.ExecuteGetAsync(request);
            TestResponseCode(response);
        }

        private void ParseCookies()
        {
            var cookies = _cookie.GetCookies(new Uri("http://chaoxing.com"));
            Fid = cookies["fid"]?.Value;
            PUid = cookies["_uid"]!.Value;
        }

        public static void TestResponseCode(IRestResponse response)
        {
            var code = response.StatusCode;
            if (code != HttpStatusCode.OK)
            {
                throw new Exception($"非 200 状态响应：{code:D} {code:G}\n{response.Content}",
                    response.ErrorException);
            }
        }

        private static void TestResult(JObject json)
        {
            if (json["result"]!.Value<int>() != 1)
            {
                throw new Exception("Message: " + json["msg"]?.Value<string>() +
                                    "\nError Message: " + json["errorMsg"]?.Value<string>());
            }
        }
    }
}
