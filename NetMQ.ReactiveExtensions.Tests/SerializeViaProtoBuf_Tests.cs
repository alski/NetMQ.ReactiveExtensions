using System.Diagnostics;
using NUnit.Framework;
// ReSharper disable InconsistentNaming

namespace NetMQ.ReactiveExtensions.Tests
{
    [TestFixture]
    public static class SerializeViaProtoBuf_Tests
    {
        [Test]
        public static void Should_be_able_to_serialize_a_decimal()
        {
            NUnitUtils.PrintTestName();

            Stopwatch sw = Stopwatch.StartNew();
            var serializer = new SerializeViaProtoBuf<decimal>();

            const decimal x = 123.4m;
            var rawBytes = serializer.Serialize(x);
            var original = serializer.Deserialize(rawBytes);
            Assert.AreEqual(x, original);

            NUnitUtils.PrintElapsedTime(sw.Elapsed);
        }

        [Test]
        public static void Should_be_able_to_serialize_a_string()
        {
            NUnitUtils.PrintTestName();

            Stopwatch sw = Stopwatch.StartNew();
            var serializer = new SerializeViaProtoBuf<string>();

            const string x = "Hello";
            var rawBytes = serializer.Serialize(x);
            var original = serializer.Deserialize(rawBytes);
            Assert.AreEqual(x, original);

            NUnitUtils.PrintElapsedTime(sw.Elapsed);
        }

        [Test]
        public static void Should_be_able_to_serialize_an_int()
        {
            NUnitUtils.PrintTestName();

            Stopwatch sw = Stopwatch.StartNew();
            var serializer = new SerializeViaProtoBuf<int>();

            const int x = 42;
            var rawBytes = serializer.Serialize(x);
            var original = serializer.Deserialize(rawBytes);
            Assert.AreEqual(x, original);

            NUnitUtils.PrintElapsedTime(sw.Elapsed);
        }
    }
}