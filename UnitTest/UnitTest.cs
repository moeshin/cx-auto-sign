using System;
using System.IO;
using cx_auto_sign;
using CxSignHelper.Models;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Serilog;

namespace UnitTest
{
    public class UnitTest
    {
        [SetUp]
        public void Setup()
        {
            Log.Logger = new LoggerConfiguration()
                .WriteTo.Console()
                .CreateLogger();
        }

        [Test]
        public void TestCheckUpdate()
        {
            Assert.IsFalse(Helper.CheckUpdate("2.1.3", "v0.0.0.1"));
            Assert.IsFalse(Helper.CheckUpdate("2.1.3.2", "v2.1.3"));
            Assert.IsFalse(Helper.CheckUpdate("2.1.3", "v2.1.3"));
            Assert.IsFalse(Helper.CheckUpdate("2.5.3", "2.2.5"));
            Assert.IsTrue(Helper.CheckUpdate("2.1.3", "v2.1.3.6"));
            Assert.IsTrue(Helper.CheckUpdate("2.1.3", "v2.1.5"));
            Assert.IsTrue(Helper.CheckUpdate("2.1.3", "v2.2.5"));
        }

        [Test]
        public void TestSignCache()
        {
            var cache = new SignCache
            {
                Expire = 5
            };
            Assert.IsTrue(cache.Add("1"));
            Assert.IsTrue(cache.Add("2"));
            Assert.IsTrue(cache.Add("3"));
            Assert.IsFalse(cache.Add("1"));
            cache.Expire = 0;
            Assert.IsTrue(cache.Add("1"));
            Assert.IsTrue(cache.Add("2"));
            Assert.IsTrue(cache.Add("3"));
        }

        private delegate bool DelegateSignPhotoRule(DateTime time, string rule);

        private static void TestDelegateSignPhotoRule(DelegateSignPhotoRule fun)
        {
            var time = new DateTime(2022, 4, 7, 8, 30, 0);
            Assert.IsTrue(fun(time, "1-4|am"));
            Assert.IsTrue(fun(time, "4|08:00-11:40"));
            Assert.IsTrue(fun(time, "4|pm,08:00-11:40"));
            Assert.IsTrue(fun(time, "4|"));
            Assert.IsTrue(fun(time, "|am"));
            Assert.IsFalse(fun(time, "|pm"));
            Assert.IsFalse(fun(time, "1-3|"));
            time = new DateTime(2022, 4, 7, 22, 33, 0);
            Assert.IsTrue(fun(time, "4|pm"));
        }

        [Test]
        public void TestSignPhotoRule()
        {
            TestDelegateSignPhotoRule(Helper.RulePhotoSign);
        }

        [Test]
        public void TestSignPhotoGetImagePath()
        {
            const string name = "test.png";
            const string alias = "../" + name;
            if (!File.Exists(name))
            {
                File.Create(name);
            }

            var cd = Environment.CurrentDirectory;
            var key = CourseConfig.GetSignTypeKey(SignType.Photo);
            var work = new SignWork(null);
            var data = new JObject();

            bool T1(DateTime time, params string[] rules)
            {
                var v = data[key] = new JObject();
                for (var i = 0; i < rules.Length; i++)
                {
                    v[rules[i]] = i == 0 ? alias : null;
                }
                var path = work.GetImagePath(time);
                if (string.IsNullOrEmpty(path))
                {
                    return false;
                }
                return Path.GetRelativePath(cd, path) == name;
            }

            bool T2(DateTime time, string rule)
            {
                return T1(time, rule);
            }

            var empty = new CourseDataConfig(new JObject());
            work.SetCourseConfig(new CourseConfig(empty, empty, new CourseDataConfig(data)));
            work.Log = Log.Logger;

            TestDelegateSignPhotoRule(T2);
            
            var time = new DateTime(2022, 4, 7, 8, 30, 0);
            Assert.IsTrue(T1(time, "|am", "1-3|"));
            Assert.IsFalse(T1(time, "1-3|", "|am"));
        }

        [Test]
        public void TestSignTypeEnum()
        {
            for (var i = -1; i < (int)SignType.Length; ++i)
            {
                SignWork.GetSignTypeName((SignType)i);
            }
        }
    }
}