using System;
using cx_auto_sign;
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

        [Test]
        public void TestRulePhotoSign()
        {
            var time = new DateTime(2022, 4, 7, 8, 30, 0);
            Assert.IsTrue(Helper.RulePhotoSign("1-4|am", time));
            Assert.IsTrue(Helper.RulePhotoSign("4|08:00-11:40", time));
            Assert.IsTrue(Helper.RulePhotoSign("4|pm,08:00-11:40", time));
            Assert.IsTrue(Helper.RulePhotoSign("4|", time));
            Assert.IsTrue(Helper.RulePhotoSign("|am", time));
            Assert.IsFalse(Helper.RulePhotoSign("|pm", time));
            Assert.IsFalse(Helper.RulePhotoSign("1-3|", time));
            time = new DateTime(2022, 4, 7, 22, 33, 0);
            Assert.IsTrue(Helper.RulePhotoSign("4|pm", time));
        }
    }
}