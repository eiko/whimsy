﻿/*-----------------------------+-------------------------------\
|                                                              |
|                         !!!NOTICE!!!                         |
|                                                              |
|  These libraries are under heavy development so they are     |
|  subject to make many changes as development continues.      |
|  For this reason, the libraries may not be well commented.   |
|  THANK YOU for supporting forge with all your feedback       |
|  suggestions, bug reports and comments!                      |
|                                                              |
|                              - The Forge Team                |
|                                Bearded Man Studios, Inc.     |
|                                                              |
|  This source code, project files, and associated files are   |
|  copyrighted by Bearded Man Studios, Inc. (2012-2016) and    |
|  may not be redistributed without written permission.        |
|                                                              |
\------------------------------+------------------------------*/

using BeardedManStudios.Forge.Networking.Frame;
using BeardedManStudios.Threading;
using System;
using System.Collections.Generic;

namespace BeardedManStudios.Forge.Networking
{
	/// <summary>
	/// The representation of an object on the network, this object can have
	/// properties that it serializes and RPC (remote procedure calls)
	/// </summary>
	public abstract class NetworkObject
	{
		protected const byte DIRTY_FIELD_SUB_ROUTER_ID = 1;
		protected const byte DESTROY_SUB_ROUTER_ID = 2;

		private const ulong DEFAULT_UPDATE_INTERVAL = 100;
		private const byte RPC_BEHAVIOR_OVERWRITE = 0x1;

		/// <summary>
		/// A generic delegate for events to fire off while passing a NetworkObject source
		/// </summary>
		/// <param name="networkObject">The object source for this event</param>
		public delegate void NetworkObjectEvent(NetworkObject networkObject);

		/// <summary>
		/// A generic delegate for events to fire off while passing a INetworkBehavior and NetworkObject source
		/// </summary>
		/// <param name="behavior"></param>
		/// <param name="networkObject">The object source for this event</param>
		public delegate void NetworkBehaviorEvent(INetworkBehavior behavior, NetworkObject networkObject);

		/// <summary>
		/// Used to create events that require BMSByte data
		/// </summary>
		/// <param name="data">The data that was read</param>
		public delegate void BinaryDataEvent(BMSByte data);

		/// <summary>
		/// Used for when an object is created on the network
		/// </summary>
		/// <param name="identity">The identity used to know what type of network object this is</param>
		/// <param name="hash">The hash id (if sent) to match up client created objects with the ids that the server will respond with asynchronously</param>
		/// <param name="id">The id for this network object</param>
		/// <param name="frame">The frame data for this object's creation (default values)</param>
		public delegate void CreateEvent(int identity, int hash, uint id, FrameStream frame);

		/// <summary>
		/// Used for when any field event occurs, will pass the target field as a param
		/// </summary>
		/// <typeparam name="T">The acceptable network serializable data type</typeparam>
		/// <param name="field">The target field related to this event</param>
		/// <param name="timestep">The timestep for when this event happens</param>
		public delegate void FieldEvent<T>(T field, ulong timestep);

        /// <summary>
        /// TODO: COMMENT THIS
        /// </summary>
        /// <param name="networker"></param>
        /// <param name="identity"></param>
        /// <param name="id"></param>
        /// <param name="frame"></param>
        /// <param name="callback"></param>
        public delegate void CreateRequestEvent(NetWorker networker, int identity, uint id, FrameStream frame, Action<NetworkObject> callback);

		/// <summary>
		/// Occurs when a client get's an id from the server asynchronously that belongs to this NetworkObject
		/// </summary>
		public static event CreateEvent objectCreateAttach;

		/// <summary>
		/// Occurs when a network object has been created on the network
		/// </summary>
		public static event NetworkObjectEvent objectCreated;

        /// <summary>
        /// TODO: COMMENT
        /// </summary>
        public static event CreateRequestEvent objectCreateRequested;

		/// <summary>
		/// Occurs when a binary message was received on the network for this object and is needed to be read
		/// </summary>
		public event BinaryDataEvent readBinary;

		/// <summary>
		/// The factory that is to be used for creating network objects, only 1 factory
		/// is needed for any network application (shared code base)
		/// </summary>
		public static INetworkObjectFactory Factory { get; set; }

		/// <summary>
		/// Occurs when this object is setup and ready on the network
		/// </summary>
		public event NetWorker.BaseNetworkEvent onReady;
		public event NetWorker.BaseNetworkEvent onDestroy;

		// TODO:  Move these to a single  class so we know what values are available
		/// <summary>
		/// The router id that is used for when sending or getting binary messages
		/// </summary>
		public const byte ROUTER_ID = 1;
		public const byte BINARY_DATA_ROUTER_ID = 3;
		public const byte CREATED_OBJECT_ROUTER_ID = 4;

		/// <summary>
		/// The unique id for this object on the current networker
		/// </summary>
		public uint NetworkId { get; private set; }

		/// <summary>
		/// A refrerence to the networker that this network object is attached to
		/// </summary>
		public NetWorker Networker { get; private set; }

		/// <summary>
		/// A helper to get the current players's id (Networker.Me.NetworkId)
		/// </summary>
		public uint MyPlayerId { get { return Networker.Me.NetworkId; } }

		/// <summary>
		/// Returns <c>true</c> if the current NetWorker is the owner of this NetworkObject
		/// </summary>
		public bool IsOwner { get; private set; }

		/// <summary>
		/// If set to true on the server, the server can change the value of the properties
		/// of a network object. BEWARE, this can cause race conditions in data transfer, so
		/// only use as a last resort
		/// </summary>
		public bool AuthorityUpdateMode { get; set; }

