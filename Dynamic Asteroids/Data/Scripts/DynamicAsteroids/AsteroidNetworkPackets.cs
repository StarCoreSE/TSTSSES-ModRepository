using DynamicAsteroids.Data.Scripts.DynamicAsteroids.AsteroidEntities;
using ProtoBuf;
using Sandbox.ModAPI;
using System.Collections.Concurrent;
using System;
using System.Collections.Generic;
using VRageMath;
using System.Linq;


namespace DynamicAsteroids.Data.Scripts.DynamicAsteroids {
    [ProtoInclude(10, typeof(AsteroidUpdatePacket))]
    [ProtoInclude(11, typeof(AsteroidRemovalPacket))]
    [ProtoInclude(12, typeof(AsteroidSpawnPacket))]
    [ProtoInclude(13, typeof(AsteroidBatchUpdatePacket))]
    [ProtoInclude(14, typeof(ZoneUpdatePacket))]
    public abstract partial class PacketBase {}

    [ProtoContract]
    public class AsteroidState {
        [ProtoMember(1)] public Vector3D Position { get; set; }

        [ProtoMember(2)] public Vector3D Velocity { get; set; }

        [ProtoMember(3)] public Quaternion Rotation { get; set; }

        [ProtoMember(4)] public float Size { get; set; }

        [ProtoMember(5)] public AsteroidType Type { get; set; }

        [ProtoMember(6)] public long EntityId { get; set; }

        [ProtoMember(7)] public Vector3D AngularVelocity { get; set; }

        public AsteroidState()
        {}

        public AsteroidState(AsteroidEntity asteroid)
        {
            Position = asteroid.PositionComp.GetPosition();
            Velocity = asteroid.Physics.LinearVelocity;
            AngularVelocity = asteroid.Physics.AngularVelocity;
            Rotation = Quaternion.CreateFromRotationMatrix(asteroid.WorldMatrix);
            Size = asteroid.Properties.Diameter;
            Type = asteroid.Type;
            EntityId = asteroid.EntityId;
        }

        public bool HasChanged(AsteroidEntity asteroid)
        {
            return Vector3D.DistanceSquared(Position, asteroid.PositionComp.GetPosition()) > 0.01 || Vector3D.DistanceSquared(Velocity, asteroid.Physics.LinearVelocity) > 0.01;
        }
    }

    [ProtoContract]
    public class AsteroidUpdatePacket : PacketBase {
        [ProtoMember(1)] public List<AsteroidState> States { get; set; } = new List<AsteroidState>();
    }

    [ProtoContract]
    public class AsteroidSpawnPacket : PacketBase {
        [ProtoMember(1)] public Vector3D Position { get; set; }

        [ProtoMember(2)] public float Size { get; set; }

        [ProtoMember(3)] public Vector3D Velocity { get; set; }

        [ProtoMember(4)] public Vector3D AngularVelocity { get; set; }

        [ProtoMember(5)] public AsteroidType Type { get; set; }

        [ProtoMember(6)] public long EntityId { get; set; }

        [ProtoMember(7)] public Quaternion Rotation { get; set; }

        public AsteroidSpawnPacket()
        {}

        public AsteroidSpawnPacket(AsteroidEntity asteroid)
        {
            Position = asteroid.PositionComp.GetPosition();
            Size = asteroid.Properties.Diameter;
            Velocity = asteroid.Physics.LinearVelocity;
            AngularVelocity = asteroid.Physics.AngularVelocity;
            Type = asteroid.Type;
            EntityId = asteroid.EntityId;
            Rotation = Quaternion.CreateFromRotationMatrix(asteroid.WorldMatrix);
        }
    }

    [ProtoContract]
    public class AsteroidRemovalPacket : PacketBase {
        [ProtoMember(1)] public long EntityId { get; set; }
    }

    [ProtoContract]
    public class AsteroidBatchUpdatePacket : PacketBase {
        [ProtoMember(1)] public List<AsteroidState> Updates { get; set; } = new List<AsteroidState>();

        [ProtoMember(2)] public List<long> Removals { get; set; } = new List<long>();

        [ProtoMember(3)] public List<AsteroidSpawnPacket> Spawns { get; set; } = new List<AsteroidSpawnPacket>();
    }

    [ProtoContract]
    public class ZoneData {
        [ProtoMember(1)] public Vector3D Center { get; set; }

        [ProtoMember(2)] public double Radius { get; set; }

        [ProtoMember(3)] public long PlayerId { get; set; }

        [ProtoMember(4)] public bool IsActive { get; set; }

        [ProtoMember(5)] public bool IsMerged { get; set; }

        [ProtoMember(6)] public double CurrentSpeed { get; set; }
    }

    [ProtoContract]
    public class ZoneUpdatePacket : PacketBase {
        [ProtoMember(1)] public List<ZoneData> Zones { get; set; } = new List<ZoneData>();
    }

