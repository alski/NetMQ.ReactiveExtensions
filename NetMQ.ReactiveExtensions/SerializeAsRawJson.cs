using System;
using System.Collections.Generic;
using System.Text;

namespace NetMQ.ReactiveExtensions
{
    public class SerializeAsRawJson : INetMQSerializer<string>
    {
        UTF8Encoding utf8 = new UTF8Encoding();

        public string Deserialize(byte[] bytes)
        {
            return utf8.GetString(bytes);
        }

        public byte[] Serialize(string message)
        {
            return utf8.GetBytes(message);
        }
    }
}