		/// <summary>
		/// A lookup table for all of the RPC's that are available to this network object
		/// </summary>
		public Dictionary<byte, Rpc> Rpcs { get; private set; }

		/// <summary>
		/// This is a mapping from the method name to the id that it is within the Rpcs dictionary
		/// </summary>
		protected Dictionary<string, byte> rpcLookup = new Dictionary<string, byte>();

		/// <summary>
		/// Used to convert from an RPC id to the string name associated with it
		/// </summary>
		protected Dictionary<byte, string> inverseRpcLookup = new Dictionary<byte, string>();

		/// <summary>
		/// Is <c>true</c> if this object has been fully setup on the network
		/// </summary>
		public bool NetworkReady { get; private set; }

		// TODO:  Make sure that these do not collide
		/// <summary>
		/// The temporary hash that was sent from a client to this server, or the hash that a client is using
		/// to identify the attach method to this particular object when a server sends the id
		/// </summary>
		private int hash = 0;

		public int CreateCode = 0;

		public static int GlobalHash { get; private set; }

		/// <summary>
		/// The object that has already been created and is pending an initialize
		/// </summary>
		private INetworkBehavior pendingBehavior = null;

		/// <summary>
		/// This is a reference to the attached behavior that is controlling this object
		/// </summary>
		public INetworkBehavior AttachedBehavior { get; set; }

		/// <summary>
		/// Occurs when the pending behavior supplied has been initialized 
		/// </summary>
		public event NetworkBehaviorEvent pendingInitialized;

		/// <summary>
		/// Used to determine the last time this object has been updated
		/// </summary>
		private ulong lastUpdateTimestep = 0;

		/// <summary>
		/// Used to identify what type (subtype) of object this is
		/// </summary>
		public abstract int UniqueIdentity { get; }

		/// <summary>
		/// The timestep that this object was created in
		/// </summary>
		protected ulong CreateTimestep { get; private set; }

		/// <summary>
		/// The time in milliseconds between each update for this object
		/// </summary>
		public ulong UpdateInterval { get; set; }
		protected bool hasDirtyFields = false;

		/// <summary>
		/// A reference to the player who created this network object
		/// </summary>
		public NetworkingPlayer Owner { get; private set; }

		/// <summary>
		/// A static list for tracking all of the NetworkObjects that have been created on the network
		/// </summary>
		private static List<NetworkObject> networkObjects = new List<NetworkObject>();
		public static List<NetworkObject> NetworkObjects { get { return networkObjects; } }

		private static List<NetworkObject> pendingCreates = new List<NetworkObject>();

		/// <summary>
		/// The structure to store the buffered rpc data in to be sent on accepted client
		/// </summary>
		protected struct BufferedRpc
		{
			public BMSByte data;
			public Receivers receivers;
			public string methodName;
			public ulong timestep;
		}

		/// <summary>
		/// Stores a list of buffered RPC calls for this particular Network Object
		/// </summary>
		protected List<BufferedRpc> rpcBuffer = new List<BufferedRpc>();

		/// <summary>
		/// This is set to true once the client has completed it's registration process
		/// and is ready to start accepting registered RPC calls
		/// </summary>
		public bool ClientRegistered { get; private set; }

		/// <summary>
		/// Used to write dirty fields across the network without having
		/// to create a new byte[] every time
		/// </summary>
		protected BMSByte dirtyFieldsData = new BMSByte();

		/// <summary>
		/// Used to read the flags on the network, this is cached on creation
		/// so that it doesn't have to be created over and over
		/// </summary>
		protected byte[] readDirtyFlags = null;

		/// <summary>
		/// This is the cached binary data byte[] for the SendBinaryData method
		/// </summary>
		private BMSByte sendBinaryData = new BMSByte();

		public bool IsServer { get { return Networker is IServer; } }

		private Dictionary<NetworkingPlayer, int> currentRpcBufferCounts = new Dictionary<NetworkingPlayer, int>();

		/// <summary>
		/// The struct that the pending Rpc methods will be mapped to
		/// </summary>
		private struct PendingRpc
		{
			public BMSByte data;
			public Receivers receivers;
			public NetworkingPlayer sender;
			public ulong timestep;
		}

		/// <summary>
		/// This is the struct for local pending rpc's before ready
		/// </summary>
		private struct PendingLocalRPC
		{
			public NetworkingPlayer TargetPlayer;
			public string MethodName;
			public Receivers Receivers;
			public object[] Args;

			public override string ToString()
			{
				return string.Format("P [{0}], M [{1}], R [{2}], A [{3}]", TargetPlayer, MethodName, Receivers, Args.Length);
			}
		}

		/// <summary>
		/// The list of pending Rpc that will need to be executed once the client connects
		/// </summary>
		private List<PendingRpc> pendingClientRegisterRpc = new List<PendingRpc>();

		/// <summary>
		/// This is a list of pending local rpcs to be send locally by the client
		/// </summary>
		private List<PendingLocalRPC> pendingLocalRpcs = new List<PendingLocalRPC>();

		/// <summary>
		/// This constructor is used to create a dummy network object that
		/// doesn't do anything on the network and is useful to be temporary
		/// until the actual network object arrives
		/// </summary>
		public NetworkObject() { IsOwner = true; NetworkReady = false; }

