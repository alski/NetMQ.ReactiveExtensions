using System;
using System.IO;
using ProtoBuf;

namespace NetMQ.ReactiveExtensions
{

    /// <summary>
    /// Intent: Allow us to serialize using ProtoBuf.
    /// </summary>
    public  class SerializeViaProtoBuf<T> : INetMQSerializer<T>
    {
		public  byte[] Serialize( T message)
		{
			byte[] result;
			using (var stream = new MemoryStream())
			{
				Serializer.Serialize(stream, message);
				result = stream.ToArray();
			}
			return result;
		}

		public  T Deserialize( byte[] bytes)
		{
			T result;
			using (var stream = new MemoryStream(bytes))
			{
				result = Serializer.Deserialize<T>(stream);
			}
			return result;
		}
	}
}
