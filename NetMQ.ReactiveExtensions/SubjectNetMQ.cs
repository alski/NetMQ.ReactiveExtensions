﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reactive;
using System.Threading;
using NetMQ.Monitoring;
using NetMQ.Sockets;
// ReSharper disable SuggestVarOrType_SimpleTypes
// ReSharper disable InvertIf
#pragma warning disable 649

namespace NetMQ.ReactiveExtensions
{
	/// <summary>
	///	Intent: Pub/sub across different processes.
	/// </summary>
	/// <threadSafe>Yes</threadSafe>
	public class SubjectNetMQ<T> : IDisposable, ISubjectNetMQ<T>, IObservable<T>, IObserver<T>
	{
		#region Public

		public string SubscriberFilterName { get; private set; }

		public string AddressZeroMq { get; private set; }
		#endregion

		// ReSharper disable once NotAccessedField.Local
		private readonly WhenToCreateNetworkConnection _whenToCreateNetworkConnection;
		private CancellationTokenSource _cancellationTokenSource;
		private readonly Action<string> _loggerDelegate;

		private readonly List<IObserver<T>> _subscribers = new List<IObserver<T>>();
		private readonly object _subscribersLock = new object();

		/// <summary>
		/// Intent: See interface.
		/// </summary>
		/// <param name="addressZeroMq">Address to connect to, e.g. "tcp://127.0.0.1:56000"</param>
		/// <param name="subscriberFilterName">Subscriber Filter name. Defaults to the type name T. Allows many types to get sent over the same transport connection.</param>
		/// <param name="whenToCreateNetworkConnection">When to create the network connection.</param>
		/// <param name="cancellationTokenSource">Allows graceful termination of all internal threads associated with this subject.</param>
		/// <param name="loggerDelegate">(optional) If we want to look at messages generated within this class, specify a logger here.</param>
		public SubjectNetMQ(string addressZeroMq, string subscriberFilterName = null, WhenToCreateNetworkConnection whenToCreateNetworkConnection = WhenToCreateNetworkConnection.LazyConnectOnFirstUse, CancellationTokenSource cancellationTokenSource = default(CancellationTokenSource), Action<string> loggerDelegate = null)
		{
			_whenToCreateNetworkConnection = whenToCreateNetworkConnection;
			_cancellationTokenSource = cancellationTokenSource;
			_loggerDelegate = loggerDelegate;

			if (subscriberFilterName == null)
			{
				// Unfortunately, the subscriber never scans more than the first 32 characters of the filter, so we must
                // trim to less than this length, but also ensure that it's unique. This ensures that if we get two
                // classnames of 50 characters that only differ by the last character, everything will still work and we
                // won't get crossed subscriptions.
				SubscriberFilterName = typeof(T).Name;

				if (SubscriberFilterName.Length > 32)
				{
					string uniqueHashCode = string.Format("{0:X8}", SubscriberFilterName.GetHashCode());

					SubscriberFilterName = SubscriberFilterName.Substring(0, Math.Min(SubscriberFilterName.Length, 24));
					SubscriberFilterName += uniqueHashCode;
				}
				if (SubscriberFilterName.Length > 32)
				{
					throw new Exception("Error E38742. Internal error; subscription length can never be longer than 32 characters.");
				}
			}

			if (string.IsNullOrEmpty(Thread.CurrentThread.Name) == true)
			{
				// Cannot set the thread name twice.
				Thread.CurrentThread.Name = subscriberFilterName;
			}

			AddressZeroMq = addressZeroMq;

			if (string.IsNullOrEmpty(AddressZeroMq))
			{
				throw new Exception("Error E76244. Must define the endpoint address for ZeroMQ to connect (or bind) to.");
			}

			switch (whenToCreateNetworkConnection)
			{
				case WhenToCreateNetworkConnection.LazyConnectOnFirstUse:
					// (default) Do nothing; will be instantiated on first use.
					break;
				case WhenToCreateNetworkConnection.SetupPublisherTransportNow:
					InitializePublisherOnFirstUse(addressZeroMq);
					break;
				case WhenToCreateNetworkConnection.SetupSubscriberTransportNow:
					InitializeSubscriberOnFirstUse(addressZeroMq);
					break;
				default:
					throw new Exception("Error E38745. Internal error; at least one publisher or subscriber must be instantiated.");
			}
		}

		#region Initialize publisher on demand.
		private PublisherSocket _publisherSocket;
		private volatile bool _initializePublisherDone = false;
		private readonly object _initializePublisherLock = new object();
		private void InitializePublisherOnFirstUse(string addressZeroMq)
		{
			if (_initializePublisherDone == false) // Double checked locking.
			{
				lock (_initializePublisherLock)
				{
					if (_initializePublisherDone == false)
					{
						_loggerDelegate?.Invoke(string.Format("Publisher socket binding to: {0}\n", AddressZeroMq));
						_publisherSocket = NetMqTransportShared.Instance(_loggerDelegate).GetSharedPublisherSocket(addressZeroMq);
						_initializePublisherDone = true;
					}
				}
			}
		}
		#endregion

