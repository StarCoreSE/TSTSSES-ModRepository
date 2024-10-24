using DynamicAsteroids.Data.Scripts.DynamicAsteroids.AsteroidEntities;
using ProtoBuf;
using VRageMath;

[ProtoContract]
public class AsteroidState
{
    public Vector3D Position { get; set; }
    public Vector3D Velocity { get; set; }
    public Quaternion Rotation { get; set; }
    public float Size { get; set; }
    public AsteroidType Type { get; set; }
    public long EntityId { get; set; }

    public AsteroidState(AsteroidEntity asteroid)
    {
        Position = asteroid.PositionComp.GetPosition();
        Velocity = asteroid.Physics.LinearVelocity;
        Rotation = Quaternion.CreateFromRotationMatrix(asteroid.WorldMatrix);
        Size = asteroid.Properties.Diameter;
        Type = asteroid.Type;
        EntityId = asteroid.EntityId;
    }

    public bool HasChanged(AsteroidEntity asteroid)
    {
        return Vector3D.DistanceSquared(Position, asteroid.PositionComp.GetPosition()) > 0.01
               || Vector3D.DistanceSquared(Velocity, asteroid.Physics.LinearVelocity) > 0.01;
    }
}