		/// <summary>
		/// This creates an instance of this object and attaches it to the specified networker
		/// </summary>
		/// <param name="networker">The networker that this object is going to be attached to</param>
		/// <param name="forceId">If 0 then the first open id will be used from the networker</param>
		public NetworkObject(NetWorker networker, INetworkBehavior networkBehavior = null, int createCode = 0)
		{
			pendingBehavior = networkBehavior;
			UpdateInterval = DEFAULT_UPDATE_INTERVAL;
			CreateCode = createCode;

			Rpcs = new Dictionary<byte, Rpc>();
			Networker = networker;
			CreateNativeRpcs();

			if (networker is IServer)
				CreateObjectOnServer(null);
			else
			{
				// This is a client so it needs to request the creation by the server
				NetworkReady = false;

				BMSByte data = new BMSByte();

				// Create a hash for this object so it knows that the response from the server is
				// for this particular create and not another one
				hash = GlobalHash == -1 ? (GlobalHash += 2) : ++GlobalHash;

				CreateCode = createCode;

				// Tell this object to listen for the create network object message from the server
				objectCreateAttach += CreatedOnNetwork;
				//TODO: MOVED HERE (#1)

				ObjectMapper.Instance.MapBytes(data, UniqueIdentity, hash, CreateCode);
				WritePayload(data);

				bool useMask = networker is TCPClient;
				Binary createRequest = new Binary(CreateTimestep, useMask, data, Receivers.Server, MessageGroupIds.CREATE_NETWORK_OBJECT_REQUEST, networker is BaseTCP, ROUTER_ID);

				NetWorker.BaseNetworkEvent request = () =>
				{
					// Send the message to the server
					if (networker is UDPClient)
						((UDPClient)networker).Send(createRequest, true);
					else
						((TCPClient)networker).Send(createRequest);
				};

				if (Networker.Me == null)
					Networker.serverAccepted += request;
				else
					request();

				//TODO: FROM HERE (#1)
			}

			// Whatever called this method is the owner
			Owner = networker.Me;
			IsOwner = true;
		}

		/// <summary>
		/// Create an instance of a network object from the network
		/// </summary>
		/// <param name="networker">The NetWorker that is managing this network object</param>
		/// <param name="serverId">The id (if any) given to this object by the server</param>
		/// <param name="frame">The initialization data for this object that is assigned from the server</param>
		public NetworkObject(NetWorker networker, uint serverId, FrameStream frame)
		{
			UpdateInterval = DEFAULT_UPDATE_INTERVAL;

			Rpcs = new Dictionary<byte, Rpc>();
			Networker = networker;

			if (Networker is IServer)
				Owner = frame.Sender;
			else
				Owner = ((IClient)Networker).Server;

			CreateNativeRpcs();

			if (networker is IServer)
			{
				// Read the hash code that was created by the client so that it can be relayed back for lookup
				hash = frame.StreamData.GetBasicType<int>();
				CreateCode = frame.StreamData.GetBasicType<int>();

				ReadPayload(frame.StreamData, frame.TimeStep);

				// Let all the clients know that a new object is being created
				CreateObjectOnServer(frame.Sender);
				Binary createObject = CreateObjectOnServer(frame.Sender, hash);

				// Send the message back to the sending client so that it can finish setting up the network object
				if (networker is UDPServer)
					((UDPServer)networker).Send(frame.Sender, createObject, true);
				else
					((TCPServer)networker).Send(frame.Sender.TcpClientHandle, createObject);
			}
			else
			{
				CreateCode = frame.StreamData.GetBasicType<int>();

				Initialize(serverId, frame.TimeStep);
				ReadPayload(frame.StreamData, frame.TimeStep);

				BMSByte createdByteData = new BMSByte();
				ObjectMapper.Instance.MapBytes(createdByteData, serverId);

				Binary createdFrame = new Binary(Networker.Time.Timestep, Networker is TCPClient, createdByteData, Receivers.Server, MessageGroupIds.GetId("NO_CREATED_" + NetworkId), Networker is BaseTCP, CREATED_OBJECT_ROUTER_ID);

				if (networker is UDPClient)
					((UDPClient)networker).Send(createdFrame, true);
				else
					((TCPClient)networker).Send(createdFrame);
			}
		}

		/// <summary>
		/// Go through and setup all of the RPCs that are a base part
		/// of all network objects
		/// </summary>
		private void CreateNativeRpcs()
		{
			RegisterRpc("ClearRpcBuffer", ClearRpcBuffer);
			RegisterRpc("RemoveRpcFromBuffer", RemoveRpcFromBuffer);
			RegisterRpc("TakeOwnership", TakeOwnership);
			RegisterRpc("AssignOwnership", AssignOwnership, typeof(bool));
		}

		/// <summary>
		/// Clear all of the buffered rpcs for this network object
		/// </summary>
		public void ClearRpcBuffer()
		{
			SendRpc("ClearRpcBuffer", Receivers.Server);
		}

		/// <summary>
		/// Allows you to remove all buffered rpcs with the given method name or
		/// just the first occurance (oldest one)
		/// </summary>
		/// <param name="methodName">The name to match and remove from the buffer</param>
		/// <param name="first">If <c>True</c> then only the first buffered rpc with the specified name will be removed</param>
		private void RemoveRpcFromBuffer(string methodName, bool first = false)
		{
			SendRpc("RemoveRpcFromBuffer", Receivers.Server, methodName, first);
		}

		public void TakeOwnership()
		{
			SendRpc("TakeOwnership", Receivers.Server);
		}

		public void AssignOwnership(NetworkingPlayer targetPlayer)
		{
			// Only the server is allowed to assign ownership
			if (!IsServer)
				return;

			if (Owner == targetPlayer)
				return;

			if (targetPlayer == Networker.Me)
				AssignOwnership(new RpcArgs { Args = new object[] { true } });
			else
				SendRpc(targetPlayer, "AssignOwnership", Receivers.Target, args: new object[] { true });

			if (Owner == Networker.Me)
				AssignOwnership(new RpcArgs { Args = new object[] { false } });
			else
				SendRpc(Owner, "AssignOwnership", Receivers.Target, args: new object[] { false });

			Owner = targetPlayer;
		}