		#region Initialize subscriber on demand.
		private SubscriberSocket _subscriberSocket;
		private volatile bool _initializeSubscriberDone = false;
		private Thread _thread;

		private void InitializeSubscriberOnFirstUse(string addressZeroMq)
		{
			if (_initializeSubscriberDone == false) // Double checked locking.
			{
				lock (_subscribersLock)
				{
					if (_initializeSubscriberDone == false)
					{
						if (_cancellationTokenSource == null)
						{
							_cancellationTokenSource = new CancellationTokenSource();
						}

						_subscriberSocket = NetMqTransportShared.Instance(_loggerDelegate).GetSharedSubscriberSocket(addressZeroMq);

						ManualResetEvent threadReadySignal = new ManualResetEvent(false);

						_thread = new Thread(() =>
						{
							try
							{
								_loggerDelegate?.Invoke(string.Format("Thread initialized.\n"));
								threadReadySignal.Set();
								while (_cancellationTokenSource.IsCancellationRequested == false)
								{
									//_loggerDelegate?.Invoke(string.Format("Received message for {0}.\n", typeof(T)));

									string messageTopicReceived = _subscriberSocket.ReceiveFrameString();
									if (messageTopicReceived != SubscriberFilterName)
									{
										// This message is for another subscriber. This should never occur.
#if DEBUG
										throw new Exception("Error E38444. Internal exception, this should never occur, as the ZeroMQ lib automaticlaly filters by subject name.");
#else
										return;
#endif
									}
									var type = _subscriberSocket.ReceiveFrameString();
									switch (type)
									{
										// Originated from "OnNext".
										case "N":
											T messageReceived = _subscriberSocket.ReceiveFrameBytes().DeserializeProtoBuf<T>();
											lock (_subscribersLock)
											{
												_subscribers.ForEach(o => o.OnNext(messageReceived));
											}
											break;
										// Originated from "OnCompleted".
										case "C":
											lock (_subscribersLock)
											{
												_subscribers.ForEach(o => o.OnCompleted());

												// We are done! We don't want to send any more messages to subscribers, and we
												// want to close the listening socket.
												_cancellationTokenSource.Cancel();
											}
											break;
										// Originated from "OnException".
										case "E":
											Exception exception;
											string exceptionAsString = "Uninitialized.";
											try
											{
												// Not used, but useful for cross-platform debugging: we can read the error straight off the wire.
												exceptionAsString = _subscriberSocket.ReceiveFrameBytes().DeserializeProtoBuf<string>();
												SerializableException exceptionWrapper = _subscriberSocket.ReceiveFrameBytes().DeSerializeException();
												exception = exceptionWrapper.InnerException;
											}
											catch (Exception ex)
											{
												// If we had trouble deserializing the exception (probably due to a
												// different version of .NET), then do the next best thing: (1) The
												// inner exception is the error we got when deserializing, and (2) the
												// main exception is the human-readable "exception.ToString()" that we
												// originally captured.
												exception = new Exception(exceptionAsString, ex);
											}

											lock (_subscribersLock)
											{
												_subscribers.ForEach(o => o.OnError(exception));
											}
											break;
										// Originated from a "Ping" request.
										case "P":
											// Do nothing, this is a ping command used to wait until sockets are initialized properly.
											_loggerDelegate?.Invoke(string.Format("Received ping.\n"));
											break;
										default:
											throw new Exception(string.Format("Error E28734. Something is wrong - received '{0}' when we expected \"N\", \"C\" or \"E\" - are we out of sync?", type));
									}
								}
							}
							catch (Exception ex)
							{
								_loggerDelegate?.Invoke(string.Format("Error E23844. Exception in threadName \"{0}\". Thread exiting. Exception: \"{1}\".\n", SubscriberFilterName, ex.Message));
								lock (_subscribersLock)
								{
									this._subscribers.ForEach((ob) => ob.OnError(ex));
								}
							}
							finally
							{
								lock (_subscribersLock)
								{
									_subscribers.Clear();
								}
								_cancellationTokenSource.Dispose();

								// Disconnect from the socket.
								_subscriberSocket.Dispose();
							}
						})
						{
							Name = this.SubscriberFilterName,
							IsBackground = true // Have to set it to background, or else it will not exit when the program exits.
						};
						_thread.Start();

						// Wait for subscriber thread to properly spin up.
						threadReadySignal.WaitOne(TimeSpan.FromMilliseconds(3000));

						// Intent: Now connect to the socket.
						{
							_loggerDelegate?.Invoke(string.Format("Subscriber socket connecting to: {0}\n", addressZeroMq));

							// this.SubscriberFilterName is set to the type T of the incoming class by default, so we can
							// have many types on the same transport.
							_subscriberSocket.Subscribe(this.SubscriberFilterName);
						}

						_loggerDelegate?.Invoke(string.Format("Subscriber: finished setup.\n"));

						_initializeSubscriberDone = true;
					}
				} // lock
				Thread.Sleep(500); // Otherwise, the first item we subscribe  to may get missed by the subscriber.
			}
		}
		#endregion

