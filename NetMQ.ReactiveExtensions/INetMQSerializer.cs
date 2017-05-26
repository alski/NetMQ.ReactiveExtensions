namespace NetMQ.ReactiveExtensions
{
    /// <summary>
    /// Defines how to plugin in the serialization and deserialization for NetMq.Rx
    /// </summary>
    public interface INetMQSerializer<T>
    {
        /// <summary>
        /// Turn the <typeparamref name="T"/> into bytes.
        /// </summary>
        /// <param name="message">object to turn into bytes</param>
        /// <returns></returns>
        byte[] Serialize(T message);

        /// <summary>
        /// Turn the bytes back into a <typeparamref name="T"/>
        /// </summary>
        /// <param name="bytes">Bytes to turn into a typed object</param>
        /// <returns></returns>
        T Deserialize(byte[] bytes);
    }
}