		private void TakeOwnership(RpcArgs args)
		{
			if (!IsServer)
				return;

			if (!AllowOwnershipChange(args.Info.SendingPlayer))
				return;

			AssignOwnership(args.Info.SendingPlayer);
		}

		private void AssignOwnership(RpcArgs args)
		{
			IsOwner = args.GetNext<bool>();

			if (!IsOwner)
				OwnershipChanged();
		}

		protected virtual void OwnershipChanged() { }

		/// <summary>
		/// Clear all of the buffered rpcs for this network object
		/// </summary>
		private void ClearRpcBuffer(RpcArgs args)
		{
			// Only allow the server or owner of the object to clear the buffer
			if (!IsServer && args.Info.SendingPlayer != Owner)
				return;

			lock (rpcBuffer)
			{
				rpcBuffer.Clear();
			}
		}

		/// <summary>
		/// Allows you to remove all buffered rpcs with the given method name or
		/// just the first occurance (oldest one)
		/// </summary>
		private void RemoveRpcFromBuffer(RpcArgs args)
		{
			// Only allow the server or owner of the object to remove from the buffer
			if (!IsServer && args.Info.SendingPlayer != Owner)
				return;

			string methodName = args.GetNext<string>();
			bool first = args.GetNext<bool>();

			lock (rpcBuffer)
			{
				for (int i = 0; i < rpcBuffer.Count; i++)
				{
					if (rpcBuffer[i].methodName == methodName)
					{
						rpcBuffer.RemoveAt(i--);

						if (first)
							break;
						else
							continue;
					}
				}
			}
		}

		/// <summary>
		/// A method for creating the network object on the server only and skipping any particular player
		/// which is often the player that is requesting that this object is created
		/// </summary>
		/// <param name="skipPlayer">The player to be skipped</param>
		/// <returns>The Binary frame data with all of the initialization data</returns>
		private Binary CreateObjectOnServer(NetworkingPlayer skipPlayer, int targetHash = 0)
		{
			UpdateInterval = DEFAULT_UPDATE_INTERVAL;

			// If there is a target hash, this object has already been initialized
			if (targetHash == 0)
			{
				// Register the network object
				Initialize();
			}

			// The data that is to be sent to all the clients who did not request this object to be created
			BMSByte data = new BMSByte();
			ObjectMapper.Instance.MapBytes(data, UniqueIdentity, targetHash, NetworkId, CreateCode);

			// Write all of the most up to date data for this object
			WritePayload(data);

			Binary createObject = new Binary(CreateTimestep, false, data, Receivers.All, MessageGroupIds.CREATE_NETWORK_OBJECT_REQUEST, Networker is BaseTCP, ROUTER_ID);

			if (targetHash != 0)
				return createObject;

			// If there is a target hash, we are just generating the create object frame
			if (Networker is UDPServer)
				((UDPServer)Networker).Send(createObject, true, skipPlayer);
			else
				((TCPServer)Networker).SendAll(createObject, skipPlayer);

			return createObject;
		}

		public static void PlayerAccepted(NetworkingPlayer player, NetworkObject[] networkObjects)
		{
			foreach (NetworkObject obj in networkObjects)
				obj.currentRpcBufferCounts.Add(player, obj.rpcBuffer.Count);

			Task.Queue(() =>
			{
				lock (player)
				{
					foreach (NetworkObject obj in networkObjects)
					{
						if (obj.Owner == player)
							continue;

						BMSByte targetData = new BMSByte();
						ObjectMapper.Instance.MapBytes(targetData, obj.UniqueIdentity, 0, obj.NetworkId, obj.CreateCode);

						// Write all of the most up to date data for this object
						obj.WritePayload(targetData);

						Binary targetCreateObject = new Binary(obj.CreateTimestep, false, targetData, Receivers.Target, MessageGroupIds.CREATE_NETWORK_OBJECT_REQUEST, obj.Networker is BaseTCP, ROUTER_ID);

						if (obj.Networker is UDPServer)
							((UDPServer)obj.Networker).Send(player, targetCreateObject, true);
						else
							((TCPServer)obj.Networker).Send(player.TcpClientHandle, targetCreateObject);
					}
				}
			});
		}

		public void CreateConfirmed(NetworkingPlayer player)
		{
			SendBuffer(player);
		}

		/// <summary>
		/// Finish setting up this network object on the network and fire off any complete events
		/// </summary>
		/// <param name="id">The id that was assigned from the network (if client)</param>
		/// <param name="skipCreated">The objectCreated event will be fired if <c>true</c></param>
		private void Initialize(uint id = 0, ulong timestep = 0)
		{
			// This is a server so it can create the object as normal
			NetworkReady = true;

			CreateTimestep = timestep == 0 ? Networker.Time.Timestep : timestep;

			// Register this object with the networker and obtain it's unique id
			Networker.RegisterNetworkObject(this, id);


			if (Networker.PendCreates)
			{
				lock (pendingCreates)
				{
					pendingCreates.Add(this);
				}

				return;
			}

			if (onReady != null)
				onReady();

			if (pendingBehavior != null)
			{
				pendingBehavior.Initialize(this);

				if (pendingInitialized != null)
					pendingInitialized(pendingBehavior, this);
			}
            else if (objectCreated != null)
                objectCreated(this);
		}

		public static void Flush(NetWorker target)
		{
			lock (pendingCreates)
			{
				for (int i = 0; i < pendingCreates.Count; i++)
				{
					if (objectCreated == null)
						continue;

					if (pendingCreates[i].onReady != null)
						pendingCreates[i].onReady();

                    objectCreated(pendingCreates[i]);
                    pendingCreates.RemoveAt(i--);
				}
			}

			target.PendCreates = false;
		}