		#region IObservable<T> (i.e. the subscriber)
		public IDisposable Subscribe(IObserver<T> observer)
		{
			lock (_subscribersLock)
			{
				this._subscribers.Add(observer);
			}

			InitializeSubscriberOnFirstUse(this.AddressZeroMq);

			// Could return ".this", but this would introduce an issue: if one subscriber unsubscribed, it would
			// unsubscribe all subscribers.
			return new AnonymousDisposable(() =>
			{
				lock (_subscribersLock)
				{
					this._subscribers.Remove(observer);
				}
			});
		}
		#endregion

		#region Implement IObserver<T> (i.e. the publisher).

		public void OnNext(T message)
		{
			try
			{
				InitializePublisherOnFirstUse(this.AddressZeroMq);

				byte[] serialized = message.SerializeProtoBuf<T>();

				// Publish message using ZeroMQ as the transport mechanism.
				_publisherSocket.SendMoreFrame(SubscriberFilterName)
					.SendMoreFrame("N") // "N", "E" or "C" for "OnNext", "OnError" or "OnCompleted".
					.SendFrame(serialized);

				// Comment in the remaining code for the standard pub/sub pattern.

				//if (this.HasObservers == false)
				//{
				//throw new QxNoSubscribers("Error E23444. As there are no subscribers to this publisher, this event will be lost.");
				//}

				//lock (_subscribersLock)
				//{
				//this._subscribers.ForEach(msg => msg.OnNext(message));
				//}
			}
			catch (InvalidOperationException ex)
			{
				if (ex.Source.ToLower().Contains("protobuf-net"))
				{
					var exWithDocs = new InvalidOperationException("Error: Message must be serializable by Protobuf-Net. To fix, annotate message with [ProtoContract] and [ProtoMember(N)]. See help on web.");
					this.OnError(exWithDocs);
					throw exWithDocs;
				}
			}
			catch (Exception ex)
			{
				_loggerDelegate?.Invoke(string.Format("Exception: {0}.", ex.Message));
				this.OnError(ex);
				throw;
			}
		}

		public void OnError(Exception exception)
		{
			InitializePublisherOnFirstUse(this.AddressZeroMq);

			var exceptionWrapper = new SerializableException(exception);	
			byte[] serializedException = exceptionWrapper.SerializeException();
			string exceptionAsString = exception.ToString();

			_publisherSocket.SendMoreFrame(SubscriberFilterName)
					.SendMoreFrame("E") // "N", "E" or "C" for "OnNext", "OnError" or "OnCompleted".
					.SendMoreFrame(exceptionAsString.SerializeProtoBuf()) // Human readable exception. Added for 100%
                                                                          // cross-platform debugging, so we can read
                                                                          // the error on the wire.
					.SendFrame(serializedException); // Machine readable exception. So we can pass the full exception to
                                                     // the .NET client.

			// Comment in the remaining code for the standard pub/sub pattern.

			//if (this.HasObservers == false)
			//{
			//throw new QxNoSubscribers("Error E28244. As there are no subscribers to this publisher, this published exception will be lost.");
			//}

			//lock (_subscribersLock)
			//{
			//this._subscribers.ForEach(msg => msg.OnError(exception));
			//}
		}

		public void OnCompleted()
		{
			InitializePublisherOnFirstUse(this.AddressZeroMq);

			_publisherSocket.SendMoreFrame(SubscriberFilterName)
				.SendFrame("C"); // "N", "E" or "C" for "OnNext", "OnError" or "OnCompleted".
		}
		#endregion

		#region Implement IDisposable.
		public void Dispose()
		{
			lock (_subscribersLock)
			{
				_subscribers.Clear();
			}
			_cancellationTokenSource.Cancel();

			// Wait until the thread has exited.
			bool threadExitedProperly = _thread.Join(TimeSpan.FromSeconds(30));
			if (threadExitedProperly == false)
			{
				throw new Exception("Error E62724. Thread did not exit when requested.");
			}
		}
		#endregion

		/// <summary>
		/// Intent: True if there are any subscribers registered.
		/// </summary>
		public bool HasObservers
		{
			get
			{
				lock (_subscribersLock)
				{
					return this._subscribers.Count > 0;
				}
			}
		}
	}	
}

