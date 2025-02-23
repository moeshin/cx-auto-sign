﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CxSignHelper.Models;
using Newtonsoft.Json.Linq;

namespace cx_auto_sign
{
    public class CourseConfig: BaseConfig
    {
        private const string ImageDir = "Images";

        private static readonly string[] ImageSuffixes = { "png", "jpg", "jpeg", "bmp", "gif", "webp" };

        private readonly JToken _app;
        private readonly JToken _user;
        private readonly JToken _course;

        public bool SignEnable => GetBool(nameof(SignEnable));
        public int SignDelay => GetInt(nameof(SignDelay));
        public string SignAddress => GetString(nameof(SignAddress));
        public string SignLongitude => GetString(nameof(SignLongitude));
        public string SignLatitude => GetString(nameof(SignLatitude));
        public string SignClientIp => GetString(nameof(SignClientIp));

        public static readonly JObject Default = new()
        {
            [nameof(SignEnable)] = false,
            [GetSignTypeKey(SignType.Normal)] = true,
            [GetSignTypeKey(SignType.Gesture)] = true,
            [GetSignTypeKey(SignType.Photo)] = true,
            [GetSignTypeKey(SignType.Location)] = true,
            [nameof(SignDelay)] = 0,
            [nameof(SignAddress)] = "中国",
            [nameof(SignLatitude)] = "-1",
            [nameof(SignLongitude)] = "-1",
            [nameof(SignClientIp)] = "1.1.1.1"
        };

        public CourseConfig(BaseDataConfig app, BaseDataConfig user, BaseDataConfig course)
        {
            _app = app.GetData();
            _user = user.GetData();
            _course = course?.GetData();
        }

        protected override JToken Get(string key)
        {
            return Get(_course?[key]) ??
                   Get(_user?[key]) ??
                   Get(_app?[key]) ??
                   Get(Default[key]);
        }

        public static string GetSignTypeKey(SignType type)
        {
            return "Sign" + type;
        }

        public SignOptions GetSignOptions(SignType type)
        {
            var config = Get(GetSignTypeKey(type));
            if (config?.Type == JTokenType.Boolean && config.Value<bool>() == false)
            {
                return null;
            }
            return new SignOptions
            {
                Address = SignAddress,
                Latitude = SignLatitude,
                Longitude = SignLongitude,
                ClientIp = SignClientIp
            };
        }

        private static string GetImageFullPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            if (!Path.IsPathRooted(path))
            {
                path = Path.Combine(ImageDir, path);
            }
            return Path.GetFullPath(path);
        }

        public IEnumerable<string> GetImageSet(DateTime now)
        {
            var set = new HashSet<string>();
            var photo = Get(GetSignTypeKey(SignType.Photo));
            // ReSharper disable once InvertIf
            if (photo != null)
            {
                void AddSet(JToken v)
                {
                    var type = v.Type;
                    // ReSharper disable once ConvertIfStatementToSwitchStatement
                    if (type == JTokenType.String)
                    {
                        AddToImageSet(set, v);
                    }
                    else if (type == JTokenType.Array)
                    {
                        foreach (var token in v)
                        {
                            if (token.Type == JTokenType.String)
                            {
                                AddToImageSet(set, token);
                            }
                        }
                    }
                }

                if (photo.Type == JTokenType.Object)
                {
                    foreach (var (k, v) in (JObject) photo)
                    {
                        if (Helper.RulePhotoSign(now, k))
                        {
                            AddSet(v);
                        }
                    }
                }
                else
                {
                    AddSet(photo);
                }
            }
            return set;
        }

        private static void AddFileToImageSet(ISet<string> set, string path)
        {
            var name = Path.GetFileName(path);
            var index = name.LastIndexOf('.') + 1;
            if (index == 0)
            {
                return;
            }
            var suffix = name[index..].ToLower();
            if (!ImageSuffixes.Contains(suffix))
            {
                return;
            }
            set.Add(path);
        }

        private static void AddToImageSet(ISet<string> set, string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                set.Add("");
                return;
            }
            if (File.Exists(path))
            {
                AddFileToImageSet(set, path);
            }
            else if (Directory.Exists(path))
            {
                AddDirToImageSet(set, path);
            }
        }

        private static void AddToImageSet(ISet<string> set, JToken token)
        {
            AddToImageSet(set, GetImageFullPath(token.Value<string>()));
        }

        private static void AddDirToImageSet(ISet<string> set, string path)
        {
            var infos = new DirectoryInfo(path).GetFileSystemInfos();
            foreach (var info in infos)
            {
                var name = info.FullName;
                if ((info.Attributes & FileAttributes.Directory) != 0)
                {
                    AddDirToImageSet(set, name);
                }
                else
                {
                    AddFileToImageSet(set, name);
                }
            }
        }
    }
}