		/// <summary>
		/// A method callback for the client to listen for when the object has been asynchronously created
		/// </summary>
		/// <param name="identity">The identity to describe what type / subtype of network object this is</param>
		/// <param name="hash">The hash id that was sent to match up with this hash id</param>
		/// <param name="id">The id that the server has given to this network object</param>
		/// <param name="frame">The initialization data for this network object</param>
		private void CreatedOnNetwork(int identity, int hash, uint id, FrameStream frame)
		{
			// Check to see if the identity from the network belongs to this type
			if (identity != UniqueIdentity)
				return;

			// If the hash does not belong to this object then ignore it
			if (hash != this.hash)
				return;

			Owner = Networker.Me;

			// This object has been found, remove it from listening to any more create messages
			objectCreateAttach -= CreatedOnNetwork;

			// Move the start index passed the identity bytes and the hash bytes
			frame.StreamData.MoveStartIndex(sizeof(int) * 2);

			Initialize(id);

			if (onReady != null)
				onReady();
		}

		/// <summary>
		/// This is called from the networker when the this object is created, it
		/// will contain the id for this object on the network
		/// </summary>
		/// <param name="id"></param>
		/// <returns><c>true</c> if the id has not already been assigned</returns>
		public bool RegisterOnce(uint id)
		{
			// If there already is an id for this object, ignore this request
			if (NetworkId != 0)
				return false;

			NetworkId = id;

			if (ClientRegistered)
				ClearClientPendingRPC();

			return true;
		}

		/// <summary>
		/// This will register a method to this network object as an Rpc
		/// </summary>
		/// <param name="methodName">The name of the method that is to be registered</param>
		/// <param name="callback">The callback to fire for this RPC when received a signal for it</param>
		/// <param name="argumentTypes">The types argument types for validation</param>
		public void RegisterRpc(string methodName, Action<RpcArgs> callback, params Type[] argumentTypes)
		{
			// Make sure that the method name string is unique and not already assigned
			if (rpcLookup.ContainsKey(methodName))
				throw new BaseNetworkException("The rpc " + methodName + " has already been registered");

			// Each network object is only allowed 255 registered RPC methods as the id is a byte
			if (Rpcs.Count >= byte.MaxValue)
				throw new BaseNetworkException("You are only allowed to register " + byte.MaxValue + " Rpc methods per network object");

			// The id for this RPC is goign to be the next index in the dictionary
			byte id = (byte)Rpcs.Count;
			Rpcs.Add(id, new Rpc(callback, argumentTypes));
			rpcLookup.Add(methodName, id);
			inverseRpcLookup.Add(id, methodName);
		}

		/// <summary>
		/// Called once all of the RPC methods have been registered
		/// </summary>
		public void RegistrationComplete()
		{
			ClientRegistered = true;

			if (NetworkId == 0)
				return;

			Networker.CompleteInitialization(this);

			lock (pendingClientRegisterRpc)
			{
				ClearClientPendingRPC();
			}
		}

		private void ClearClientPendingRPC()
		{
			foreach (PendingRpc rpc in pendingClientRegisterRpc)
				InvokeRpc(rpc.sender, rpc.timestep, rpc.data, rpc.receivers);

			foreach (PendingLocalRPC rpc in pendingLocalRpcs)
				SendRpc(rpc.TargetPlayer, rpc.MethodName, rpc.Receivers, false, rpc.Args);

			pendingClientRegisterRpc.Clear();
			pendingLocalRpcs.Clear();
		}

		/// <summary>
		/// Used to call a RPC on the local process
		/// </summary>
		/// <param name="data">The data sent from the network to be mapped to the RPC input arguments</param>
		/// <param name="receivers">The receivers that were supplied on by the sender</param>
		public void InvokeRpc(NetworkingPlayer sender, ulong timestep, BMSByte data, Receivers receivers)
		{
			lock (pendingClientRegisterRpc)
			{
				if (!ClientRegistered)
				{
					pendingClientRegisterRpc.Add(new PendingRpc()
					{
						data = data,
						receivers = receivers,
						sender = sender,
						timestep = timestep
					});

					return;
				}
			}

			byte methodId = data.GetBasicType<byte>();

			if (!Rpcs.ContainsKey(methodId))
				throw new BaseNetworkException("The rpc " + methodId + " was not found on this network object");

			byte behaviorFlags = data.GetBasicType<byte>();

			bool overwriteExisting = (RPC_BEHAVIOR_OVERWRITE & behaviorFlags) != 0;

			object[] args = Rpcs[methodId].ReadArgs(data);

			RpcArgs rpcArgs = new RpcArgs(args, new RPCInfo { SendingPlayer = sender, TimeStep = timestep });

			// If we are the server we need to determine if this RPC is okay to replicate
			if (Networker is IServer && receivers != Receivers.Server && receivers != Receivers.Target)
			{
				string methodName = inverseRpcLookup[methodId];

				// Validate the RPC call using the method name and the supplied arguments from the client
				// then replicate to the correct receivers
				// Do not read or replicate if the server denies replication
				if (ServerAllowRpc(methodName, receivers, rpcArgs))
					SendRpc(methodName, receivers, overwriteExisting, sender, args);

				return;
			}

			// Call the method on the client without validation
			Rpcs[methodId].Invoke(rpcArgs);
		}

		/// <summary>
		/// Called only on the server and will determine if an RPC call should be replicated
		/// </summary>
		/// <param name="methodName">The name of the RPC that is being executed</param>
		/// <param name="receivers">The receivers that are being requested</param>
		/// <param name="args">The arguments that were supplied by the client when invoked</param>
		/// <returns>If <c>true</c> the RPC will be replicated to other clients</returns>
		protected virtual bool ServerAllowRpc(string methodName, Receivers receivers, RpcArgs args)
		{
			return true;
		}

