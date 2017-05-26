using System;
using NetMQ.ReactiveExtensions;

namespace NetMQ.Rx.ZlibJson
{
    public class SerializeAsString : INetMQSerializer
    {
        public T Deserialize<T>(byte[] bytes)
        {
           throw new NotImplementedException();
        }

        public byte[] Serialize<T>(T message)
        {
            throw new NotImplementedException();
        }
    }
}
