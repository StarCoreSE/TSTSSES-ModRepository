using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRageMath;

namespace DynamicAsteroids {
    [ProtoContract]
    public class PlayerStatePacket : PacketBase {
        [ProtoMember(1)]
        public long PlayerId { get; set; }

        [ProtoMember(2)]
        public Vector3D Position { get; set; }

        [ProtoMember(3)]
        public double Speed { get; set; }
    }
}