		/// <summary>
		/// Called only on the server and will determine if binary data should be replicated
		/// </summary>
		/// <param name="data">The data to be read and replicated</param>
		/// <param name="receivers">The receivers for the data to be replicated</param>
		/// <returns>If <c>true</c> the binary data will be replicated to other clients</returns>
		protected virtual bool ServerAllowBinaryData(BMSByte data, Receivers receivers)
		{
			return true;
		}

		/// <summary>
		/// Called only on the server and will determine if the ownership change request is allowed
		/// </summary>
		/// <param name="newOwner">The new player that is requesting ownership</param>
		/// <returns>If <c>true</c> then the ownership change will be allowed</returns>
		protected virtual bool AllowOwnershipChange(NetworkingPlayer newOwner)
		{
			return true;
		}

		/// <summary>
		/// This will send the current buffer to the connecting player after they have created the object
		/// </summary>
		/// <param name="player">The player that will be receiving the RPC calls</param>
		private void SendBuffer(NetworkingPlayer player)
		{
			int count;

			if (!currentRpcBufferCounts.TryGetValue(player, out count))
				return;

			currentRpcBufferCounts.Remove(player);

			lock (rpcBuffer)
			{
				for (int i = 0; i < count; i++)
					FinalizeSendRpc(rpcBuffer[i].data, rpcBuffer[i].receivers, rpcBuffer[i].methodName, rpcBuffer[i].timestep, player);
			}
		}

		/// <summary>
		/// Build the network frame (message) data for this RPC call so that it is properly
		/// delegated on the network
		/// </summary>
		/// <param name="methodName">The name of the RPC to be called</param>
		/// <param name="receivers">The clients / server to receive the message</param>
		/// <param name="args">The input arguments for the method call</param>
		public void SendRpc(string methodName, Receivers receivers, params object[] args)
		{
            SendRpc(null, methodName, false, receivers, Networker.Me, args);
		}

		/// <summary>
		/// Build the network frame (message) data for this RPC call so that it is properly
		/// delegated on the network
		/// </summary>
		/// <param name="methodName">The name of the RPC to be called</param>
		/// <param name="receivers">The clients / server to receive the message</param>
		/// <param name="replacePrevious">If <c>True</c> then the previous call to this method will be replaced with this one</param>
		/// <param name="args">The input arguments for the method call</param>
		public void SendRpc(string methodName, bool replacePrevious, Receivers receivers, params object[] args)
		{
            SendRpc(null, methodName, replacePrevious, receivers, Networker.Me, args);
		}

		/// <summary>
		/// Build the network frame (message) data for this RPC call so that it is properly
		/// delegated on the network
		/// </summary>
		/// <param name="methodName">The name of the RPC to be called</param>
		/// <param name="receivers">The clients / server to receive the message</param>
		/// <param name="sender">The sender of this rpc (to be assigned by the server)</param>
		/// <param name="args">The input arguments for the method call</param>
		public void SendRpc(string methodName, Receivers receivers, NetworkingPlayer sender, params object[] args)
		{
            SendRpc(null, methodName, false, receivers, sender, args);
		}

		/// <summary>
		/// Build the network frame (message) data for this RPC call so that it is properly
		/// delegated on the network
		/// </summary>
		/// <param name="methodName">The name of the RPC to be called</param>
		/// <param name="receivers">The clients / server to receive the message</param>
		/// <param name="replacePrevious">If <c>True</c> then the previous call to this method will be replaced with this one</param>
		/// <param name="sender">The sender of this rpc (to be assigned by the server)</param>
		/// <param name="args">The input arguments for the method call</param>
		public void SendRpc(string methodName, Receivers receivers, bool replacePrevious, NetworkingPlayer sender, params object[] args)
		{
			SendRpc(null, methodName, replacePrevious, receivers, sender, args);
		}

		/// <summary>
		/// Build the network frame (message) data for this RPC call so that it is properly
		/// delegated on the network
		/// </summary>
		/// <param name="targetPlayer">The player that is being sent this RPC from the server</param>
		/// <param name="methodName">The name of the RPC to be called</param>
		/// <param name="receivers">The clients / server to receive the message</param>
		/// <param name="args">The input arguments for the method call</param>
		public void SendRpc(NetworkingPlayer targetPlayer, string methodName, Receivers receivers, params object[] args)
		{
            SendRpc(targetPlayer, methodName, false, receivers, Networker.Me, args);
		}

		/// <summary>
		/// Build the network frame (message) data for this RPC call so that it is properly
		/// delegated on the network
		/// </summary>
		/// <param name="targetPlayer">The player that is being sent this RPC from the server</param>
		/// <param name="methodName">The name of the RPC to be called</param>
		/// <param name="receivers">The clients / server to receive the message</param>
		/// <param name="replacePrevious">If <c>True</c> then the previous call to this method will be replaced with this one</param>
		/// <param name="args">The input arguments for the method call</param>
		public void SendRpc(NetworkingPlayer targetPlayer, string methodName, bool replacePrevious, Receivers receivers, params object[] args)
		{
            SendRpc(targetPlayer, methodName, replacePrevious, receivers, Networker.Me, args);
		}

