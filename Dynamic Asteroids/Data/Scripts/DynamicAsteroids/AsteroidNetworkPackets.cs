using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRageMath;
using static DynamicAsteroids.Data.Scripts.DynamicAsteroids.MainSession;

namespace DynamicAsteroids.Data.Scripts.DynamicAsteroids {

    [ProtoInclude(10, typeof(AsteroidUpdatePacket))]
    [ProtoInclude(11, typeof(AsteroidRemovalPacket))]
    [ProtoInclude(12, typeof(AsteroidSpawnPacket))]
    [ProtoInclude(13, typeof(ZoneUpdatePacket))]
    public abstract partial class PacketBase {
    }

    public class AsteroidNetworkPackets {

    }

    [ProtoContract]
    public class AsteroidUpdatePacket : PacketBase {
        [ProtoMember(1)]
        public Vector3D Position { get; set; }

        [ProtoMember(2)]
        public Vector3D Velocity { get; set; }

        [ProtoMember(3)]
        public Vector3D AngularVelocity { get; set; }

        [ProtoMember(4)]
        public Quaternion Rotation { get; set; }

        [ProtoMember(5)]
        public long EntityId { get; set; }
    }

    [ProtoContract]
    public class AsteroidRemovalPacket : PacketBase {
        [ProtoMember(1)]
        public long EntityId { get; set; }
    }

    [ProtoContract]
    public class AsteroidSpawnPacket : PacketBase {
        [ProtoMember(1)]
        public Vector3D Position { get; set; }

        [ProtoMember(2)]
        public float Size { get; set; }

        [ProtoMember(3)]
        public Vector3D InitialVelocity { get; set; }

        [ProtoMember(4)]
        public int AsteroidType { get; set; }

        [ProtoMember(5)]
        public long EntityId { get; set; }

        [ProtoMember(6)]
        public Quaternion InitialRotation { get; set; }
    }

    [ProtoContract]
    public class ZoneData {
        [ProtoMember(1)]
        public Vector3D Center { get; set; }

        [ProtoMember(2)]
        public double Radius { get; set; }

        [ProtoMember(3)]
        public long PlayerId { get; set; }

        [ProtoMember(4)]
        public bool IsActive { get; set; }

        [ProtoMember(5)]
        public bool IsMerged { get; set; }

        [ProtoMember(6)]
        public double CurrentSpeed { get; set; }
    }

    [ProtoContract]
    public class ZoneUpdatePacket : PacketBase {
        [ProtoMember(1)]
        public List<ZoneData> Zones { get; set; } = new List<ZoneData>();
    }

}