    [ProtoContract]
    public class AsteroidPacketData {
        [ProtoMember(1)] public Vector3D Position { get; set; }

        [ProtoMember(2)] public Vector3D Velocity { get; set; }

        [ProtoMember(3)] public Vector3D AngularVelocity { get; set; }

        [ProtoMember(4)] public float Size { get; set; }

        [ProtoMember(5)] public AsteroidType Type { get; set; }

        [ProtoMember(6)] public long EntityId { get; set; }

        [ProtoMember(7)] public Quaternion Rotation { get; set; }

        [ProtoMember(8)] public bool IsRemoval { get; set; }

        [ProtoMember(9)] public bool IsInitialCreation { get; set; }

        public AsteroidPacketData()
        {}// Required for protobuf

        public AsteroidPacketData(AsteroidEntity asteroid, bool isRemoval = false, bool isInitialCreation = false)
        {
            Position = asteroid.PositionComp.GetPosition();
            Velocity = asteroid.Physics.LinearVelocity;
            AngularVelocity = asteroid.Physics.AngularVelocity;
            Size = asteroid.Properties.Diameter;
            Type = asteroid.Type;
            EntityId = asteroid.EntityId;
            Rotation = Quaternion.CreateFromRotationMatrix(asteroid.WorldMatrix);
            IsRemoval = isRemoval;
            IsInitialCreation = isInitialCreation;
        }
    }

    [ProtoContract]
    public class AsteroidBatchPacket : PacketBase {
        [ProtoMember(1)] public List<AsteroidPacketData> Messages { get; set; } = new List<AsteroidPacketData>();

        public AsteroidBatchPacket()
        {}

        public AsteroidBatchPacket(IEnumerable<AsteroidEntity> asteroids)
        {
            Messages = asteroids.Select(a => new AsteroidPacketData(a)).ToList();
        }
    }

    //TODO: test this lmao

    public static class NetworkHandler {
        private static ConcurrentQueue<AsteroidUpdatePacket> _updateQueue = new ConcurrentQueue<AsteroidUpdatePacket>();
        private static ConcurrentQueue<AsteroidSpawnPacket> _spawnQueue = new ConcurrentQueue<AsteroidSpawnPacket>();
        private static ConcurrentQueue<AsteroidRemovalPacket> _removalQueue = new ConcurrentQueue<AsteroidRemovalPacket>();

        private static ConcurrentQueue<ZoneUpdatePacket> _zoneUpdateQueue = new ConcurrentQueue<ZoneUpdatePacket>();

        private const int UpdateBatchSize = 50;
        private const int SpawnBatchSize = 10;
        private const int RemovalBatchSize = 20;

        private const int ZoneUpdateBatchSize = 10;

        public static void QueueZoneUpdate(ZoneUpdatePacket packet)
        {
            _zoneUpdateQueue.Enqueue(packet);
            ProcessQueue(_zoneUpdateQueue, ZoneUpdateBatchSize, SendPacket);
        }
        public static void QueueUpdate(AsteroidEntity asteroid)
        {
            AsteroidUpdatePacket packet = new AsteroidUpdatePacket { States = new List<AsteroidState> { new AsteroidState(asteroid) } };
            _updateQueue.Enqueue(packet);
            ProcessQueue(_updateQueue, UpdateBatchSize, SendPacket);
        }

        public static void QueueSpawn(AsteroidEntity asteroid)
        {
            AsteroidSpawnPacket packet = new AsteroidSpawnPacket(asteroid);
            _spawnQueue.Enqueue(packet);
            ProcessQueue(_spawnQueue, SpawnBatchSize, SendPacket);
        }

        public static void QueueRemoval(long entityId)
        {
            AsteroidRemovalPacket packet = new AsteroidRemovalPacket { EntityId = entityId };
            _removalQueue.Enqueue(packet);
            ProcessQueue(_removalQueue, RemovalBatchSize, SendPacket);
        }

        private static void ProcessQueue<T>(ConcurrentQueue<T> queue, int batchSize, Action<T> sendAction)
        {
            int processed = 0;
            T packet;
            while(processed < batchSize && queue.TryDequeue(out packet))
            {
                try
                {
                    sendAction(packet);
                    processed++;
                }
                catch (Exception ex)
                {
                    // Log the exception with details about the calling type and specific error context
                    Log.Exception(ex, typeof(NetworkHandler), "Failed to process and send packet.");
                    queue.Enqueue(packet);// Return the packet to the queue if it fails to send
                    // Probably a bad idea
                }
            }
        }

        private static void SendPacket<T>(T packet) where T : PacketBase
        {
            try
            {
                byte[] data = MyAPIGateway.Utilities.SerializeToBinary(packet);
                MyAPIGateway.Multiplayer.SendMessageToOthers(32000, data);
            }
            catch (Exception ex)
            {
                Log.Exception(ex, typeof(NetworkHandler), "Error serializing or sending packet.");
            }
        }
    }