		/// <summary>
		/// Build the network frame (message) data for this RPC call so that it is properly
		/// delegated on the network
		/// </summary>
		/// <param name="targetPlayer">The target player that should receive the RPC</param>
		/// <param name="methodName">The name of the RPC to be called</param>
		/// <param name="receivers">The clients / server to receive the message</param>
		/// <param name="args">The input arguments for the method call</param>
		/// <returns></returns>
		public void SendRpc(NetworkingPlayer targetPlayer, string methodName, bool replacePrevious, Receivers receivers, NetworkingPlayer sender, params object[] args)
		{
			if (!ClientRegistered)
			{
				pendingLocalRpcs.Add(new PendingLocalRPC()
				{
					TargetPlayer = targetPlayer,
					MethodName = methodName,
					Receivers = receivers,
					Args = args
				});

				return;
			}

			// Make sure that the Rpc has been registered
			if (!rpcLookup.ContainsKey(methodName))
				throw new BaseNetworkException("The method named " + methodName + " was never registered as an Rpc");

			// Make sure that the parameters that were passed match the desired arguments
			byte id = rpcLookup[methodName];
			Rpcs[id].ValidateParameters(args);

			ulong timestep = Networker.Time.Timestep;

			// The server should execute the RPC before it is sent out to the clients
			if (Networker is IServer)
			{
				// Don't execute the RPC if the server is sending it to receivers
				// that don't include itself
				if ((sender != Networker.Me && sender != null) || receivers != Receivers.Others && receivers != Receivers.Target &&
					receivers != Receivers.OthersBuffered && receivers != Receivers.OthersProximity)
				{
					Rpcs[id].Invoke(new RpcArgs(args, new RPCInfo { SendingPlayer = sender, TimeStep = timestep }), sender == Networker.Me);
				}
			}

			BMSByte data = new BMSByte();

			// Map the id of the object into the data so that the program knows what fire from
			ObjectMapper.Instance.MapBytes(data, NetworkId);

			// Map the id of the Rpc as the second data into the byte array
			ObjectMapper.Instance.MapBytes(data, id);

			// Map the behavior flags to the rpc
			byte behaviorFlags = 0;
			behaviorFlags |= replacePrevious ? RPC_BEHAVIOR_OVERWRITE : (byte)0;
			ObjectMapper.Instance.MapBytes(data, behaviorFlags);

			// Map all of the data to bytes
			ObjectMapper.Instance.MapBytes(data, args);

			if (Networker is IServer)
			{
				// Buffered RPC messages are stored on the NetworkObject level and not on the NetWorker level
				if (receivers == Receivers.AllBuffered || receivers == Receivers.OthersBuffered)
				{
					if (receivers == Receivers.AllBuffered)
						receivers = Receivers.All;

					if (receivers == Receivers.OthersBuffered)
						receivers = Receivers.Others;

					lock (rpcBuffer)
					{
						BufferedRpc rpc = new BufferedRpc()
						{
							data = new BMSByte().Clone(data),
							receivers = receivers,
							methodName = methodName,
							timestep = timestep
						};

						bool replaced = false;
						if (replacePrevious)
						{
							for (int i = 0; i < rpcBuffer.Count; i++)
							{
								if (rpcBuffer[i].methodName == methodName)
								{
									rpcBuffer[i] = rpc;
									replaced = true;
									break;
								}
							}
						}

						if (!replaced)
						{
							// Add the RPC to the buffer to be sent on accept
							rpcBuffer.Add(rpc);
						}
					}
				}
			}

			FinalizeSendRpc(data, receivers, methodName, timestep, targetPlayer, sender);
		}

		private void FinalizeSendRpc(BMSByte data, Receivers receivers, string methodName, ulong timestep, NetworkingPlayer targetPlayer = null, NetworkingPlayer sender = null)
		{
			// Generate a binary frame with a router
			Binary rpcFrame = new Binary(timestep, Networker is TCPClient, data, receivers, MessageGroupIds.GetId("NO_RPC_" + NetworkId + "_" + methodName), Networker is BaseTCP, Rpc.ROUTER_ID);
			rpcFrame.SetSender(sender);

			if (targetPlayer != null && Networker is IServer)
			{
				if (Networker is TCPServer)
					((TCPServer)Networker).Send(targetPlayer.TcpClientHandle, rpcFrame);
				else
					((UDPServer)Networker).Send(targetPlayer, rpcFrame, true);
			}
			else
			{
				if (Networker is TCPServer)
					((TCPServer)Networker).SendAll(rpcFrame);
				else if (Networker is TCPClient)
					((TCPClient)Networker).Send(rpcFrame);
				else if (Networker is BaseUDP)
					((BaseUDP)Networker).Send(rpcFrame, true);
			}
		}

		/// <summary>
		/// Send raw binary data across the network to the associated NetworkObject
		/// </summary>
		/// <param name="data">The raw data to be sent</param>
		/// <param name="receivers">The receivers for this raw data</param>
		/// <param name="subRouter">Used to determine if this is a special type of binary data</param>
		public void SendBinaryData(BMSByte data, Receivers receivers, byte subRouter = 0, bool reliable = true, bool skipOwner = false)
		{
			NetworkingPlayer skipPlayer = null;
			if (skipOwner && IsServer)
			{
				if (Owner == Networker.Me || !AuthorityUpdateMode)
					skipPlayer = Owner;
			}

			lock (sendBinaryData)
			{
				sendBinaryData.Clear();

				// Map the id of the object into the data so that the program knows what fire from
				ObjectMapper.Instance.MapBytes(sendBinaryData, NetworkId, subRouter);

				// Map all of the data to bytes
				sendBinaryData.Append(data);

				// Generate a binary frame with a router
				Binary frame = new Binary(Networker.Time.Timestep, Networker is TCPClient, sendBinaryData, receivers, MessageGroupIds.GetId("NO_BIN_DATA_" + NetworkId), Networker is BaseTCP, BINARY_DATA_ROUTER_ID);

				if (Networker is TCPServer)
					((TCPServer)Networker).SendAll(frame, skipPlayer);
				else if (Networker is TCPClient)
					((TCPClient)Networker).Send(frame);
				else if (Networker is UDPServer)
					((UDPServer)Networker).Send(frame, reliable, skipPlayer);
				else if (Networker is UDPClient)
					((UDPClient)Networker).Send(frame, reliable);
			}
		}

