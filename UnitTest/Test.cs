using cx_auto_sign;
using NUnit.Framework;

namespace UnitTest
{
    public class Tests
    {
        [SetUp]
        public void Setup()
        {
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
    }
}