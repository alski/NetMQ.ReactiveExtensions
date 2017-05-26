﻿using System;
using System.Threading;
using ProtoBuf;

namespace NetMQ.ReactiveExtensions.SampleSubscriber
{
	class Program
	{
		static void Main(string[] args)
		{
			Console.Write("Reactive Extensions subscriber demo:\n");

			string endPoint = "tcp://127.0.0.1:56001";
			if (args.Length >= 1)
			{
				endPoint = args[0];
			}

			Console.Write("Endpoint: {0}\n", endPoint);

			SubscriberNetMq<MyMessage> subject = new SubscriberNetMq<MyMessage>(endPoint, new SerializeViaProtoBuf<MyMessage>(), loggerDelegate: msg => Console.Write(msg));
			subject.Subscribe(message =>
			{
				Console.Write("Received: {0}, '{1}'.\n", message.Num, message.Name);
			});

			Console.WriteLine("[waiting for publisher - any key to exit]");
			Console.ReadKey();
		}
	}



	[ProtoContract]
	public class MyMessage
	{
		public MyMessage()
		{
			
		}

		public MyMessage(int num, string name)
		{
			Num = num;
			Name = name;
		}

		[ProtoMember(1)]
		public int Num { get; set; }

		[ProtoMember(2)]
		public string Name { get; set; }
	}
}