		/// <summary>
		/// There has been binary data sent across the network to this NetworkObject
		/// </summary>
		/// <param name="data">The data that has been sent</param>
		/// <param name="receivers">The receivers for this data</param>
		public void ReadBinaryData(FrameStream frame)
		{
			// Get the subrouter from the binary data
			byte subRouter = frame.StreamData.GetBasicType<byte>();

			switch (subRouter)
			{
				// If the subRouter is set to be field serializations then read the new field values
				case DIRTY_FIELD_SUB_ROUTER_ID:
					// Should only read if the sending player is the owner
					if (Networker is IClient || frame.Sender == Owner)
					{
						// TODO:  Allow server to replicate as in the other example below
						// Replicate the data to the other clients
						if (Networker is IServer)
						{
							BMSByte data = new BMSByte().Clone(frame.StreamData);

							if (data != null)
								SendBinaryData(data, Receivers.All, DIRTY_FIELD_SUB_ROUTER_ID, false, true);
						}

						ReadDirtyFields(frame.StreamData, frame.TimeStep);
					}
					return;
				case DESTROY_SUB_ROUTER_ID:
					Destroy(true);
					return;
			}

			if (Networker is IServer)
			{
				// Do not read or replicate if the server denies replication
				if (ServerAllowBinaryData(frame.StreamData, frame.Receivers))
				{
					if (readBinary != null)
						readBinary(frame.StreamData);

					SendBinaryData(frame.StreamData, frame.Receivers);
				}

				return;
			}

			// Call the event on the client without validation
			if (readBinary != null)
				readBinary(frame.StreamData);
		}

		/// <summary>
		/// Used to check if there are any updates to this object, it is called in 10ms intervals
		/// </summary>
		/// <param name="timeStep">The current timestep for the server</param>
		public void HeartBeat(ulong timeStep)
		{
			if (!hasDirtyFields)
				return;

			if (timeStep - lastUpdateTimestep > UpdateInterval)
			{
				BMSByte data = SerializeDirtyFields();

				if (data != null)
					SendBinaryData(data, Receivers.All, DIRTY_FIELD_SUB_ROUTER_ID, false, true);

				hasDirtyFields = false;
				lastUpdateTimestep = timeStep;
			}
		}

		/// <summary>
		/// Called when data comes in for this network object that is needed to be read
		/// in order to update any values contained within it
		/// </summary>
		/// <param name="payload">The data from the network for this object</param>
		/// <param name="timestep">The timestep for this particular change</param>
		protected abstract void ReadPayload(BMSByte payload, ulong timestep);

		/// <summary>
		/// Used to write any data on the network for this object to keep it up to date
		/// </summary>
		/// <param name="data">The data that is going to be sent across the network</param>
		/// <returns>The same input data for method chaining</returns>
		protected abstract BMSByte WritePayload(BMSByte data);

		/// <summary>
		/// Used to write any data on the network for the various fields that have been modified on this object
		/// </summary>
		protected abstract BMSByte SerializeDirtyFields();

		/// <summary>
		/// Used to read the data from the network for changes to this object
		/// </summary>
		/// <param name="data">The data that was received for this object</param>
		/// <param name="timestep">The timestep for this particular update</param>
		protected abstract void ReadDirtyFields(BMSByte data, ulong timestep);

		/// <summary>
		/// Is called by the NetWorker when a message is received to create a NetworkObject
		/// </summary>
		/// <param name="networker">The networker that received the message to create a network object</param>
		/// <param name="player">The player that requested this object be created</param>
		/// <param name="frame">The data to initialize the object</param>
		public static void CreateNetworkObject(NetWorker networker, NetworkingPlayer player, Binary frame)
		{
			// Get the identity so that the proper type / subtype can be selected
			int identity = frame.StreamData.GetBasicType<int>();

			if (networker is IServer)
			{
				// The client is requesting to create a new networked object
				if (Factory != null)
				{
					Factory.NetworkCreateObject(networker, identity, 0, frame, (obj) =>
					{
						networkObjects.Add(obj);
					});
				}
			}
			else if (networker is IClient)
			{
				int hash = frame.StreamData.GetBasicType<int>();

				// Get the server assigned id for this network object
				uint id = frame.StreamData.GetBasicType<uint>();

				if (hash != 0)
				{
					// The server is responding to the create request
					objectCreateAttach(identity, hash, id, frame);
					return;
				}

                if (objectCreateRequested != null)
                {
                    objectCreateRequested(networker, identity, id, frame, (obj) =>
                    {
                        if (obj != null)
                            networkObjects.Add(obj);
                    });
                }

				// The server is dictating to create a new networked object
				if (Factory != null)
				{
					Factory.NetworkCreateObject(networker, identity, id, frame, (obj) =>
					{
						networkObjects.Add(obj);
					});
				}
			}
		}

		/// <summary>
		/// This is used to destroy this object on the network
		/// </summary>
		public void Destroy()
		{
			Destroy(false);
		}

		/// <summary>
		/// This is used to destroy this object on the network
		/// </summary>
		/// <param name="remoteCall">Used to knwo if this call was made over the network</param>
		private void Destroy(bool remoteCall)
		{
			if (IsOwner || (Networker is IServer && !remoteCall))
				SendBinaryData(null, Receivers.All, DESTROY_SUB_ROUTER_ID);

			if (onDestroy != null)
				onDestroy();
		}

		public virtual void InterpolateUpdate() { }
	}
}