    [ProtoContract]
    public class AsteroidNetworkMessageContainer {

        [ProtoMember(2)] public AsteroidNetworkMessage[] Messages { get; set; }

        public AsteroidNetworkMessageContainer()
        {}

        public AsteroidNetworkMessageContainer(AsteroidNetworkMessage[] messages)
        {
            Messages = messages;
        }
    }

    [ProtoContract]
    public class AsteroidNetworkMessage {
        [ProtoMember(1)]
        public double PosX;
        [ProtoMember(2)]
        public double PosY;
        [ProtoMember(3)]
        public double PosZ;
        [ProtoMember(4)]
        public float Size;
        [ProtoMember(5)]
        public double VelX;
        [ProtoMember(6)]
        public double VelY;
        [ProtoMember(7)]
        public double VelZ;
        [ProtoMember(8)]
        public double AngVelX;
        [ProtoMember(9)]
        public double AngVelY;
        [ProtoMember(10)]
        public double AngVelZ;
        [ProtoMember(11)]
        public int Type;
        [ProtoMember(13)]
        public long EntityId;
        [ProtoMember(14)]
        public bool IsRemoval;
        [ProtoMember(15)]
        public bool IsInitialCreation;
        [ProtoMember(16)]
        public float RotX;
        [ProtoMember(17)]
        public float RotY;
        [ProtoMember(18)]
        public float RotZ;
        [ProtoMember(19)]
        public float RotW;

        public AsteroidNetworkMessage()
        {}

        public AsteroidNetworkMessage(Vector3D position, float size, Vector3D initialVelocity, Vector3D angularVelocity,
            AsteroidType type, bool isSubChunk, long entityId, bool isRemoval, bool isInitialCreation, Quaternion rotation)
        {
            try
            {
                // Validate EntityId: must be positive
                if (entityId <= 0)
                {
                    Log.Warning("Invalid EntityId in AsteroidNetworkMessage constructor: Must be a positive non-zero value.");
                    throw new ArgumentException("EntityId must be a positive non-zero value.", nameof(entityId));
                }

                // Validate Size: must be positive
                if (size <= 0)
                {
                    Log.Warning("Invalid Size in AsteroidNetworkMessage constructor: Must be a positive non-zero value.");
                    throw new ArgumentException("Size must be a positive non-zero value.", nameof(size));
                }

                // Validate Position: should be finite values
                if (!position.IsValid())
                {
                    Log.Warning("Invalid Position in AsteroidNetworkMessage constructor: Position contains NaN or Infinity values.");
                    throw new ArgumentException("Position contains invalid (NaN or Infinity) values.", nameof(position));
                }

                // Validate Initial Velocity and Angular Velocity: should be finite values
                if (!initialVelocity.IsValid())
                {
                    Log.Warning("Invalid InitialVelocity in AsteroidNetworkMessage constructor: Contains NaN or Infinity values.");
                    throw new ArgumentException("Initial velocity contains invalid (NaN or Infinity) values.", nameof(initialVelocity));
                }

                if (!angularVelocity.IsValid())
                {
                    Log.Warning("Invalid AngularVelocity in AsteroidNetworkMessage constructor: Contains NaN or Infinity values.");
                    throw new ArgumentException("Angular velocity contains invalid (NaN or Infinity) values.", nameof(angularVelocity));
                }

                // Assign Position
                PosX = position.X;
                PosY = position.Y;
                PosZ = position.Z;

                // Assign Size
                Size = size;

                // Assign Velocity
                VelX = initialVelocity.X;
                VelY = initialVelocity.Y;
                VelZ = initialVelocity.Z;

                // Assign Angular Velocity
                AngVelX = angularVelocity.X;
                AngVelY = angularVelocity.Y;
                AngVelZ = angularVelocity.Z;

                // Assign Type
                Type = (int)type;

                // Assign Entity Id, Removal, and Creation flags
                EntityId = entityId;
                IsRemoval = isRemoval;
                IsInitialCreation = isInitialCreation;

                // Assign Rotation
                RotX = rotation.X;
                RotY = rotation.Y;
                RotZ = rotation.Z;
                RotW = rotation.W;
            }
            catch (Exception ex)
            {
                // Log the exception with detailed context for debugging
                Log.Exception(ex, typeof(AsteroidNetworkMessage), "Exception occurred while initializing AsteroidNetworkMessage.");
                throw;// Re-throw the exception to ensure the error is handled at a higher level if necessary
            }
        }

        public Vector3D GetPosition() => new Vector3D(PosX, PosY, PosZ);
        public Vector3D GetVelocity() => new Vector3D(VelX, VelY, VelZ);
        public Vector3D GetAngularVelocity() => new Vector3D(AngVelX, AngVelY, AngVelZ);
        public AsteroidType GetType() => (AsteroidType)Type;
        public Quaternion GetRotation() => new Quaternion(RotX, RotY, RotZ, RotW);
    }

}